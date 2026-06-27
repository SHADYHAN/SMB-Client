pub mod activation;
pub mod context_ipc;
pub mod context_request;
pub mod explorer;
pub mod local_redirect;
pub mod registration;
#[cfg(feature = "smb-session")]
pub mod smb_session;
pub mod unc_path;

pub use activation::{
    ActivationRequestError, LinkActivationTarget, deep_link_from_local_request_line,
    explorer_target_from_link,
};
pub use context_ipc::{
    ContextIpcError, DEFAULT_CONTEXT_IPC_PORT, build_context_http_request,
    parse_context_http_request, parse_context_http_response, send_context_request,
    start_context_ipc_server,
};
pub use context_request::{ContextAction, ContextRequest, ContextRequestError, ContextResponse};
pub use explorer::ExplorerTarget;
pub use local_redirect::{
    DEFAULT_LOCAL_REDIRECT_PORT, LocalRedirectError, start_local_redirect_server,
};
pub use registration::{
    ContextMenuRegistration, ProtocolRegistration, RegistrationError,
    windows_context_menu_reg_file, windows_protocol_reg_file,
};
#[cfg(feature = "smb-session")]
pub use smb_session::{
    SmbSessionConnectRequest, SmbSessionConnector, SmbSessionError, SmbSessionResult,
    UnsupportedSmbSessionConnector,
};
pub use unc_path::{UncPath, UncPathError};
