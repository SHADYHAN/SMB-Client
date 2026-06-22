use aes_gcm::aead::{Aead, KeyInit as AeadKeyInit};
use aes_gcm::{Aes256Gcm, Nonce};
use hmac::{Hmac, KeyInit as HmacKeyInit, Mac};
use sha2::Sha256;
use std::sync::OnceLock;

use crate::error::{CoreError, CoreResult};

const LEGACY_ENCRYPTED_PREFIX: &str = "v1:";
const KEYCHAIN_ENCRYPTED_PREFIX: &str = "v2:";
const NONCE_LEN: usize = 12;
static LEGACY_KEY: OnceLock<[u8; 32]> = OnceLock::new();

pub fn encrypt_credential(password: &str) -> CoreResult<String> {
    if password.is_empty() {
        return Err(CoreError::MissingField("password"));
    }

    let mut nonce = [0_u8; NONCE_LEN];
    getrandom::fill(&mut nonce).map_err(|error| CoreError::Crypto(error.to_string()))?;
    if let Some(key) = platform_secret_key()? {
        encrypt_credential_with_nonce_and_key(password, &nonce, &key, KEYCHAIN_ENCRYPTED_PREFIX)
    } else {
        encrypt_credential_with_nonce_and_key(
            password,
            &nonce,
            legacy_key(),
            LEGACY_ENCRYPTED_PREFIX,
        )
    }
}

pub fn decrypt_credential(encrypted: &str) -> CoreResult<String> {
    let (payload, key) = if let Some(payload) = encrypted.strip_prefix(KEYCHAIN_ENCRYPTED_PREFIX) {
        let key = platform_secret_key()?
            .ok_or_else(|| CoreError::Crypto("credential key is unavailable".to_string()))?;
        (payload, key)
    } else if let Some(payload) = encrypted.strip_prefix(LEGACY_ENCRYPTED_PREFIX) {
        (payload, *legacy_key())
    } else {
        return Err(CoreError::Crypto(
            "unsupported credential format".to_string(),
        ));
    };
    let bytes = base64url_decode(payload)?;
    if bytes.len() <= NONCE_LEN {
        return Err(CoreError::Crypto(
            "credential payload is too short".to_string(),
        ));
    }

    let (nonce, ciphertext) = bytes.split_at(NONCE_LEN);
    let cipher =
        Aes256Gcm::new_from_slice(&key).map_err(|error| CoreError::Crypto(error.to_string()))?;
    let nonce =
        Nonce::try_from(nonce).map_err(|_| CoreError::Crypto("invalid nonce".to_string()))?;
    let plain = cipher
        .decrypt(&nonce, ciphertext)
        .map_err(|_| CoreError::Crypto("credential decrypt failed".to_string()))?;
    String::from_utf8(plain).map_err(|error| CoreError::Crypto(error.to_string()))
}

pub fn is_encrypted_credential(value: &str) -> bool {
    value.starts_with(LEGACY_ENCRYPTED_PREFIX) || value.starts_with(KEYCHAIN_ENCRYPTED_PREFIX)
}

fn encrypt_credential_with_nonce_and_key(
    password: &str,
    nonce: &[u8; NONCE_LEN],
    key: &[u8; 32],
    prefix: &str,
) -> CoreResult<String> {
    let cipher =
        Aes256Gcm::new_from_slice(key).map_err(|error| CoreError::Crypto(error.to_string()))?;
    let cipher_nonce = Nonce::try_from(nonce.as_slice())
        .map_err(|_| CoreError::Crypto("invalid nonce".to_string()))?;
    let ciphertext = cipher
        .encrypt(&cipher_nonce, password.as_bytes())
        .map_err(|_| CoreError::Crypto("credential encrypt failed".to_string()))?;

    let mut payload = Vec::with_capacity(NONCE_LEN + ciphertext.len());
    payload.extend_from_slice(nonce);
    payload.extend_from_slice(&ciphertext);
    Ok(format!("{}{}", prefix, base64url_encode(&payload)))
}

