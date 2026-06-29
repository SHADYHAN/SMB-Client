#[cfg(windows)]
mod platform {
    use std::ffi::OsStr;
    use std::iter;
    use std::os::windows::ffi::OsStrExt;
    use std::ptr;

    type Hwnd = *mut core::ffi::c_void;
    type Hglobal = *mut core::ffi::c_void;
    type Bool = i32;
    type Uint = u32;
    type SizeT = usize;

    const CF_UNICODETEXT: Uint = 13;
    const GMEM_MOVEABLE: Uint = 0x0002;

    #[link(name = "User32")]
    unsafe extern "system" {
        fn OpenClipboard(h_wnd_new_owner: Hwnd) -> Bool;
        fn EmptyClipboard() -> Bool;
        fn SetClipboardData(u_format: Uint, h_mem: Hglobal) -> Hglobal;
        fn CloseClipboard() -> Bool;
    }

    #[link(name = "Kernel32")]
    unsafe extern "system" {
        fn GlobalAlloc(u_flags: Uint, dw_bytes: SizeT) -> Hglobal;
        fn GlobalLock(h_mem: Hglobal) -> *mut core::ffi::c_void;
        fn GlobalUnlock(h_mem: Hglobal) -> Bool;
        fn GlobalFree(h_mem: Hglobal) -> Hglobal;
    }

    pub fn set_text(text: &str) -> Result<(), String> {
        let wide = OsStr::new(text)
            .encode_wide()
            .chain(iter::once(0))
            .collect::<Vec<u16>>();
        let byte_len = wide.len() * std::mem::size_of::<u16>();

        let handle = unsafe { GlobalAlloc(GMEM_MOVEABLE, byte_len) };
        if handle.is_null() {
            return Err("GlobalAlloc failed".to_string());
        }

        let locked = unsafe { GlobalLock(handle) } as *mut u16;
        if locked.is_null() {
            unsafe {
                let _ = GlobalFree(handle);
            }
            return Err("GlobalLock failed".to_string());
        }

        unsafe {
            ptr::copy_nonoverlapping(wide.as_ptr(), locked, wide.len());
            let _ = GlobalUnlock(handle);
        }

        let clipboard_opened = unsafe { OpenClipboard(ptr::null_mut()) != 0 };
        if !clipboard_opened {
            unsafe {
                let _ = GlobalFree(handle);
            }
            return Err("OpenClipboard failed".to_string());
        }

        let result = unsafe {
            let emptied = EmptyClipboard() != 0;
            let set = if emptied {
                !SetClipboardData(CF_UNICODETEXT, handle).is_null()
            } else {
                false
            };
            let _ = CloseClipboard();
            (emptied, set)
        };

        match result {
            (true, true) => Ok(()),
            (false, _) => {
                unsafe {
                    let _ = GlobalFree(handle);
                }
                Err("EmptyClipboard failed".to_string())
            }
            (true, false) => {
                unsafe {
                    let _ = GlobalFree(handle);
                }
                Err("SetClipboardData failed".to_string())
            }
        }
    }
}

#[cfg(not(windows))]
mod platform {
    pub fn set_text(_text: &str) -> Result<(), String> {
        Ok(())
    }
}

pub fn set_text(text: &str) -> Result<(), String> {
    platform::set_text(text)
}
