use serde::Serialize;
use std::sync::Mutex;

use tauri::AppHandle;
#[cfg(target_os = "windows")]
use tauri::Emitter;

mod spectrum;

#[derive(Serialize, Clone, Default, Debug)]
pub struct NowPlayingInfo {
    pub title: String,
    pub artist: String,
    pub album: String,
    pub thumbnail_base64: Option<String>,
    pub position_ms: u64,
    pub duration_ms: u64,
    pub is_playing: bool,
    pub has_next: bool,
    pub has_previous: bool,
}

// ---------------------------------------------------------------------------
// Thumbnail cache: only re-read the stream when the track changes
// ---------------------------------------------------------------------------

struct ThumbCache {
    key: (String, String, String),
    data: Option<String>,
}

static THUMB: Mutex<Option<ThumbCache>> = Mutex::new(None);

// ---------------------------------------------------------------------------
// Windows implementation
// ---------------------------------------------------------------------------

#[cfg(target_os = "windows")]
mod win {
    use super::*;
    use base64::Engine;
    use windows::Foundation::{EventRegistrationToken, TypedEventHandler};
    use windows::Media::Control::{
        CurrentSessionChangedEventArgs, GlobalSystemMediaTransportControlsSession,
        GlobalSystemMediaTransportControlsSessionManager,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus,
        MediaPropertiesChangedEventArgs, PlaybackInfoChangedEventArgs,
        TimelinePropertiesChangedEventArgs,
    };
    use windows::Storage::Streams::DataReader;

    fn get_session() -> Result<GlobalSystemMediaTransportControlsSession, String> {
        let manager = GlobalSystemMediaTransportControlsSessionManager::RequestAsync()
            .map_err(|e| e.to_string())?
            .get()
            .map_err(|e| e.to_string())?;

        manager
            .GetCurrentSession()
            .map_err(|e| format!("No active media session: {e}"))
    }

    fn timespan_to_ms(ts: windows::Foundation::TimeSpan) -> u64 {
        // TimeSpan.Duration is in 100-nanosecond ticks
        (ts.Duration.max(0) as u64) / 10_000
    }

    /// Normalize a raw SMTC timeline (which may report a non-zero StartTime
    /// offset) into a zero-based frame. Returns `(position_ms, duration_ms)`
    /// where position is clamped to `0..=duration`.
    pub(super) fn normalize_timeline_ms(start_ms: u64, position_ms: u64, end_ms: u64) -> (u64, u64) {
        let duration_ms = end_ms.saturating_sub(start_ms);
        let position_ms = position_ms.saturating_sub(start_ms).min(duration_ms);
        (position_ms, duration_ms)
    }

    fn read_thumbnail(
        session: &GlobalSystemMediaTransportControlsSession,
        key: &(String, String, String),
    ) -> Option<String> {
        // Check cache first
        {
            // Recover from a poisoned lock instead of skipping the cache / panicking.
            let guard = THUMB.lock().unwrap_or_else(|e| e.into_inner());
            if let Some(ref cache) = *guard {
                if cache.key == *key {
                    return cache.data.clone();
                }
            }
        }

        // Read thumbnail from stream
        let data = (|| -> Option<String> {
            let props = session.TryGetMediaPropertiesAsync().ok()?.get().ok()?;
            let thumb_ref = props.Thumbnail().ok()?;
            let stream = thumb_ref.OpenReadAsync().ok()?.get().ok()?;
            let size = stream.Size().ok()? as u32;
            if size == 0 {
                return None;
            }

            let content_type = stream.ContentType().ok()?.to_string();
            let reader = DataReader::CreateDataReader(&stream).ok()?;
            reader.LoadAsync(size).ok()?.get().ok()?;

            let mut buf = vec![0u8; size as usize];
            reader.ReadBytes(&mut buf).ok()?;

            let mime = if content_type.contains("jpeg") || content_type.contains("jpg") {
                "image/jpeg"
            } else {
                "image/png"
            };

            let b64 = base64::engine::general_purpose::STANDARD.encode(&buf);
            Some(format!("data:{mime};base64,{b64}"))
        })();

        // Update cache (recover from a poisoned lock instead of dropping the update).
        {
            let mut guard = THUMB.lock().unwrap_or_else(|e| e.into_inner());
            *guard = Some(ThumbCache {
                key: key.clone(),
                data: data.clone(),
            });
        }

        data
    }

