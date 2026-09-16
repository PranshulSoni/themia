//! Audio spectrum via WASAPI loopback capture + FFT.
//!
//! A dedicated capture thread records the default render endpoint in loopback
//! mode, runs a windowed FFT over a sliding sample window, groups the magnitude
//! spectrum into log-spaced bands, and emits `music://spectrum` (a `Vec<f32>` of
//! 0..1 band levels) at ~30 fps. The thread body is wrapped in `catch_unwind`
//! and COM is balanced via a drop guard; the stop flag is an `AtomicBool`.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::thread::JoinHandle;
use tauri::{AppHandle, Emitter};

const FFT_SIZE: usize = 1024;
pub const BANDS: usize = 24;

struct SpectrumState {
    stop: Arc<AtomicBool>,
    handle: Option<JoinHandle<()>>,
    /// Live subscriber count. The capture thread is shared across every widget
    /// instance, so it must outlive each individual one: start() adds a ref,
    /// stop() drops one, and the thread is torn down only at the last ref.
    refs: usize,
}

static SPECTRUM: Mutex<Option<SpectrumState>> = Mutex::new(None);

/// What start() should do, given the currently-tracked worker (if any) as
/// `(worker_alive, refs)`. Pure so the ref-count contract is unit-testable
/// without spawning a real WASAPI capture thread.
#[derive(Debug, PartialEq, Eq)]
enum StartPlan {
    /// Worker is alive, so just register one more subscriber.
    AddRef,
    /// No live worker, so spawn one, starting at this many refs.
    Spawn { refs: usize },
}

fn plan_start(existing: Option<(bool, usize)>) -> StartPlan {
    match existing {
        // Genuinely running: another subscriber (2nd widget instance, or the
        // new instance of a config-driven remount) joins the shared thread.
        Some((true, _)) => StartPlan::AddRef,
        // Dead worker: carry the prior subscriber count forward so a later
        // stop() still balances, and add this caller. A finished handle must
        // never make start() a no-op (that wedges the spectrum off until a full
        // app restart).
        Some((false, refs)) => StartPlan::Spawn { refs: refs + 1 },
        None => StartPlan::Spawn { refs: 1 },
    }
}

/// What stop() should do given the current ref count. Pure for the same reason.
#[derive(Debug, PartialEq, Eq)]
enum StopPlan {
    /// Subscribers remain, so keep the thread, this many refs left.
    Keep { refs: usize },
    /// Last subscriber left, so tear the capture thread down.
    Teardown,
}

fn plan_stop(refs: usize) -> StopPlan {
    match refs.saturating_sub(1) {
        0 => StopPlan::Teardown,
        n => StopPlan::Keep { refs: n },
    }
}

pub fn start(app: AppHandle) -> Result<(), String> {
    let mut guard = SPECTRUM.lock().unwrap_or_else(|e| e.into_inner());
    let existing = guard
        .as_ref()
        .map(|s| (s.handle.as_ref().is_some_and(|h| !h.is_finished()), s.refs));
    let prev_refs = match plan_start(existing) {
        StartPlan::AddRef => {
            guard.as_mut().unwrap().refs += 1;
            return Ok(());
        }
        StartPlan::Spawn { refs } => refs,
    };
    let stop = Arc::new(AtomicBool::new(false));
    let stop_thread = stop.clone();
    let handle = std::thread::spawn(move || {
        // A capture-thread panic must never cross into the runtime.
        let _ = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
            run_capture(&app, &stop_thread);
        }));
    });
    *guard = Some(SpectrumState { stop, handle: Some(handle), refs: prev_refs });
    Ok(())
}

/// Self-healing capture supervisor. A single WASAPI session dies when the
/// default render endpoint changes (you switch output device / plug headphones),
/// the mix format changes, or the device is transiently invalidated. Letting the
/// thread exit on that error freezes the bars forever; instead we re-acquire the
/// endpoint and retry until the stop flag is set.
#[cfg(target_os = "windows")]
fn run_capture(app: &AppHandle, stop: &AtomicBool) {
    while !stop.load(Ordering::SeqCst) {
        match capture_loop(app, stop) {
            Ok(()) => break, // clean exit: stop was requested
            Err(e) => {
                let _ = app.emit("music://spectrum-error", e);
                // Backoff so a persistent failure can't spin the CPU, while
                // staying responsive to stop.
                for _ in 0..50 {
                    if stop.load(Ordering::SeqCst) {
                        return;
                    }
                    std::thread::sleep(std::time::Duration::from_millis(10));
                }
            }
        }
    }
}

