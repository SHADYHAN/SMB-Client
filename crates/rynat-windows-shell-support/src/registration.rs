use thiserror::Error;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ProtocolRegistration {
    pub executable_path: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ContextMenuRegistration {
    pub helper_path: String,
    pub menu_text: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Error)]
pub enum RegistrationError {
    #[error("executable path is required")]
    MissingExecutablePath,
    #[error("helper path is required")]
    MissingHelperPath,
    #[error("menu text is required")]
    MissingMenuText,
}

pub fn windows_protocol_reg_file(
    registration: &ProtocolRegistration,
) -> Result<String, RegistrationError> {
    let executable = registration.executable_path.trim();
    if executable.is_empty() {
        return Err(RegistrationError::MissingExecutablePath);
    }

    let escaped = reg_escape(executable);
    Ok(format!(
        r#"Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\rynat]
@="URL:RYNAT Link"
"URL Protocol"=""

[HKEY_CURRENT_USER\Software\Classes\rynat\DefaultIcon]
@="\"{escaped}\",0"

[HKEY_CURRENT_USER\Software\Classes\rynat\shell\open\command]
@="\"{escaped}\" \"%1\""
"#
    ))
}

pub fn windows_context_menu_reg_file(
    registration: &ContextMenuRegistration,
) -> Result<String, RegistrationError> {
    let helper = registration.helper_path.trim();
    if helper.is_empty() {
        return Err(RegistrationError::MissingHelperPath);
    }
    let menu_text = registration.menu_text.trim();
    if menu_text.is_empty() {
        return Err(RegistrationError::MissingMenuText);
    }

    let escaped_helper = reg_escape(helper);
    let escaped_text = reg_escape(menu_text);
    Ok(format!(
        r#"Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\*\shell\RynatCopyLink]
@="{escaped_text}"
"Icon"="\"{escaped_helper}\",0"

[HKEY_CURRENT_USER\Software\Classes\*\shell\RynatCopyLink\command]
@="\"{escaped_helper}\" copy-link \"%1\" --kind file"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\RynatCopyLink]
@="{escaped_text}"
"Icon"="\"{escaped_helper}\",0"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\RynatCopyLink\command]
@="\"{escaped_helper}\" copy-link \"%1\" --kind directory"
"#
    ))
}

fn reg_escape(value: &str) -> String {
    value.replace('\\', r"\\").replace('"', r#"\""#)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn builds_protocol_registration_file() {
        let reg = windows_protocol_reg_file(&ProtocolRegistration {
            executable_path: r"C:\Program Files\RYNAT\RYNAT.exe".to_string(),
        })
        .unwrap();

        assert!(reg.contains(r"[HKEY_CURRENT_USER\Software\Classes\rynat]"));
        assert!(reg.contains(r#"@="\"C:\\Program Files\\RYNAT\\RYNAT.exe\" \"%1\"""#));
    }

    #[test]
    fn builds_context_menu_registration_file() {
        let reg = windows_context_menu_reg_file(&ContextMenuRegistration {
            helper_path: r"C:\Program Files\RYNAT\rynat-windows-context-helper.exe".to_string(),
            menu_text: "复制 RYNAT 分享链接".to_string(),
        })
        .unwrap();

        assert!(reg.contains(r"[HKEY_CURRENT_USER\Software\Classes\*\shell\RynatCopyLink]"));
        assert!(reg.contains(r#"copy-link \"%1\" --kind file"#));
        assert!(reg.contains(r#"copy-link \"%1\" --kind directory"#));
    }
}