    pub fn get_now_playing_inner() -> Result<Option<NowPlayingInfo>, String> {
        let session = match get_session() {
            Ok(s) => s,
            Err(_) => return Ok(None),
        };

        // Media properties
        let props = session
            .TryGetMediaPropertiesAsync()
            .map_err(|e| e.to_string())?
            .get()
            .map_err(|e| e.to_string())?;

        let title = props.Title().unwrap_or_default().to_string();
        let artist = props.Artist().unwrap_or_default().to_string();
        let album = props.AlbumTitle().unwrap_or_default().to_string();

        // If there's no title, treat as no media
        if title.is_empty() && artist.is_empty() {
            return Ok(None);
        }

        // Timeline: normalize to a zero-based frame. Some players report a
        // non-zero StartTime; Position/EndTime are relative to it, so we must
        // subtract StartTime to get a correct progress %, elapsed label, seek.
        let timeline = session.GetTimelineProperties().map_err(|e| e.to_string())?;
        let raw_position_ms = timespan_to_ms(timeline.Position().unwrap_or_default());
        let end_ms = timespan_to_ms(timeline.EndTime().unwrap_or_default());
        let start_ms = timespan_to_ms(timeline.StartTime().unwrap_or_default());
        let (position_ms, duration_ms) = normalize_timeline_ms(start_ms, raw_position_ms, end_ms);

        // Playback info
        let playback = session.GetPlaybackInfo().map_err(|e| e.to_string())?;
        let status = playback.PlaybackStatus().unwrap_or(
            GlobalSystemMediaTransportControlsSessionPlaybackStatus::Stopped,
        );
        let is_playing =
            status == GlobalSystemMediaTransportControlsSessionPlaybackStatus::Playing;

        let controls = playback.Controls().ok();
        let has_next = controls
            .as_ref()
            .and_then(|c| c.IsNextEnabled().ok())
            .unwrap_or(false);
        let has_previous = controls
            .as_ref()
            .and_then(|c| c.IsPreviousEnabled().ok())
            .unwrap_or(false);

        // Thumbnail (cached by track)
        let thumb_key = (title.clone(), artist.clone(), album.clone());
        let thumbnail_base64 = read_thumbnail(&session, &thumb_key);

        Ok(Some(NowPlayingInfo {
            title,
            artist,
            album,
            thumbnail_base64,
            position_ms,
            duration_ms,
            is_playing,
            has_next,
            has_previous,
        }))
    }

    pub fn music_play_pause_inner() -> Result<(), String> {
        let session = get_session()?;
        session
            .TryTogglePlayPauseAsync()
            .map_err(|e| e.to_string())?
            .get()
            .map_err(|e| e.to_string())?;
        Ok(())
    }

    pub fn music_next_inner() -> Result<(), String> {
        let session = get_session()?;
        session
            .TrySkipNextAsync()
            .map_err(|e| e.to_string())?
            .get()
            .map_err(|e| e.to_string())?;
        Ok(())
    }

    pub fn music_previous_inner() -> Result<(), String> {
        let session = get_session()?;
        session
            .TrySkipPreviousAsync()
            .map_err(|e| e.to_string())?
            .get()
            .map_err(|e| e.to_string())?;
        Ok(())
    }

    pub fn music_seek_inner(position_ms: u64) -> Result<(), String> {
        let session = get_session()?;
        // The frontend passes a zero-based frame position, but SMTC expects an
        // absolute position relative to the provider's StartTime offset. Add
        // StartTime back before seeking so non-zero-start providers seek correctly.
        let start_ms = session
            .GetTimelineProperties()
            .ok()
            .and_then(|t| t.StartTime().ok())
            .map(timespan_to_ms)
            .unwrap_or(0);
        let absolute_ms = position_ms.saturating_add(start_ms);
        // Saturate instead of overflowing on an out-of-range position from the frontend.
        let ticks = (absolute_ms as i64).saturating_mul(10_000);
        session
            .TryChangePlaybackPositionAsync(ticks)
            .map_err(|e| e.to_string())?
            .get()
            .map_err(|e| e.to_string())?;
        Ok(())
    }