#[cfg(not(target_os = "windows"))]
fn run_capture(app: &AppHandle, _stop: &AtomicBool) {
    if let Err(e) = capture_loop(app, _stop) {
        let _ = app.emit("music://spectrum-error", e);
    }
}

pub fn stop() {
    let mut guard = SPECTRUM.lock().unwrap_or_else(|e| e.into_inner());
    let Some(state) = guard.as_mut() else { return };
    match plan_stop(state.refs) {
        StopPlan::Keep { refs } => {
            state.refs = refs; // other widget instances still want it running
            return;
        }
        StopPlan::Teardown => {}
    }
    // Last subscriber left, so tear the capture thread down.
    if let Some(mut done) = guard.take() {
        done.stop.store(true, Ordering::SeqCst);
        if let Some(h) = done.handle.take() {
            let _ = h.join();
        }
    }
}

#[cfg(target_os = "windows")]
unsafe fn read_sample(base: *const u8, index: usize, bits: usize) -> f32 {
    match bits {
        32 => *(base as *const f32).add(index),          // shared-mode mix format is IEEE float
        16 => *(base as *const i16).add(index) as f32 / 32768.0,
        _ => 0.0,
    }
}

fn to_bands(mags: &[f32]) -> Vec<f32> {
    let half = mags.len();
    let mut bands = vec![0.0f32; BANDS];
    if half < 2 {
        return bands;
    }
    let min_bin = 1.0f32;
    let max_bin = half as f32;
    for (b, band) in bands.iter_mut().enumerate() {
        let lo = (min_bin * (max_bin / min_bin).powf(b as f32 / BANDS as f32)) as usize;
        let hi = ((min_bin * (max_bin / min_bin).powf((b + 1) as f32 / BANDS as f32)) as usize)
            .max(lo + 1)
            .min(half);
        let mut sum = 0.0f32;
        let mut n = 0u32;
        for m in mags.iter().take(hi).skip(lo) {
            sum += *m;
            n += 1;
        }
        let avg = if n > 0 { sum / n as f32 } else { 0.0 };
        // sqrt compression + gain, clamped to 0..1.
        *band = ((avg / FFT_SIZE as f32).sqrt() * 3.5).min(1.0);
    }
    bands
}

