//! IDropSource COM implementation.

use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::System::Ole::*;
use windows::Win32::System::SystemServices::MODIFIERKEYS_FLAGS;

const MK_LBUTTON: u32 = 0x0001;

#[implement(IDropSource)]
pub struct ThemiaDropSource;

impl ThemiaDropSource {
    // COM factory: windows-rs `#[implement]` types are handed to the system as
    // their interface (`IDropSource`), not as `Self`.
    #[allow(clippy::new_ret_no_self)]
    pub fn new() -> IDropSource {
        let obj = ThemiaDropSource;
        obj.into()
    }
}

impl IDropSource_Impl for ThemiaDropSource_Impl {
    fn QueryContinueDrag(&self, fescapepressed: BOOL, grfkeystate: MODIFIERKEYS_FLAGS) -> HRESULT {
        if fescapepressed.as_bool() {
            return DRAGDROP_S_CANCEL;
        }
        if (grfkeystate.0 & MK_LBUTTON) == 0 {
            return DRAGDROP_S_DROP;
        }
        S_OK
    }

    fn GiveFeedback(&self, _dweffect: DROPEFFECT) -> HRESULT {
        DRAGDROP_S_USEDEFAULTCURSORS
    }
}