    // -----------------------------------------------------------------------
    // Event-driven watcher: binds GSMTC events and emits `music://update`
    // -----------------------------------------------------------------------

    struct SessionHooks {
        session: GlobalSystemMediaTransportControlsSession,
        media_token: EventRegistrationToken,
        playback_token: EventRegistrationToken,
        timeline_token: EventRegistrationToken,
    }

    struct WatcherState {
        manager: GlobalSystemMediaTransportControlsSessionManager,
        #[allow(dead_code)]
        manager_token: EventRegistrationToken,
        hooks: Option<SessionHooks>,
        app: AppHandle,
    }

    static WATCHER: Mutex<Option<WatcherState>> = Mutex::new(None);

    fn emit_update(app: &AppHandle) {
        let info = get_now_playing_inner().ok().flatten();
        let _ = app.emit("music://update", &info);
    }

    fn bind_session(
        session: &GlobalSystemMediaTransportControlsSession,
        app: &AppHandle,
    ) -> Result<SessionHooks, String> {
        let a1 = app.clone();
        let media_handler = TypedEventHandler::<
            GlobalSystemMediaTransportControlsSession,
            MediaPropertiesChangedEventArgs,
        >::new(move |_, _| {
            emit_update(&a1);
            Ok(())
        });
        let media_token = session
            .MediaPropertiesChanged(&media_handler)
            .map_err(|e| e.to_string())?;

        let a2 = app.clone();
        let playback_handler = TypedEventHandler::<
            GlobalSystemMediaTransportControlsSession,
            PlaybackInfoChangedEventArgs,
        >::new(move |_, _| {
            emit_update(&a2);
            Ok(())
        });
        let playback_token = session
            .PlaybackInfoChanged(&playback_handler)
            .map_err(|e| e.to_string())?;

        let a3 = app.clone();
        let timeline_handler = TypedEventHandler::<
            GlobalSystemMediaTransportControlsSession,
            TimelinePropertiesChangedEventArgs,
        >::new(move |_, _| {
            emit_update(&a3);
            Ok(())
        });
        let timeline_token = session
            .TimelinePropertiesChanged(&timeline_handler)
            .map_err(|e| e.to_string())?;

        Ok(SessionHooks {
            session: session.clone(),
            media_token,
            playback_token,
            timeline_token,
        })
    }

    fn unbind_session(h: &SessionHooks) {
        let _ = h.session.RemoveMediaPropertiesChanged(h.media_token);
        let _ = h.session.RemovePlaybackInfoChanged(h.playback_token);
        let _ = h.session.RemoveTimelinePropertiesChanged(h.timeline_token);
    }

    fn rebind_current_session(state: &mut WatcherState) {
        if let Some(old) = state.hooks.take() {
            unbind_session(&old);
        }
        if let Ok(session) = state.manager.GetCurrentSession() {
            if let Ok(h) = bind_session(&session, &state.app) { state.hooks = Some(h) }
        }
    }

    pub fn start_watcher(app: AppHandle) -> Result<(), String> {
        // Recover from a poisoned lock instead of permanently disabling the watcher.
        let mut guard = WATCHER.lock().unwrap_or_else(|e| e.into_inner());
        if guard.is_some() {
            // Already running, so just re-emit so the caller gets a snapshot.
            drop(guard);
            emit_update(&app);
            return Ok(());
        }

        let manager = GlobalSystemMediaTransportControlsSessionManager::RequestAsync()
            .map_err(|e| e.to_string())?
            .get()
            .map_err(|e| e.to_string())?;

        // Rebind to whichever session becomes current (user switches app, etc.)
        let app_for_mgr = app.clone();
        let handler = TypedEventHandler::<
            GlobalSystemMediaTransportControlsSessionManager,
            CurrentSessionChangedEventArgs,
        >::new(move |_, _| {
            // Recover from a poisoned lock; this runs inside a COM event callback
            // where a panic would cross the FFI boundary.
            let mut g = WATCHER.lock().unwrap_or_else(|e| e.into_inner());
            if let Some(state) = g.as_mut() {
                rebind_current_session(state);
            }
            drop(g);
            emit_update(&app_for_mgr);
            Ok(())
        });
        let manager_token = manager
            .CurrentSessionChanged(&handler)
            .map_err(|e| e.to_string())?;

        let mut state = WatcherState {
            manager: manager.clone(),
            manager_token,
            hooks: None,
            app: app.clone(),
        };

        // Bind to whatever session is already current
        if let Ok(session) = manager.GetCurrentSession() {
            if let Ok(h) = bind_session(&session, &app) {
                state.hooks = Some(h);
            }
        }

        *guard = Some(state);
        drop(guard);

        // Kick off an initial snapshot
        emit_update(&app);
        Ok(())
    }