fn derive_legacy_key() -> [u8; 32] {
    let mut input = Vec::new();
    input.extend_from_slice(machine_identity().as_bytes());
    input.push(0);
    input.extend_from_slice(user_identity().as_bytes());

    let prk = hkdf_extract(b"RYNAT credential encryption salt v1", &input);
    hkdf_expand(&prk, b"RYNAT AES-256-GCM key v1")
}

fn legacy_key() -> &'static [u8; 32] {
    LEGACY_KEY.get_or_init(derive_legacy_key)
}

#[cfg(all(target_os = "macos", not(test)))]
fn platform_secret_key() -> CoreResult<Option<[u8; 32]>> {
    macos_keychain::credential_key()
}

#[cfg(not(all(target_os = "macos", not(test))))]
fn platform_secret_key() -> CoreResult<Option<[u8; 32]>> {
    Ok(None)
}

fn machine_identity() -> String {
    platform_machine_identity()
        .or_else(hostname_from_env)
        .unwrap_or_else(|| "unknown-machine".to_string())
}

fn user_identity() -> String {
    platform_user_identity()
        .or_else(|| {
            std::env::var("USER")
                .or_else(|_| std::env::var("USERNAME"))
                .ok()
        })
        .unwrap_or_else(|| "unknown-user".to_string())
}

#[cfg(target_os = "macos")]
fn platform_machine_identity() -> Option<String> {
    system_command(&["ioreg", "-rd1", "-c", "IOPlatformExpertDevice"])
        .and_then(|output| parse_ioplatform_uuid(&output))
        .or_else(|| system_command(&["scutil", "--get", "ComputerName"]))
}

#[cfg(target_os = "windows")]
fn platform_machine_identity() -> Option<String> {
    system_command(&[
        "reg",
        "query",
        r"HKLM\SOFTWARE\Microsoft\Cryptography",
        "/v",
        "MachineGuid",
    ])
    .and_then(|output| {
        output.lines().find_map(|line| {
            let mut parts = line.split_whitespace();
            let name = parts.next()?;
            if !name.eq_ignore_ascii_case("MachineGuid") {
                return None;
            }
            let _kind = parts.next()?;
            parts.next().map(str::to_string)
        })
    })
}

#[cfg(not(any(target_os = "macos", target_os = "windows")))]
fn platform_machine_identity() -> Option<String> {
    std::fs::read_to_string("/etc/machine-id")
        .ok()
        .map(|value| value.trim().to_string())
        .filter(|value| !value.is_empty())
}

#[cfg(target_os = "windows")]
fn platform_user_identity() -> Option<String> {
    system_command(&["whoami", "/user"]).and_then(|output| {
        output
            .split_whitespace()
            .find(|part| part.starts_with("S-1-"))
            .map(str::to_string)
    })
}

#[cfg(not(target_os = "windows"))]
fn platform_user_identity() -> Option<String> {
    system_command(&["id", "-u"]).map(|uid| {
        let user = std::env::var("USER")
            .or_else(|_| std::env::var("USERNAME"))
            .unwrap_or_else(|_| "unknown-user".to_string());
        format!("{user}:{uid}")
    })
}

fn hostname_from_env() -> Option<String> {
    std::env::var("HOSTNAME")
        .or_else(|_| std::env::var("COMPUTERNAME"))
        .ok()
        .filter(|value| !value.trim().is_empty())
}

fn system_command(args: &[&str]) -> Option<String> {
    let (program, rest) = args.split_first()?;
    let output = std::process::Command::new(program)
        .args(rest)
        .output()
        .ok()?;
    if !output.status.success() {
        return None;
    }
    String::from_utf8(output.stdout)
        .ok()
        .map(|value| value.trim().to_string())
        .filter(|value| !value.is_empty())
}

#[cfg(any(target_os = "macos", test))]
fn parse_ioplatform_uuid(output: &str) -> Option<String> {
    output.lines().find_map(|line| {
        let (key, value) = line.split_once('=')?;
        if !key.contains("IOPlatformUUID") {
            return None;
        }
        let uuid = value.trim().trim_matches('"').trim().to_string();
        if uuid.is_empty() { None } else { Some(uuid) }
    })
}

