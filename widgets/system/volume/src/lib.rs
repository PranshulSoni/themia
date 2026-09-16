//! Default-output-device master volume + mute, via WASAPI
//! (`IAudioEndpointVolume`). All COM interfaces are refcounted smart pointers
//! that release on drop (RAII); `ComGuard` balances CoInitialize/CoUninitialize
//! on every path. No `.unwrap()`: every fallible call returns `Result<_, String>`.

use serde::Serialize;

#[derive(Serialize, Clone, Copy)]
pub struct VolumeState {
    /// 0.0..=1.0 scalar master volume.
    pub level: f32,
    pub muted: bool,
}

#[cfg(target_os = "windows")]
mod imp {
    use super::VolumeState;
    use windows::core::Result as WinResult;
    use windows::Win32::Media::Audio::Endpoints::IAudioEndpointVolume;
    use windows::Win32::Media::Audio::{
        eMultimedia, eRender, IMMDeviceEnumerator, MMDeviceEnumerator,
    };
    use windows::Win32::System::Com::{
        CoCreateInstance, CoInitializeEx, CoUninitialize, CLSCTX_ALL, COINIT_MULTITHREADED,
    };

    /// Balances COM init/uninit for the current thread on all return paths.
    struct ComGuard;
    impl ComGuard {
        fn new() -> Self {
            // S_FALSE (already initialized on this thread) is fine; we still
            // uninit on drop to keep the refcount balanced.
            unsafe {
                let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
            }
            ComGuard
        }
    }
    impl Drop for ComGuard {
        fn drop(&mut self) {
            unsafe { CoUninitialize() };
        }
    }

    fn endpoint_volume() -> WinResult<IAudioEndpointVolume> {
        unsafe {
            let enumerator: IMMDeviceEnumerator =
                CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;
            let device = enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia)?;
            let volume: IAudioEndpointVolume = device.Activate(CLSCTX_ALL, None)?;
            Ok(volume)
        }
    }

    pub fn get_volume() -> Result<VolumeState, String> {
        let _guard = ComGuard::new();
        let volume = endpoint_volume().map_err(|e| e.to_string())?;
        unsafe {
            let level = volume
                .GetMasterVolumeLevelScalar()
                .map_err(|e| e.to_string())?;
            let muted = volume.GetMute().map_err(|e| e.to_string())?.as_bool();
            Ok(VolumeState { level, muted })
        }
    }

    pub fn set_volume(level: f32) -> Result<(), String> {
        let _guard = ComGuard::new();
        let volume = endpoint_volume().map_err(|e| e.to_string())?;
        let clamped = level.clamp(0.0, 1.0);
        unsafe {
            volume
                .SetMasterVolumeLevelScalar(clamped, std::ptr::null())
                .map_err(|e| e.to_string())?;
        }
        Ok(())
    }

    pub fn set_mute(muted: bool) -> Result<(), String> {
        let _guard = ComGuard::new();
        let volume = endpoint_volume().map_err(|e| e.to_string())?;
        unsafe {
            volume
                .SetMute(muted, std::ptr::null())
                .map_err(|e| e.to_string())?;
        }
        Ok(())
    }
}

#[cfg(target_os = "windows")]
pub fn get_volume() -> Result<VolumeState, String> {
    imp::get_volume()
}

#[cfg(target_os = "windows")]
pub fn set_volume(level: f32) -> Result<(), String> {
    imp::set_volume(level)
}

#[cfg(target_os = "windows")]
pub fn set_mute(muted: bool) -> Result<(), String> {
    imp::set_mute(muted)
}

// Non-Windows stubs so the workspace type-checks on other platforms.
#[cfg(not(target_os = "windows"))]
pub fn get_volume() -> Result<VolumeState, String> {
    Err("volume control is only supported on Windows".into())
}
#[cfg(not(target_os = "windows"))]
pub fn set_volume(_level: f32) -> Result<(), String> {
    Err("volume control is only supported on Windows".into())
}
#[cfg(not(target_os = "windows"))]
pub fn set_mute(_muted: bool) -> Result<(), String> {
    Err("volume control is only supported on Windows".into())
}