    #[cfg(test)]
    mod tests {
        use super::normalize_timeline_ms;

        #[test]
        fn zero_start_is_passthrough() {
            // No offset: position/duration unchanged.
            assert_eq!(normalize_timeline_ms(0, 30_000, 200_000), (30_000, 200_000));
        }

        #[test]
        fn nonzero_start_offset_is_subtracted() {
            // Provider reports StartTime=60s; a Position of 90s is really 30s in,
            // and EndTime 260s is a 200s track.
            assert_eq!(
                normalize_timeline_ms(60_000, 90_000, 260_000),
                (30_000, 200_000)
            );
        }

        #[test]
        fn position_clamped_to_duration() {
            // Stale/over-range Position must not exceed the duration.
            assert_eq!(
                normalize_timeline_ms(60_000, 300_000, 260_000),
                (200_000, 200_000)
            );
        }

        #[test]
        fn position_below_start_saturates_to_zero() {
            // Position reported below StartTime saturates to 0, never underflows.
            assert_eq!(normalize_timeline_ms(60_000, 10_000, 260_000), (0, 200_000));
        }

        #[test]
        fn end_below_start_yields_zero_duration() {
            // Degenerate frame: duration saturates to 0, position clamps to 0.
            assert_eq!(normalize_timeline_ms(100_000, 120_000, 50_000), (0, 0));
        }
    }
}

// ---------------------------------------------------------------------------
// Public API (delegates to platform impl)
// ---------------------------------------------------------------------------

#[cfg(target_os = "windows")]
pub fn get_now_playing_inner() -> Result<Option<NowPlayingInfo>, String> {
    win::get_now_playing_inner()
}

#[cfg(target_os = "windows")]
pub fn music_play_pause_inner() -> Result<(), String> {
    win::music_play_pause_inner()
}

#[cfg(target_os = "windows")]
pub fn music_next_inner() -> Result<(), String> {
    win::music_next_inner()
}

#[cfg(target_os = "windows")]
pub fn music_previous_inner() -> Result<(), String> {
    win::music_previous_inner()
}

#[cfg(target_os = "windows")]
pub fn music_seek_inner(position_ms: u64) -> Result<(), String> {
    win::music_seek_inner(position_ms)
}

#[cfg(target_os = "windows")]
pub fn start_watcher(app: AppHandle) -> Result<(), String> {
    win::start_watcher(app)
}

// Stub for non-Windows
#[cfg(not(target_os = "windows"))]
pub fn get_now_playing_inner() -> Result<Option<NowPlayingInfo>, String> {
    Ok(None)
}
#[cfg(not(target_os = "windows"))]
pub fn music_play_pause_inner() -> Result<(), String> {
    Err("Not supported on this platform".into())
}
#[cfg(not(target_os = "windows"))]
pub fn music_next_inner() -> Result<(), String> {
    Err("Not supported on this platform".into())
}
#[cfg(not(target_os = "windows"))]
pub fn music_previous_inner() -> Result<(), String> {
    Err("Not supported on this platform".into())
}
#[cfg(not(target_os = "windows"))]
pub fn music_seek_inner(_position_ms: u64) -> Result<(), String> {
    Err("Not supported on this platform".into())
}
#[cfg(not(target_os = "windows"))]
pub fn start_watcher(_app: AppHandle) -> Result<(), String> {
    Ok(())
}

// ---------------------------------------------------------------------------
// Audio spectrum (WASAPI loopback + FFT): cross-platform wrappers
// ---------------------------------------------------------------------------

pub fn start_spectrum(app: AppHandle) -> Result<(), String> {
    spectrum::start(app)
}

pub fn stop_spectrum() {
    spectrum::stop();
}