#[cfg(all(target_os = "macos", not(test)))]
mod macos_keychain {
    use std::ffi::c_void;
    use std::ptr;

    use crate::error::{CoreError, CoreResult};

    type CFIndex = isize;
    type OSStatus = i32;
    type CFTypeRef = *const c_void;
    type CFStringRef = *const c_void;
    type CFDataRef = *const c_void;
    type CFDictionaryRef = *const c_void;
    type CFAllocatorRef = *const c_void;

    const ERR_SEC_SUCCESS: OSStatus = 0;
    const ERR_SEC_ITEM_NOT_FOUND: OSStatus = -25300;
    const ERR_SEC_DUPLICATE_ITEM: OSStatus = -25299;
    const K_CF_STRING_ENCODING_UTF8: u32 = 0x0800_0100;
    const SERVICE: &str = "com.rynat.shared-disk.credential-key";
    const ACCOUNT: &str = "rynat-shared-disk";

    #[repr(C)]
    struct CFDictionaryKeyCallBacks {
        version: CFIndex,
        retain: *const c_void,
        release: *const c_void,
        copy_description: *const c_void,
        equal: *const c_void,
        hash: *const c_void,
    }

    #[repr(C)]
    struct CFDictionaryValueCallBacks {
        version: CFIndex,
        retain: *const c_void,
        release: *const c_void,
        copy_description: *const c_void,
        equal: *const c_void,
    }

    #[link(name = "CoreFoundation", kind = "framework")]
    unsafe extern "C" {
        static kCFBooleanTrue: CFTypeRef;
        static kCFTypeDictionaryKeyCallBacks: CFDictionaryKeyCallBacks;
        static kCFTypeDictionaryValueCallBacks: CFDictionaryValueCallBacks;

        fn CFStringCreateWithBytes(
            alloc: CFAllocatorRef,
            bytes: *const u8,
            num_bytes: CFIndex,
            encoding: u32,
            is_external_representation: bool,
        ) -> CFStringRef;
        fn CFDataCreate(allocator: CFAllocatorRef, bytes: *const u8, length: CFIndex) -> CFDataRef;
        fn CFDataGetLength(data: CFDataRef) -> CFIndex;
        fn CFDataGetBytePtr(data: CFDataRef) -> *const u8;
        fn CFDictionaryCreate(
            allocator: CFAllocatorRef,
            keys: *const *const c_void,
            values: *const *const c_void,
            num_values: CFIndex,
            key_callbacks: *const CFDictionaryKeyCallBacks,
            value_callbacks: *const CFDictionaryValueCallBacks,
        ) -> CFDictionaryRef;
        fn CFRelease(cf: CFTypeRef);
    }

    #[link(name = "Security", kind = "framework")]
    unsafe extern "C" {
        static kSecAttrAccount: CFStringRef;
        static kSecAttrAccessible: CFStringRef;
        static kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly: CFStringRef;
        static kSecAttrService: CFStringRef;
        static kSecClass: CFStringRef;
        static kSecClassGenericPassword: CFStringRef;
        static kSecMatchLimit: CFStringRef;
        static kSecMatchLimitOne: CFStringRef;
        static kSecReturnData: CFStringRef;
        static kSecValueData: CFStringRef;

        fn SecItemAdd(attributes: CFDictionaryRef, result: *mut CFTypeRef) -> OSStatus;
        fn SecItemCopyMatching(query: CFDictionaryRef, result: *mut CFTypeRef) -> OSStatus;
    }

    pub(super) fn credential_key() -> CoreResult<Option<[u8; 32]>> {
        if let Some(existing) = copy_existing_key()? {
            return Ok(Some(existing));
        }

        let mut key = [0_u8; 32];
        getrandom::fill(&mut key).map_err(|error| CoreError::Crypto(error.to_string()))?;
        match add_key(&key) {
            ERR_SEC_SUCCESS => Ok(Some(key)),
            ERR_SEC_DUPLICATE_ITEM => copy_existing_key(),
            status => Err(CoreError::Crypto(format!(
                "Keychain credential key save failed: OSStatus {status}"
            ))),
        }
    }

