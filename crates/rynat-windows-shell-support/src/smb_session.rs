use thiserror::Error;

use crate::explorer::unc_path;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SmbSessionConnectRequest {
    pub host: String,
    pub share: Option<String>,
    pub username: Option<String>,
    pub password: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Error)]
pub enum SmbSessionError {
    #[error("server host is required")]
    MissingHost,
    #[error("Windows SMB session setup is only available on Windows")]
    UnsupportedPlatform,
    #[error("Windows SMB session setup failed: {0}")]
    Windows(String),
}

pub type SmbSessionResult<T> = Result<T, SmbSessionError>;

pub trait SmbSessionConnector {
    fn connect(&self, request: &SmbSessionConnectRequest) -> SmbSessionResult<()>;
    fn disconnect(&self, host: &str) -> SmbSessionResult<()>;
    fn unc_root(&self, host: &str, share: Option<&str>) -> SmbSessionResult<String>;
}

pub struct UnsupportedSmbSessionConnector;

impl SmbSessionConnector for UnsupportedSmbSessionConnector {
    fn connect(&self, _request: &SmbSessionConnectRequest) -> SmbSessionResult<()> {
        Err(SmbSessionError::UnsupportedPlatform)
    }

    fn disconnect(&self, _host: &str) -> SmbSessionResult<()> {
        Err(SmbSessionError::UnsupportedPlatform)
    }

    fn unc_root(&self, host: &str, share: Option<&str>) -> SmbSessionResult<String> {
        format_unc_root(host, share)
    }
}

pub fn format_unc_root(host: &str, share: Option<&str>) -> SmbSessionResult<String> {
    let host = host.trim();
    if host.is_empty() {
        return Err(SmbSessionError::MissingHost);
    }

    match share.map(str::trim).filter(|value| !value.is_empty()) {
        Some(share) => Ok(unc_path(host, share, "/")),
        None => Ok(format!(r"\\{host}")),
    }
}

#[cfg(windows)]
pub mod windows {
    use super::*;
    use std::ffi::OsStr;
    use std::iter;
    use std::os::windows::ffi::OsStrExt;
    use std::ptr;

    const NO_ERROR: u32 = 0;
    const RESOURCETYPE_DISK: u32 = 1;
    const CONNECT_TEMPORARY: u32 = 0x0000_0004;

    #[repr(C)]
    struct NetResourceW {
        dw_scope: u32,
        dw_type: u32,
        dw_display_type: u32,
        dw_usage: u32,
        lp_local_name: *mut u16,
        lp_remote_name: *mut u16,
        lp_comment: *mut u16,
        lp_provider: *mut u16,
    }

    #[link(name = "Mpr")]
    unsafe extern "system" {
        fn WNetAddConnection2W(
            lp_net_resource: *const NetResourceW,
            lp_password: *const u16,
            lp_user_name: *const u16,
            dw_flags: u32,
        ) -> u32;

        fn WNetCancelConnection2W(lp_name: *const u16, dw_flags: u32, force: i32) -> u32;
    }

    pub struct WindowsSmbSessionConnector;

    impl SmbSessionConnector for WindowsSmbSessionConnector {
        fn connect(&self, request: &SmbSessionConnectRequest) -> SmbSessionResult<()> {
            let remote_name = format_unc_root(&request.host, request.share.as_deref())?;
            let remote_name_w = wide_null(&remote_name);
            let username_w = request.username.as_ref().map(wide_null);
            let password_w = request.password.as_ref().map(wide_null);

            let resource = NetResourceW {
                dw_scope: 0,
                dw_type: RESOURCETYPE_DISK,
                dw_display_type: 0,
                dw_usage: 0,
                lp_local_name: ptr::null_mut(),
                lp_remote_name: remote_name_w.as_ptr() as *mut _,
                lp_comment: ptr::null_mut(),
                lp_provider: ptr::null_mut(),
            };

            let result = unsafe {
                WNetAddConnection2W(
                    &resource,
                    password_w
                        .as_ref()
                        .map(|value| value.as_ptr())
                        .unwrap_or(ptr::null()),
                    username_w
                        .as_ref()
                        .map(|value| value.as_ptr())
                        .unwrap_or(ptr::null()),
                    CONNECT_TEMPORARY,
                )
            };

            if result == NO_ERROR {
                Ok(())
            } else {
                Err(SmbSessionError::Windows(format!("code {result}")))
            }
        }

        fn disconnect(&self, host: &str) -> SmbSessionResult<()> {
            let remote_name = format_unc_root(host, None)?;
            let remote_name_w = wide_null(&remote_name);
            let result = unsafe { WNetCancelConnection2W(remote_name_w.as_ptr(), 0, 1) };

            if result == NO_ERROR {
                Ok(())
            } else {
                Err(SmbSessionError::Windows(format!("code {result}")))
            }
        }

        fn unc_root(&self, host: &str, share: Option<&str>) -> SmbSessionResult<String> {
            format_unc_root(host, share)
        }
    }

    fn wide_null(value: &str) -> Vec<u16> {
        OsStr::new(value)
            .encode_wide()
            .chain(iter::once(0))
            .collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn formats_server_and_share_unc_roots() {
        assert_eq!(
            format_unc_root("192.168.102.136", Some("共享资料")).unwrap(),
            r"\\192.168.102.136\共享资料"
        );
        assert_eq!(format_unc_root("nas.local", None).unwrap(), r"\\nas.local");
    }

    #[test]
    fn rejects_empty_host() {
        assert_eq!(
            format_unc_root("", None).unwrap_err(),
            SmbSessionError::MissingHost
        );
    }
}
