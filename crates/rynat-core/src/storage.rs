use std::path::Path;
use std::sync::{Arc, Mutex};

use rusqlite::Connection;
use rusqlite::types::Type;

use crate::credential::{decrypt_credential, encrypt_credential, is_encrypted_credential};
use crate::error::{CoreError, CoreResult};
use crate::link::{LinkKind, QuickLink, QuickLinkTarget};
use crate::server::{
    AuthMode, ServerCredential, ServerEndpointKey, ServerProfile, SmbDialectPreference,
    parse_server_endpoint,
};

pub const DEFAULT_SERVER_DISPLAY_NAME: &str = "共享网盘";
pub const DEFAULT_SERVER_HOST: &str = "192.168.102.136";

const INIT_SQL: &str = r#"
CREATE TABLE IF NOT EXISTS quick_links (
    id TEXT PRIMARY KEY,
    server_host TEXT NOT NULL,
    share TEXT NOT NULL,
    remote_path TEXT NOT NULL,
    name TEXT,
    kind TEXT NOT NULL,
    http_url TEXT NOT NULL,
    deep_link_url TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_quick_links_created_at
ON quick_links(created_at DESC);

CREATE TABLE IF NOT EXISTS recent_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    server_host TEXT NOT NULL,
    share TEXT NOT NULL,
    remote_path TEXT NOT NULL,
    name TEXT,
    kind TEXT NOT NULL,
    accessed_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_recent_items_accessed_at
ON recent_items(accessed_at DESC);

CREATE TABLE IF NOT EXISTS server_profiles (
    id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    host TEXT NOT NULL,
    port INTEGER,
    username TEXT,
    auth_mode TEXT NOT NULL,
    dialect_preference TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_server_profiles_endpoint
ON server_profiles(host, COALESCE(port, 0));

CREATE TABLE IF NOT EXISTS server_credentials (
    server_profile_id TEXT PRIMARY KEY,
    username TEXT NOT NULL,
    password_encrypted TEXT NOT NULL,
    remember_password INTEGER NOT NULL,
    auto_login INTEGER NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app_settings (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
"#;

#[derive(Clone)]
pub struct CoreStore {
    conn: Arc<Mutex<Connection>>,
}

impl CoreStore {
    pub fn open(path: impl AsRef<Path>) -> CoreResult<Self> {
        if let Some(parent) = path.as_ref().parent() {
            std::fs::create_dir_all(parent)
                .map_err(|error| CoreError::Storage(error.to_string()))?;
        }
        prepare_store_file(path.as_ref())?;
        let conn = Connection::open(path)?;
        conn.execute_batch(INIT_SQL)?;
        migrate_credentials_schema(&conn)?;
        Ok(Self {
            conn: Arc::new(Mutex::new(conn)),
        })
    }

    pub fn in_memory() -> CoreResult<Self> {
        let conn = Connection::open_in_memory()?;
        conn.execute_batch(INIT_SQL)?;
        migrate_credentials_schema(&conn)?;
        Ok(Self {
            conn: Arc::new(Mutex::new(conn)),
        })
    }

    pub fn save_quick_link(&self, link: &QuickLink) -> CoreResult<()> {
        let mut conn = self.lock_conn()?;
        let tx = conn.transaction()?;
        tx.execute(
            "DELETE FROM quick_links
             WHERE server_host = ?1 AND share = ?2 AND remote_path = ?3 AND kind = ?4",
            rusqlite::params![
                link.target.server_host,
                link.target.share,
                link.target.path,
                kind_to_str(link.target.kind),
            ],
        )?;
        tx.execute(
            "INSERT OR REPLACE INTO quick_links
             (id, server_host, share, remote_path, name, kind, http_url, deep_link_url, created_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)",
            rusqlite::params![
                link.id,
                link.target.server_host,
                link.target.share,
                link.target.path,
                link.target.name,
                kind_to_str(link.target.kind),
                link.http_url,
                link.deep_link_url,
                link.created_at,
            ],
        )?;
        tx.commit()?;
        Ok(())
    }

    pub fn list_quick_links(&self) -> CoreResult<Vec<QuickLink>> {
        let conn = self.lock_conn()?;
        let mut stmt = conn.prepare(
            "SELECT id, server_host, share, remote_path, name, kind, http_url, deep_link_url, created_at
             FROM quick_links
             ORDER BY created_at DESC",
        )?;
        let links = stmt
            .query_map([], row_to_quick_link)?
            .collect::<Result<Vec<_>, _>>()?;
        Ok(links)
    }

    pub fn delete_quick_link(&self, id: &str) -> CoreResult<()> {
        let conn = self.lock_conn()?;
        conn.execute("DELETE FROM quick_links WHERE id = ?1", [id])?;
        Ok(())
    }

    pub fn save_server_profile(&self, profile: &ServerProfile) -> CoreResult<()> {
        let conn = self.lock_conn()?;
        conn.execute(
            "INSERT OR REPLACE INTO server_profiles
             (id, display_name, host, port, username, auth_mode, dialect_preference, created_at, updated_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)",
            rusqlite::params![
                profile.id,
                profile.display_name,
                profile.endpoint.host,
                profile.endpoint.port,
                profile.username,
                profile.auth_mode.as_str(),
                profile.dialect_preference.as_str(),
                profile.created_at,
                profile.updated_at,
            ],
        )?;
        Ok(())
    }

    pub fn ensure_default_server_profile(&self) -> CoreResult<ServerProfile> {
        if let Some(profile) =
            self.find_server_profile_by_endpoint(&parse_server_endpoint(DEFAULT_SERVER_HOST)?)?
        {
            return Ok(profile);
        }

        let profile = ServerProfile::new(
            DEFAULT_SERVER_DISPLAY_NAME,
            DEFAULT_SERVER_HOST,
            None,
            AuthMode::UsernamePassword,
        )?;
        self.save_server_profile(&profile)?;
        if self.active_server_profile()?.is_none() {
            self.set_active_server_profile(&profile.id)?;
        }
        Ok(profile)
    }

    pub fn save_server_profile_from_parts(
        &self,
        id: Option<&str>,
        display_name: &str,
        host: &str,
        username: Option<String>,
        auth_mode: AuthMode,
        dialect_preference: SmbDialectPreference,
        set_active: bool,
    ) -> CoreResult<ServerProfile> {
        let mut profile = if let Some(id) = id.filter(|value| !value.trim().is_empty()) {
            match self.find_server_profile_by_id(id)? {
                Some(profile) => profile,
                None => ServerProfile::new(display_name, host, username.clone(), auth_mode)?,
            }
        } else {
            ServerProfile::new(display_name, host, username.clone(), auth_mode)?
        };

        profile.update(display_name, host, username, auth_mode, dialect_preference)?;
        let mut conn = self.lock_conn()?;
        let tx = conn.transaction()?;
        tx.execute(
            "INSERT OR REPLACE INTO server_profiles
             (id, display_name, host, port, username, auth_mode, dialect_preference, created_at, updated_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)",
            rusqlite::params![
                profile.id,
                profile.display_name,
                profile.endpoint.host,
                profile.endpoint.port,
                profile.username,
                profile.auth_mode.as_str(),
                profile.dialect_preference.as_str(),
                profile.created_at,
                profile.updated_at,
            ],
        )?;
        if set_active {
            tx.execute(
                "INSERT OR REPLACE INTO app_settings (key, value) VALUES ('active_server_profile_id', ?1)",
                [&profile.id],
            )?;
        }
        tx.commit()?;
        Ok(profile)
    }

    pub fn list_server_profiles(&self) -> CoreResult<Vec<ServerProfile>> {
        let conn = self.lock_conn()?;
        let mut stmt = conn.prepare(
            "SELECT id, display_name, host, port, username, auth_mode, dialect_preference, created_at, updated_at
             FROM server_profiles
             ORDER BY display_name ASC, host ASC",
        )?;
        let profiles = stmt
            .query_map([], row_to_server_profile)?
            .collect::<Result<Vec<_>, _>>()?;
        Ok(profiles)
    }

    pub fn find_server_profile_for_target(
        &self,
        target: &QuickLinkTarget,
    ) -> CoreResult<Option<ServerProfile>> {
        let endpoint = parse_server_endpoint(&target.server_host)?;
        self.find_server_profile_by_endpoint(&endpoint)
    }

    pub fn find_server_profile_by_endpoint(
        &self,
        endpoint: &ServerEndpointKey,
    ) -> CoreResult<Option<ServerProfile>> {
        let conn = self.lock_conn()?;
        let mut stmt = conn.prepare(
            "SELECT id, display_name, host, port, username, auth_mode, dialect_preference, created_at, updated_at
             FROM server_profiles
             WHERE host = ?1 AND COALESCE(port, 0) = COALESCE(?2, 0)
             LIMIT 1",
        )?;
        let mut rows = stmt.query(rusqlite::params![endpoint.host, endpoint.port])?;
        match rows.next()? {
            Some(row) => Ok(Some(row_to_server_profile(row)?)),
            None => Ok(None),
        }
    }

    pub fn find_server_profile_by_id(&self, id: &str) -> CoreResult<Option<ServerProfile>> {
        let conn = self.lock_conn()?;
        let mut stmt = conn.prepare(
            "SELECT id, display_name, host, port, username, auth_mode, dialect_preference, created_at, updated_at
             FROM server_profiles
             WHERE id = ?1
             LIMIT 1",
        )?;
        let mut rows = stmt.query([id])?;
        match rows.next()? {
            Some(row) => Ok(Some(row_to_server_profile(row)?)),
            None => Ok(None),
        }
    }

    pub fn set_active_server_profile(&self, id: &str) -> CoreResult<()> {
        let conn = self.lock_conn()?;
        conn.execute(
            "INSERT OR REPLACE INTO app_settings (key, value) VALUES ('active_server_profile_id', ?1)",
            [id],
        )?;
        Ok(())
    }

    pub fn delete_server_profile(&self, id: &str) -> CoreResult<()> {
        let id = id.trim();
        if id.is_empty() {
            return Err(CoreError::MissingField("server_profile_id"));
        }

        let mut conn = self.lock_conn()?;
        let profile_count: i64 =
            conn.query_row("SELECT COUNT(*) FROM server_profiles", [], |row| row.get(0))?;
        if profile_count <= 1 {
            return Err(CoreError::InvalidLink(
                "cannot delete the last server profile".to_string(),
            ));
        }

        let active_id: Option<String> = conn
            .query_row(
                "SELECT value FROM app_settings WHERE key = 'active_server_profile_id'",
                [],
                |row| row.get(0),
            )
            .ok();

        let tx = conn.transaction()?;
        let deleted = tx.execute("DELETE FROM server_profiles WHERE id = ?1", [id])?;
        if deleted == 0 {
            return Err(CoreError::InvalidLink(
                "server profile not found".to_string(),
            ));
        }
        tx.execute(
            "DELETE FROM server_credentials WHERE server_profile_id = ?1",
            [id],
        )?;

        if active_id.as_deref() == Some(id) {
            let replacement_id: String = tx.query_row(
                "SELECT id FROM server_profiles ORDER BY display_name ASC, host ASC LIMIT 1",
                [],
                |row| row.get(0),
            )?;
            tx.execute(
                "INSERT OR REPLACE INTO app_settings (key, value) VALUES ('active_server_profile_id', ?1)",
                [replacement_id],
            )?;
        }
        tx.commit()?;
        Ok(())
    }

    pub fn active_server_profile(&self) -> CoreResult<Option<ServerProfile>> {
        let conn = self.lock_conn()?;
        let active_id: Option<String> = conn
            .query_row(
                "SELECT value FROM app_settings WHERE key = 'active_server_profile_id'",
                [],
                |row| row.get(0),
            )
            .ok();
        let Some(active_id) = active_id else {
            return Ok(None);
        };
        let mut stmt = conn.prepare(
            "SELECT id, display_name, host, port, username, auth_mode, dialect_preference, created_at, updated_at
             FROM server_profiles
             WHERE id = ?1
             LIMIT 1",
        )?;
        let mut rows = stmt.query([active_id])?;
        match rows.next()? {
            Some(row) => Ok(Some(row_to_server_profile(row)?)),
            None => Ok(None),
        }
    }

    pub fn save_server_credential(&self, credential: &ServerCredential) -> CoreResult<()> {
        let conn = self.lock_conn()?;
        let password_encrypted = encrypt_credential(&credential.password)?;
        conn.execute(
            "INSERT OR REPLACE INTO server_credentials
             (server_profile_id, username, password_encrypted, remember_password, auto_login, updated_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6)",
            rusqlite::params![
                credential.server_profile_id,
                credential.username,
                password_encrypted,
                credential.remember_password,
                credential.auto_login,
                credential.updated_at,
            ],
        )?;
        Ok(())
    }

    pub fn update_server_credential_options(
        &self,
        server_profile_id: &str,
        remember_password: bool,
        auto_login: bool,
    ) -> CoreResult<Option<ServerCredential>> {
        let existing = self.server_credential(server_profile_id)?;
        let Some(mut credential) = existing else {
            return Ok(None);
        };
        credential.remember_password = remember_password;
        credential.auto_login = auto_login;
        credential.updated_at = chrono::Utc::now().to_rfc3339();

        let conn = self.lock_conn()?;
        conn.execute(
            "UPDATE server_credentials
             SET remember_password = ?2, auto_login = ?3, updated_at = ?4
             WHERE server_profile_id = ?1",
            rusqlite::params![
                credential.server_profile_id,
                credential.remember_password,
                credential.auto_login,
                credential.updated_at,
            ],
        )?;
        Ok(Some(credential))
    }

    pub fn server_credential(
        &self,
        server_profile_id: &str,
    ) -> CoreResult<Option<ServerCredential>> {
        let conn = self.lock_conn()?;
        let mut stmt = conn.prepare(
            "SELECT server_profile_id, username, password_encrypted, remember_password, auto_login, updated_at
             FROM server_credentials
             WHERE server_profile_id = ?1
             LIMIT 1",
        )?;
        let mut rows = stmt.query([server_profile_id])?;
        match rows.next()? {
            Some(row) => Ok(Some(row_to_server_credential(row)?)),
            None => Ok(None),
        }
    }

    pub fn active_server_credential(&self) -> CoreResult<Option<ServerCredential>> {
        let Some(profile) = self.active_server_profile()? else {
            return Ok(None);
        };
        self.server_credential(&profile.id)
    }

    pub fn delete_server_credential(&self, server_profile_id: &str) -> CoreResult<()> {
        let conn = self.lock_conn()?;
        conn.execute(
            "DELETE FROM server_credentials WHERE server_profile_id = ?1",
            [server_profile_id],
        )?;
        Ok(())
    }

    pub fn set_app_setting(&self, key: &str, value: &str) -> CoreResult<()> {
        let key = key.trim();
        if key.is_empty() {
            return Err(CoreError::MissingField("key"));
        }
        let conn = self.lock_conn()?;
        conn.execute(
            "INSERT OR REPLACE INTO app_settings (key, value) VALUES (?1, ?2)",
            rusqlite::params![key, value],
        )?;
        Ok(())
    }

    pub fn app_setting(&self, key: &str) -> CoreResult<Option<String>> {
        let key = key.trim();
        if key.is_empty() {
            return Err(CoreError::MissingField("key"));
        }
        let conn = self.lock_conn()?;
        let mut stmt = conn.prepare("SELECT value FROM app_settings WHERE key = ?1 LIMIT 1")?;
        let mut rows = stmt.query([key])?;
        match rows.next()? {
            Some(row) => Ok(Some(row.get(0)?)),
            None => Ok(None),
        }
    }

    fn lock_conn(&self) -> CoreResult<std::sync::MutexGuard<'_, Connection>> {
        self.conn
            .lock()
            .map_err(|error| CoreError::Storage(error.to_string()))
    }
}

fn row_to_quick_link(row: &rusqlite::Row<'_>) -> rusqlite::Result<QuickLink> {
    let kind_text: String = row.get(5)?;
    Ok(QuickLink {
        id: row.get(0)?,
        target: QuickLinkTarget {
            server_host: row.get(1)?,
            share: row.get(2)?,
            path: row.get(3)?,
            name: row.get(4)?,
            kind: str_to_kind(&kind_text),
        },
        http_url: row.get(6)?,
        deep_link_url: row.get(7)?,
        created_at: row.get(8)?,
    })
}

fn row_to_server_profile(row: &rusqlite::Row<'_>) -> rusqlite::Result<ServerProfile> {
    let auth_mode: String = row.get(5)?;
    let dialect_preference: String = row.get(6)?;
    Ok(ServerProfile {
        id: row.get(0)?,
        display_name: row.get(1)?,
        endpoint: ServerEndpointKey {
            host: row.get(2)?,
            port: row.get(3)?,
        },
        username: row.get(4)?,
        auth_mode: AuthMode::from_storage_value(&auth_mode),
        dialect_preference: SmbDialectPreference::from_storage_value(&dialect_preference),
        created_at: row.get(7)?,
        updated_at: row.get(8)?,
    })
}

fn row_to_server_credential(row: &rusqlite::Row<'_>) -> rusqlite::Result<ServerCredential> {
    let encrypted: String = row.get(2)?;
    let password = decrypt_credential(&encrypted).map_err(|error| {
        rusqlite::Error::FromSqlConversionFailure(2, Type::Text, Box::new(error))
    })?;
    Ok(ServerCredential {
        server_profile_id: row.get(0)?,
        username: row.get(1)?,
        password,
        remember_password: row.get::<_, i64>(3)? != 0,
        auto_login: row.get::<_, i64>(4)? != 0,
        updated_at: row.get(5)?,
    })
}

fn kind_to_str(kind: LinkKind) -> &'static str {
    match kind {
        LinkKind::File => "file",
        LinkKind::Directory => "dir",
        LinkKind::Unknown => "unknown",
    }
}

fn str_to_kind(value: &str) -> LinkKind {
    match value {
        "file" => LinkKind::File,
        "dir" => LinkKind::Directory,
        _ => LinkKind::Unknown,
    }
}

fn prepare_store_file(path: &Path) -> CoreResult<()> {
    if !path.exists() {
        create_private_store_file(path)?;
    }
    set_private_store_permissions(path)
}

#[cfg(unix)]
fn create_private_store_file(path: &Path) -> CoreResult<()> {
    use std::os::unix::fs::OpenOptionsExt;

    std::fs::OpenOptions::new()
        .create_new(true)
        .write(true)
        .mode(0o600)
        .open(path)
        .map(|_| ())
        .or_else(|error| {
            if error.kind() == std::io::ErrorKind::AlreadyExists {
                Ok(())
            } else {
                Err(error)
            }
        })
        .map_err(|error| CoreError::Storage(error.to_string()))
}

#[cfg(not(unix))]
fn create_private_store_file(path: &Path) -> CoreResult<()> {
    std::fs::OpenOptions::new()
        .create_new(true)
        .write(true)
        .open(path)
        .map(|_| ())
        .or_else(|error| {
            if error.kind() == std::io::ErrorKind::AlreadyExists {
                Ok(())
            } else {
                Err(error)
            }
        })
        .map_err(|error| CoreError::Storage(error.to_string()))
}

#[cfg(unix)]
fn set_private_store_permissions(path: &Path) -> CoreResult<()> {
    use std::os::unix::fs::PermissionsExt;

    let permissions = std::fs::Permissions::from_mode(0o600);
    std::fs::set_permissions(path, permissions)
        .map_err(|error| CoreError::Storage(error.to_string()))
}

#[cfg(not(unix))]
fn set_private_store_permissions(_path: &Path) -> CoreResult<()> {
    Ok(())
}

fn migrate_credentials_schema(conn: &Connection) -> CoreResult<()> {
    let columns = table_columns(conn, "server_credentials")?;
    let has_password = columns.iter().any(|column| column == "password");
    let has_encrypted = columns.iter().any(|column| column == "password_encrypted");

    if !has_password && has_encrypted {
        conn.execute(
            "DELETE FROM server_credentials
             WHERE password_encrypted IS NULL OR password_encrypted = ''",
            [],
        )?;
        return Ok(());
    }

    if !has_password && !has_encrypted {
        rebuild_credentials_table(conn, Vec::new())?;
        return Ok(());
    }

    let has_username = columns.iter().any(|column| column == "username");
    let has_remember = columns.iter().any(|column| column == "remember_password");
    let has_auto_login = columns.iter().any(|column| column == "auto_login");
    let has_updated_at = columns.iter().any(|column| column == "updated_at");
    let mut credentials = Vec::new();

    if has_password {
        let encrypted_expr = if has_encrypted {
            "password_encrypted"
        } else {
            "NULL AS password_encrypted"
        };
        let username_expr = if has_username {
            "username"
        } else {
            "'' AS username"
        };
        let remember_expr = if has_remember {
            "remember_password"
        } else {
            "1 AS remember_password"
        };
        let auto_login_expr = if has_auto_login {
            "auto_login"
        } else {
            "0 AS auto_login"
        };
        let updated_at_expr = if has_updated_at {
            "updated_at"
        } else {
            "datetime('now') AS updated_at"
        };
        let query = format!(
            "SELECT server_profile_id, {username_expr}, password, {encrypted_expr}, \
             {remember_expr}, {auto_login_expr}, {updated_at_expr} FROM server_credentials"
        );
        let mut stmt = conn.prepare(&query)?;
        let rows = stmt
            .query_map([], |row| {
                Ok((
                    row.get::<_, String>(0)?,
                    row.get::<_, String>(1)?,
                    row.get::<_, String>(2)?,
                    row.get::<_, Option<String>>(3)?,
                    row.get::<_, i64>(4)?,
                    row.get::<_, i64>(5)?,
                    row.get::<_, String>(6)?,
                ))
            })?
            .collect::<Result<Vec<_>, _>>()?;

        for (
            server_profile_id,
            username,
            password,
            existing_encrypted,
            remember_password,
            auto_login,
            updated_at,
        ) in rows
        {
            let password_encrypted = if existing_encrypted
                .as_deref()
                .is_some_and(is_encrypted_credential)
            {
                existing_encrypted.unwrap()
            } else if is_encrypted_credential(&password) {
                password
            } else {
                encrypt_credential(&password)?
            };

            if !password_encrypted.is_empty() {
                credentials.push(MigrationCredential {
                    server_profile_id,
                    username,
                    password_encrypted,
                    remember_password,
                    auto_login,
                    updated_at,
                });
            }
        }
    }

    rebuild_credentials_table(conn, credentials)
}

struct MigrationCredential {
    server_profile_id: String,
    username: String,
    password_encrypted: String,
    remember_password: i64,
    auto_login: i64,
    updated_at: String,
}

fn rebuild_credentials_table(
    conn: &Connection,
    credentials: Vec<MigrationCredential>,
) -> CoreResult<()> {
    let tx = conn.unchecked_transaction()?;
    tx.execute_batch(
        "DROP TABLE IF EXISTS server_credentials_new;
         CREATE TABLE server_credentials_new (
             server_profile_id TEXT PRIMARY KEY,
             username TEXT NOT NULL,
             password_encrypted TEXT NOT NULL,
             remember_password INTEGER NOT NULL,
             auto_login INTEGER NOT NULL,
             updated_at TEXT NOT NULL
         );",
    )?;

    for credential in credentials {
        if credential.password_encrypted.is_empty() {
            continue;
        }
        tx.execute(
            "INSERT OR REPLACE INTO server_credentials_new
             (server_profile_id, username, password_encrypted, remember_password, auto_login, updated_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6)",
            rusqlite::params![
                credential.server_profile_id,
                credential.username,
                credential.password_encrypted,
                credential.remember_password,
                credential.auto_login,
                credential.updated_at,
            ],
        )?;
    }

    tx.execute_batch(
        "DROP TABLE IF EXISTS server_credentials;
         ALTER TABLE server_credentials_new RENAME TO server_credentials;",
    )?;
    tx.commit()?;
    Ok(())
}

fn table_columns(conn: &Connection, table: &str) -> CoreResult<Vec<String>> {
    let mut stmt = conn.prepare(&format!("PRAGMA table_info({table})"))?;
    let columns = stmt
        .query_map([], |row| row.get::<_, String>(1))?
        .collect::<Result<Vec<_>, _>>()?;
    Ok(columns)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::link::{LinkKind, QuickLink, QuickLinkTarget};
    use crate::server::{AuthMode, ServerProfile, SmbDialectPreference};

    #[test]
    fn stores_and_lists_quick_links_newest_first() {
        let store = CoreStore::in_memory().unwrap();
        let first = QuickLink::create(QuickLinkTarget::new(
            "nas-a",
            "Backoffice",
            "/Contracts",
            Some("Contracts".to_string()),
            LinkKind::Directory,
        ))
        .unwrap();
        let second = QuickLink::create(QuickLinkTarget::new(
            "nas-b",
            "Media",
            "/Movies/demo.mp4",
            Some("demo.mp4".to_string()),
            LinkKind::File,
        ))
        .unwrap();

        store.save_quick_link(&first).unwrap();
        store.save_quick_link(&second).unwrap();

        let links = store.list_quick_links().unwrap();

        assert_eq!(links.len(), 2);
        assert_eq!(links[0].target.server_host, "nas-b");
        assert_eq!(links[0].target.kind, LinkKind::File);
        assert_eq!(links[1].target.server_host, "nas-a");
    }

    #[test]
    fn deletes_quick_links() {
        let store = CoreStore::in_memory().unwrap();
        let link = QuickLink::create(QuickLinkTarget::new(
            "nas",
            "Share",
            "/Docs",
            None,
            LinkKind::Directory,
        ))
        .unwrap();

        store.save_quick_link(&link).unwrap();
        store.delete_quick_link(&link.id).unwrap();

        assert!(store.list_quick_links().unwrap().is_empty());
    }

    #[test]
    fn replaces_quick_link_for_same_target() {
        let store = CoreStore::in_memory().unwrap();
        let target = QuickLinkTarget::new("nas", "Share", "/Docs/a.pdf", None, LinkKind::File);
        let first = QuickLink::create(target.clone()).unwrap();
        let second = QuickLink::create(target).unwrap();

        store.save_quick_link(&first).unwrap();
        store.save_quick_link(&second).unwrap();
        let links = store.list_quick_links().unwrap();

        assert_eq!(links.len(), 1);
        assert_eq!(links[0].id, second.id);
        assert_eq!(links[0].http_url, first.http_url);
    }

    #[test]
    fn stores_and_selects_server_profiles() {
        let store = CoreStore::in_memory().unwrap();
        let first = ServerProfile::new(
            "设计部 NAS",
            "nas-a.local",
            Some("alice".to_string()),
            AuthMode::UsernamePassword,
        )
        .unwrap();
        let second =
            ServerProfile::new("归档 NAS", "nas-b.local:445", None, AuthMode::CurrentUser).unwrap();

        store.save_server_profile(&first).unwrap();
        store.save_server_profile(&second).unwrap();
        store.set_active_server_profile(&second.id).unwrap();

        let profiles = store.list_server_profiles().unwrap();
        let active = store.active_server_profile().unwrap().unwrap();

        assert_eq!(profiles.len(), 2);
        assert_eq!(active.id, second.id);
        assert_eq!(active.endpoint.as_link_host(), "nas-b.local:445");
    }

    #[test]
    fn saving_inactive_profile_does_not_change_active_server() {
        let store = CoreStore::in_memory().unwrap();
        let active = store
            .save_server_profile_from_parts(
                None,
                "主 NAS",
                "nas-a.local",
                None,
                AuthMode::UsernamePassword,
                SmbDialectPreference::Smb3Preferred,
                true,
            )
            .unwrap();
        let inactive = store
            .save_server_profile_from_parts(
                None,
                "备用 NAS",
                "nas-b.local",
                None,
                AuthMode::UsernamePassword,
                SmbDialectPreference::Smb3Preferred,
                false,
            )
            .unwrap();

        store
            .save_server_profile_from_parts(
                Some(&inactive.id),
                "备用 NAS 改名",
                "nas-b.local",
                None,
                AuthMode::UsernamePassword,
                SmbDialectPreference::Smb3Preferred,
                false,
            )
            .unwrap();

        assert_eq!(
            store.active_server_profile().unwrap().unwrap().id,
            active.id
        );
    }

    #[test]
    fn deletes_server_profile_and_selects_replacement() {
        let store = CoreStore::in_memory().unwrap();
        let first = ServerProfile::new(
            "设计部 NAS",
            "nas-a.local",
            Some("alice".to_string()),
            AuthMode::UsernamePassword,
        )
        .unwrap();
        let second = ServerProfile::new(
            "归档 NAS",
            "nas-b.local:445",
            None,
            AuthMode::UsernamePassword,
        )
        .unwrap();
        store.save_server_profile(&first).unwrap();
        store.save_server_profile(&second).unwrap();
        store.set_active_server_profile(&second.id).unwrap();
        store
            .save_server_credential(
                &ServerCredential::new(&second.id, "bob", "secret", true, false).unwrap(),
            )
            .unwrap();

        store.delete_server_profile(&second.id).unwrap();

        let profiles = store.list_server_profiles().unwrap();
        let active = store.active_server_profile().unwrap().unwrap();
        assert_eq!(profiles.len(), 1);
        assert_eq!(profiles[0].id, first.id);
        assert_eq!(active.id, first.id);
        assert!(store.server_credential(&second.id).unwrap().is_none());
    }

    #[test]
    fn rejects_deleting_last_server_profile() {
        let store = CoreStore::in_memory().unwrap();
        let only = store.ensure_default_server_profile().unwrap();

        let error = store.delete_server_profile(&only.id).unwrap_err();

        assert!(matches!(error, CoreError::InvalidLink(_)));
        assert_eq!(store.list_server_profiles().unwrap().len(), 1);
        assert_eq!(store.active_server_profile().unwrap().unwrap().id, only.id);
    }

    #[test]
    fn finds_server_profile_for_shared_link_target() {
        let store = CoreStore::in_memory().unwrap();
        let profile = ServerProfile::new(
            "媒体 NAS",
            "smb://nas.local:445/Media",
            Some("bob".to_string()),
            AuthMode::UsernamePassword,
        )
        .unwrap();
        let target = QuickLinkTarget::new(
            "NAS.local:445",
            "Media",
            "/Movies/demo.mp4",
            None,
            LinkKind::File,
        );

        store.save_server_profile(&profile).unwrap();
        let matched = store
            .find_server_profile_for_target(&target)
            .unwrap()
            .unwrap();

        assert_eq!(matched.id, profile.id);
        assert_eq!(matched.username.as_deref(), Some("bob"));
    }

    #[test]
    fn ensures_default_server_profile_once() {
        let store = CoreStore::in_memory().unwrap();

        let first = store.ensure_default_server_profile().unwrap();
        let second = store.ensure_default_server_profile().unwrap();
        let profiles = store.list_server_profiles().unwrap();

        assert_eq!(first.id, second.id);
        assert_eq!(profiles.len(), 1);
        assert_eq!(first.display_name, DEFAULT_SERVER_DISPLAY_NAME);
        assert_eq!(first.endpoint.as_link_host(), DEFAULT_SERVER_HOST);
        assert_eq!(store.active_server_profile().unwrap().unwrap().id, first.id);
    }

    #[test]
    fn stores_active_server_credential() {
        let store = CoreStore::in_memory().unwrap();
        let profile = store.ensure_default_server_profile().unwrap();
        let credential = ServerCredential::new(&profile.id, "alice", "secret", true, true).unwrap();

        store.save_server_credential(&credential).unwrap();
        let active = store.active_server_credential().unwrap().unwrap();

        assert_eq!(active.server_profile_id, profile.id);
        assert_eq!(active.username, "alice");
        assert_eq!(active.password, "secret");
        assert!(active.remember_password);
        assert!(active.auto_login);

        let conn = store.lock_conn().unwrap();
        let password_encrypted: String = conn
            .query_row(
                "SELECT password_encrypted FROM server_credentials WHERE server_profile_id = ?1",
                [&profile.id],
                |row| row.get(0),
            )
            .unwrap();
        let columns = table_columns(&conn, "server_credentials").unwrap();

        assert!(is_encrypted_credential(&password_encrypted));
        assert!(!password_encrypted.contains("secret"));
        assert!(!columns.iter().any(|column| column == "password"));
    }

    #[test]
    fn migrates_legacy_plaintext_credentials_without_leaving_password_column() {
        let path = unique_temp_db_path("legacy-credentials");
        {
            let conn = Connection::open(&path).unwrap();
            conn.execute_batch(
                "CREATE TABLE server_credentials (
                    server_profile_id TEXT PRIMARY KEY,
                    username TEXT NOT NULL,
                    password TEXT NOT NULL,
                    remember_password INTEGER NOT NULL,
                    auto_login INTEGER NOT NULL,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO server_credentials
                    (server_profile_id, username, password, remember_password, auto_login, updated_at)
                VALUES
                    ('profile-1', 'alice', 'legacy-secret', 1, 1, '2026-06-17T00:00:00Z');",
            )
            .unwrap();
        }

        let store = CoreStore::open(&path).unwrap();
        let credential = store.server_credential("profile-1").unwrap().unwrap();

        assert_eq!(credential.username, "alice");
        assert_eq!(credential.password, "legacy-secret");
        assert!(credential.remember_password);
        assert!(credential.auto_login);

        let conn = store.lock_conn().unwrap();
        let columns = table_columns(&conn, "server_credentials").unwrap();
        let password_encrypted: String = conn
            .query_row(
                "SELECT password_encrypted FROM server_credentials WHERE server_profile_id = 'profile-1'",
                [],
                |row| row.get(0),
            )
            .unwrap();

        assert!(!columns.iter().any(|column| column == "password"));
        assert!(columns.iter().any(|column| column == "password_encrypted"));
        assert!(is_encrypted_credential(&password_encrypted));
        assert!(!password_encrypted.contains("legacy-secret"));
        drop(conn);
        let _ = std::fs::remove_file(path);
    }

    #[cfg(unix)]
    #[test]
    fn creates_store_file_with_private_permissions() {
        use std::os::unix::fs::PermissionsExt;

        let path = unique_temp_db_path("private-store");
        let _store = CoreStore::open(&path).unwrap();
        let mode = std::fs::metadata(&path).unwrap().permissions().mode() & 0o777;

        assert_eq!(mode, 0o600);
        let _ = std::fs::remove_file(path);
    }

    fn unique_temp_db_path(prefix: &str) -> std::path::PathBuf {
        let nanos = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        std::env::temp_dir().join(format!("rynat-storage-{prefix}-{nanos}.sqlite"))
    }
}