    fn copy_existing_key() -> CoreResult<Option<[u8; 32]>> {
        let service = cf_string(SERVICE)?;
        let account = cf_string(ACCOUNT)?;
        let keys = unsafe {
            [
                kSecClass,
                kSecAttrService,
                kSecAttrAccount,
                kSecReturnData,
                kSecMatchLimit,
            ]
        };
        let values = unsafe {
            [
                kSecClassGenericPassword,
                service,
                account,
                kCFBooleanTrue,
                kSecMatchLimitOne,
            ]
        };
        let query = cf_dictionary(&keys, &values);
        let mut result: CFTypeRef = ptr::null();
        let status = unsafe { SecItemCopyMatching(query, &mut result) };
        unsafe {
            CFRelease(query);
            CFRelease(service);
            CFRelease(account);
        }

        if status == ERR_SEC_ITEM_NOT_FOUND {
            return Ok(None);
        }
        if status != ERR_SEC_SUCCESS {
            return Err(CoreError::Crypto(format!(
                "Keychain credential key lookup failed: OSStatus {status}"
            )));
        }
        if result.is_null() {
            return Err(CoreError::Crypto(
                "Keychain credential key lookup returned no data".to_string(),
            ));
        }

        let data = result as CFDataRef;
        let len = unsafe { CFDataGetLength(data) };
        let mut key = [0_u8; 32];
        if len == key.len() as CFIndex {
            let ptr = unsafe { CFDataGetBytePtr(data) };
            if !ptr.is_null() {
                unsafe {
                    std::ptr::copy_nonoverlapping(ptr, key.as_mut_ptr(), key.len());
                    CFRelease(result);
                }
                return Ok(Some(key));
            }
        }
        unsafe { CFRelease(result) };
        Err(CoreError::Crypto(
            "Keychain credential key has invalid data".to_string(),
        ))
    }

    fn add_key(key: &[u8; 32]) -> OSStatus {
        let service = match cf_string(SERVICE) {
            Ok(value) => value,
            Err(_) => return -1,
        };
        let account = match cf_string(ACCOUNT) {
            Ok(value) => value,
            Err(_) => {
                unsafe { CFRelease(service) };
                return -1;
            }
        };
        let data = unsafe { CFDataCreate(ptr::null(), key.as_ptr(), key.len() as CFIndex) };
        if data.is_null() {
            unsafe {
                CFRelease(service);
                CFRelease(account);
            }
            return -1;
        }

        let keys = unsafe {
            [
                kSecClass,
                kSecAttrService,
                kSecAttrAccount,
                kSecValueData,
                kSecAttrAccessible,
            ]
        };
        let values = unsafe {
            [
                kSecClassGenericPassword,
                service,
                account,
                data,
                kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
            ]
        };
        let attributes = cf_dictionary(&keys, &values);
        let status = unsafe { SecItemAdd(attributes, ptr::null_mut()) };
        unsafe {
            CFRelease(attributes);
            CFRelease(data);
            CFRelease(service);
            CFRelease(account);
        }
        status
    }

    fn cf_string(value: &str) -> CoreResult<CFStringRef> {
        let string = unsafe {
            CFStringCreateWithBytes(
                ptr::null(),
                value.as_ptr(),
                value.len() as CFIndex,
                K_CF_STRING_ENCODING_UTF8,
                false,
            )
        };
        if string.is_null() {
            Err(CoreError::Crypto(
                "failed to create keychain string".to_string(),
            ))
        } else {
            Ok(string)
        }
    }

    fn cf_dictionary(keys: &[CFStringRef], values: &[CFTypeRef]) -> CFDictionaryRef {
        unsafe {
            CFDictionaryCreate(
                ptr::null(),
                keys.as_ptr(),
                values.as_ptr(),
                keys.len() as CFIndex,
                &kCFTypeDictionaryKeyCallBacks,
                &kCFTypeDictionaryValueCallBacks,
            )
        }
    }
}

type HmacSha256 = Hmac<Sha256>;

