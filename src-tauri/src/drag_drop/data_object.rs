//! IDataObject COM implementation for OLE drag/drop.

use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::System::Com::*;
use windows::Win32::System::Memory::*;
use windows::Win32::System::Ole::DROPEFFECT_MOVE;

use super::formats;

// ---------------------------------------------------------------------------
// Data item: one clipboard format + its serialized bytes
// ---------------------------------------------------------------------------

struct DataItem {
    format: FORMATETC,
    data: Vec<u8>,
}

// ---------------------------------------------------------------------------
// IDataObject implementation
// ---------------------------------------------------------------------------

#[implement(IDataObject)]
pub struct ThemiaDataObject {
    items: Vec<DataItem>,
}

impl ThemiaDataObject {
    /// Create a data object holding file paths (CF_HDROP).
    pub fn for_files(paths: &[String]) -> IDataObject {
        let obj = ThemiaDataObject {
            items: vec![
                DataItem {
                    format: formats::hdrop_format(),
                    data: formats::build_hdrop_bytes(paths),
                },
                // Suggest MOVE so the desktop/Explorer doesn't default a drop from
                // our (non-Explorer) source to COPY; Windows still falls back to
                // copy across drives, matching Explorer's same-drive=move behavior.
                DataItem {
                    format: formats::preferred_drop_effect_format(),
                    data: formats::build_drop_effect_bytes(DROPEFFECT_MOVE.0),
                },
            ],
        };
        obj.into()
    }

    /// Create a data object holding a widget JSON snapshot.
    pub fn for_widget(json: &str) -> IDataObject {
        let bytes = formats::build_widget_bytes(json);
        let obj = ThemiaDataObject {
            items: vec![DataItem {
                format: formats::widget_format(),
                data: bytes,
            }],
        };
        obj.into()
    }

    /// Create a data object holding both widget data and file paths.
    pub fn for_widget_and_files(json: &str, paths: &[String]) -> IDataObject {
        let obj = ThemiaDataObject {
            items: vec![
                DataItem {
                    format: formats::widget_format(),
                    data: formats::build_widget_bytes(json),
                },
                DataItem {
                    format: formats::hdrop_format(),
                    data: formats::build_hdrop_bytes(paths),
                },
                DataItem {
                    format: formats::preferred_drop_effect_format(),
                    data: formats::build_drop_effect_bytes(DROPEFFECT_MOVE.0),
                },
            ],
        };
        obj.into()
    }

    fn find_item(&self, fmt: &FORMATETC) -> Option<&DataItem> {
        self.items.iter().find(|item| {
            item.format.cfFormat == fmt.cfFormat
                && (item.format.tymed & fmt.tymed) != 0
        })
    }

    // STGMEDIUM carries a union (`u.hGlobal`) that can't be set in a struct
    // literal, so default-then-assign is the idiomatic build.
    #[allow(clippy::field_reassign_with_default)]
    fn make_hglobal_medium(&self, data: &[u8]) -> Result<STGMEDIUM> {
        unsafe {
            let hglobal = GlobalAlloc(GMEM_MOVEABLE, data.len())
                .map_err(|_| Error::from(E_OUTOFMEMORY))?;
            let ptr = GlobalLock(hglobal);
            if ptr.is_null() {
                let _ = GlobalFree(Some(hglobal));
                return Err(E_OUTOFMEMORY.into());
            }
            std::ptr::copy_nonoverlapping(data.as_ptr(), ptr as *mut u8, data.len());
            GlobalUnlock(hglobal).ok();

            let mut medium = STGMEDIUM::default();
            medium.tymed = TYMED_HGLOBAL.0 as u32;
            medium.u.hGlobal = hglobal;
            medium.pUnkForRelease = std::mem::ManuallyDrop::new(None);
            Ok(medium)
        }
    }
}

impl IDataObject_Impl for ThemiaDataObject_Impl {
    fn GetData(&self, pformatetcin: *const FORMATETC) -> Result<STGMEDIUM> {
        if pformatetcin.is_null() {
            return Err(E_INVALIDARG.into());
        }
        let fmt = unsafe { &*pformatetcin };
        match self.find_item(fmt) {
            Some(item) => self.make_hglobal_medium(&item.data),
            None => Err(DV_E_FORMATETC.into()),
        }
    }