#[cfg(target_os = "windows")]
fn capture_loop(app: &AppHandle, stop: &AtomicBool) -> Result<(), String> {
    use core::ffi::c_void;
    use rustfft::{num_complex::Complex, FftPlanner};
    use windows::Win32::Media::Audio::{
        eConsole, eRender, IAudioCaptureClient, IAudioClient, IMMDeviceEnumerator,
        MMDeviceEnumerator, AUDCLNT_BUFFERFLAGS_SILENT, AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_LOOPBACK,
    };
    use windows::Win32::System::Com::{
        CoCreateInstance, CoInitializeEx, CoTaskMemFree, CoUninitialize, CLSCTX_ALL,
        COINIT_MULTITHREADED,
    };

    struct ComGuard;
    impl Drop for ComGuard {
        fn drop(&mut self) {
            unsafe { CoUninitialize() };
        }
    }

    unsafe {
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
        let _com = ComGuard;

        let enumerator: IMMDeviceEnumerator =
            CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL).map_err(|e| e.to_string())?;
        let device = enumerator
            .GetDefaultAudioEndpoint(eRender, eConsole)
            .map_err(|e| e.to_string())?;
        let client: IAudioClient = device.Activate(CLSCTX_ALL, None).map_err(|e| e.to_string())?;

        let pwfx = client.GetMixFormat().map_err(|e| e.to_string())?;
        let channels = (*pwfx).nChannels as usize;
        let bits = (*pwfx).wBitsPerSample as usize;

        client
            .Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK,
                2_000_000, // 200 ms buffer (hundred-ns units)
                0,
                pwfx,
                None,
            )
            .map_err(|e| e.to_string())?;
        // Format header consumed by Initialize + fields copied out, so free it now
        // so no leak on any later error path.
        CoTaskMemFree(Some(pwfx as *const c_void));

        let capture: IAudioCaptureClient = client.GetService().map_err(|e| e.to_string())?;
        client.Start().map_err(|e| e.to_string())?;

        let mut planner = FftPlanner::<f32>::new();
        let fft = planner.plan_fft_forward(FFT_SIZE);
        let window: Vec<f32> = (0..FFT_SIZE)
            .map(|i| 0.5 - 0.5 * (2.0 * std::f32::consts::PI * i as f32 / (FFT_SIZE as f32 - 1.0)).cos())
            .collect();

        let mut ring: Vec<f32> = Vec::with_capacity(FFT_SIZE * 2);
        let mut last_emit = std::time::Instant::now();

        let result = (|| -> Result<(), String> {
            while !stop.load(Ordering::SeqCst) {
                let mut packet = capture.GetNextPacketSize().map_err(|e| e.to_string())?;
                while packet > 0 {
                    let mut pdata: *mut u8 = std::ptr::null_mut();
                    let mut num_frames: u32 = 0;
                    let mut flags: u32 = 0;
                    capture
                        .GetBuffer(&mut pdata, &mut num_frames, &mut flags, None, None)
                        .map_err(|e| e.to_string())?;
                    let silent = (flags & AUDCLNT_BUFFERFLAGS_SILENT.0 as u32) != 0;
                    let frames = num_frames as usize;
                    if !pdata.is_null() && frames > 0 {
                        for f in 0..frames {
                            let mut s = 0.0f32;
                            if !silent {
                                for c in 0..channels {
                                    s += read_sample(pdata, f * channels + c, bits);
                                }
                                s /= channels.max(1) as f32;
                            }
                            ring.push(s);
                        }
                    }
                    capture.ReleaseBuffer(num_frames).map_err(|e| e.to_string())?;
                    packet = capture.GetNextPacketSize().map_err(|e| e.to_string())?;
                }

                if ring.len() >= FFT_SIZE && last_emit.elapsed().as_millis() >= 33 {
                    let start = ring.len() - FFT_SIZE;
                    let mut buf: Vec<Complex<f32>> = (0..FFT_SIZE)
                        .map(|i| Complex {
                            re: ring[start + i] * window[i],
                            im: 0.0,
                        })
                        .collect();
                    fft.process(&mut buf);
                    let mags: Vec<f32> = buf[..FFT_SIZE / 2]
                        .iter()
                        .map(|c| (c.re * c.re + c.im * c.im).sqrt())
                        .collect();
                    let _ = app.emit("music://spectrum", to_bands(&mags));
                    last_emit = std::time::Instant::now();
                    if ring.len() > FFT_SIZE * 2 {
                        ring.drain(0..ring.len() - FFT_SIZE);
                    }
                }

                std::thread::sleep(std::time::Duration::from_millis(8));
            }
            Ok(())
        })();

        let _ = client.Stop();
        result
    }
}

#[cfg(not(target_os = "windows"))]
fn capture_loop(_app: &AppHandle, _stop: &AtomicBool) -> Result<(), String> {
    Err("audio spectrum is only supported on Windows".into())
}

#[cfg(test)]
mod tests {
    use super::*;

    // [S-SPEC-05] The shared capture thread is reference-counted so a second
    // widget instance / view switch / config-driven remount can't kill the
    // spectrum for the instances that remain.

    #[test]
    fn s_spec_05_first_subscriber_spawns_with_one_ref() {
        assert_eq!(plan_start(None), StartPlan::Spawn { refs: 1 });
    }

    #[test]
    fn s_spec_05_extra_subscriber_on_live_worker_only_adds_ref() {
        // A live worker is shared, not duplicated.
        assert_eq!(plan_start(Some((true, 1))), StartPlan::AddRef);
        assert_eq!(plan_start(Some((true, 3))), StartPlan::AddRef);
    }

    #[test]
    fn s_spec_05_dead_worker_respawns_carrying_refs_forward() {
        // Regression: a finished handle must NOT make start() a no-op, and the
        // prior subscriber count must survive the respawn so a later stop()
        // still balances. Pre-fix start() short-circuited on any Some(state).
        assert_eq!(plan_start(Some((false, 0))), StartPlan::Spawn { refs: 1 });
        assert_eq!(plan_start(Some((false, 2))), StartPlan::Spawn { refs: 3 });
    }

    #[test]
    fn s_spec_05_stop_keeps_thread_while_other_subscribers_remain() {
        // Regression: pre-fix stop() tore the thread down unconditionally, so an
        // unmounting instance killed the spectrum for the ones still showing it.
        assert_eq!(plan_stop(3), StopPlan::Keep { refs: 2 });
        assert_eq!(plan_stop(2), StopPlan::Keep { refs: 1 });
    }

    #[test]
    fn s_spec_05_stop_tears_down_at_last_subscriber() {
        assert_eq!(plan_stop(1), StopPlan::Teardown);
        // Defensive: an over-decrement (leaked stop) never underflows.
        assert_eq!(plan_stop(0), StopPlan::Teardown);
    }
}