fn hkdf_extract(salt: &[u8], input: &[u8]) -> [u8; 32] {
    let mut mac =
        <HmacSha256 as HmacKeyInit>::new_from_slice(salt).expect("HMAC accepts any key length");
    mac.update(input);
    mac.finalize().into_bytes().into()
}

fn hkdf_expand(prk: &[u8; 32], info: &[u8]) -> [u8; 32] {
    let mut mac =
        <HmacSha256 as HmacKeyInit>::new_from_slice(prk).expect("HMAC accepts any key length");
    mac.update(info);
    mac.update(&[1]);
    mac.finalize().into_bytes().into()
}

const BASE64URL: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

fn base64url_encode(bytes: &[u8]) -> String {
    let mut out = String::with_capacity(bytes.len().div_ceil(3) * 4);
    for chunk in bytes.chunks(3) {
        let b0 = chunk[0];
        let b1 = *chunk.get(1).unwrap_or(&0);
        let b2 = *chunk.get(2).unwrap_or(&0);
        let n = ((b0 as u32) << 16) | ((b1 as u32) << 8) | b2 as u32;
        out.push(BASE64URL[((n >> 18) & 0x3f) as usize] as char);
        out.push(BASE64URL[((n >> 12) & 0x3f) as usize] as char);
        if chunk.len() > 1 {
            out.push(BASE64URL[((n >> 6) & 0x3f) as usize] as char);
        }
        if chunk.len() > 2 {
            out.push(BASE64URL[(n & 0x3f) as usize] as char);
        }
    }
    out
}

fn base64url_decode(value: &str) -> CoreResult<Vec<u8>> {
    let mut output = Vec::with_capacity((value.len() * 3) / 4);
    let mut buffer = 0_u32;
    let mut bits = 0_u8;
    for byte in value.bytes() {
        let value = decode_base64url_byte(byte)
            .ok_or_else(|| CoreError::Crypto("invalid credential encoding".to_string()))?;
        buffer = (buffer << 6) | value as u32;
        bits += 6;
        while bits >= 8 {
            bits -= 8;
            output.push(((buffer >> bits) & 0xff) as u8);
        }
    }
    Ok(output)
}

fn decode_base64url_byte(byte: u8) -> Option<u8> {
    match byte {
        b'A'..=b'Z' => Some(byte - b'A'),
        b'a'..=b'z' => Some(byte - b'a' + 26),
        b'0'..=b'9' => Some(byte - b'0' + 52),
        b'-' => Some(62),
        b'_' => Some(63),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn encrypts_and_decrypts_credential() {
        let encrypted = encrypt_credential("secret-password").unwrap();

        assert!(is_encrypted_credential(&encrypted));
        assert!(!encrypted.contains("secret-password"));
        assert_eq!(decrypt_credential(&encrypted).unwrap(), "secret-password");
    }

    #[test]
    fn encrypts_with_random_nonce() {
        let first = encrypt_credential("secret-password").unwrap();
        let second = encrypt_credential("secret-password").unwrap();

        assert_ne!(first, second);
        assert_eq!(decrypt_credential(&first).unwrap(), "secret-password");
        assert_eq!(decrypt_credential(&second).unwrap(), "secret-password");
    }

    #[test]
    fn rejects_plaintext_credential_payload() {
        let error = decrypt_credential("secret-password").unwrap_err();

        assert!(error.to_string().contains("credential"));
    }

    #[test]
    fn base64url_round_trips_without_padding() {
        let bytes = b"abc123\x00\xff";
        let encoded = base64url_encode(bytes);

        assert!(!encoded.contains('='));
        assert_eq!(base64url_decode(&encoded).unwrap(), bytes);
    }

    #[test]
    fn parses_ioplatform_uuid() {
        let output = r#"{
          "IOPlatformUUID" = "A1B2-C3"
        }"#;

        assert_eq!(parse_ioplatform_uuid(output).as_deref(), Some("A1B2-C3"));
    }

    #[test]
    fn ignores_empty_ioplatform_uuid() {
        let output = r#"{
          "IOPlatformUUID" = ""
        }"#;

        assert_eq!(parse_ioplatform_uuid(output), None);
    }
}