    fn GetDataHere(&self, _pformatetc: *const FORMATETC, _pmedium: *mut STGMEDIUM) -> Result<()> {
        Err(E_NOTIMPL.into())
    }

    fn QueryGetData(&self, pformatetc: *const FORMATETC) -> HRESULT {
        if pformatetc.is_null() {
            return E_INVALIDARG;
        }
        let fmt = unsafe { &*pformatetc };
        if self.find_item(fmt).is_some() {
            S_OK
        } else {
            DV_E_FORMATETC
        }
    }

    fn GetCanonicalFormatEtc(
        &self,
        _pformatectin: *const FORMATETC,
        pformatetcout: *mut FORMATETC,
    ) -> HRESULT {
        if pformatetcout.is_null() {
            return E_INVALIDARG;
        }
        unsafe {
            (*pformatetcout).ptd = std::ptr::null_mut();
        }
        DATA_S_SAMEFORMATETC
    }

    fn SetData(
        &self,
        _pformatetc: *const FORMATETC,
        _pmedium: *const STGMEDIUM,
        _frelease: BOOL,
    ) -> Result<()> {
        Err(E_NOTIMPL.into())
    }

    fn EnumFormatEtc(&self, dwdirection: u32) -> Result<IEnumFORMATETC> {
        if dwdirection != DATADIR_GET.0 as u32 {
            return Err(E_NOTIMPL.into());
        }
        let fmts: Vec<FORMATETC> = self.items.iter().map(|i| i.format).collect();
        let enumerator = ThemiaEnumFormatEtc {
            formats: fmts,
            index: std::sync::atomic::AtomicUsize::new(0),
        };
        Ok(enumerator.into())
    }

    fn DAdvise(
        &self,
        _pformatetc: *const FORMATETC,
        _advf: u32,
        _padvsink: Ref<'_, IAdviseSink>,
    ) -> Result<u32> {
        Err(OLE_E_ADVISENOTSUPPORTED.into())
    }

    fn DUnadvise(&self, _dwconnection: u32) -> Result<()> {
        Err(OLE_E_ADVISENOTSUPPORTED.into())
    }

    fn EnumDAdvise(&self) -> Result<IEnumSTATDATA> {
        Err(OLE_E_ADVISENOTSUPPORTED.into())
    }
}

// ---------------------------------------------------------------------------
// IEnumFORMATETC implementation
// ---------------------------------------------------------------------------

#[implement(IEnumFORMATETC)]
struct ThemiaEnumFormatEtc {
    formats: Vec<FORMATETC>,
    index: std::sync::atomic::AtomicUsize,
}

impl IEnumFORMATETC_Impl for ThemiaEnumFormatEtc_Impl {
    fn Next(
        &self,
        celt: u32,
        rgelt: *mut FORMATETC,
        pceltfetched: *mut u32,
    ) -> HRESULT {
        let idx = self.index.load(std::sync::atomic::Ordering::Relaxed);
        let remaining = self.formats.len().saturating_sub(idx);
        let to_copy = (celt as usize).min(remaining);

        if to_copy > 0 && rgelt.is_null() {
            return E_INVALIDARG;
        }

        unsafe {
            for i in 0..to_copy {
                *rgelt.add(i) = self.formats[idx + i];
            }
            if !pceltfetched.is_null() {
                *pceltfetched = to_copy as u32;
            }
        }
        self.index.store(idx + to_copy, std::sync::atomic::Ordering::Relaxed);

        if to_copy == celt as usize { S_OK } else { S_FALSE }
    }

    fn Skip(&self, celt: u32) -> Result<()> {
        let idx = self.index.load(std::sync::atomic::Ordering::Relaxed);
        let new_idx = (idx + celt as usize).min(self.formats.len());
        self.index.store(new_idx, std::sync::atomic::Ordering::Relaxed);
        if new_idx - idx == celt as usize {
            Ok(())
        } else {
            Err(S_FALSE.into())
        }
    }

    fn Reset(&self) -> Result<()> {
        self.index.store(0, std::sync::atomic::Ordering::Relaxed);
        Ok(())
    }

    fn Clone(&self) -> Result<IEnumFORMATETC> {
        let cloned = ThemiaEnumFormatEtc {
            formats: self.formats.clone(),
            index: std::sync::atomic::AtomicUsize::new(
                self.index.load(std::sync::atomic::Ordering::Relaxed),
            ),
        };
        Ok(cloned.into())
    }
}
