import Foundation

@_silgen_name("rynat_generate_link_json")
private func rynatGenerateLinkJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_build_link_json")
private func rynatBuildLinkJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_activate_link_json")
private func rynatActivateLinkJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_preview_plan_json")
private func rynatPreviewPlanJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_upload_plan_json")
private func rynatUploadPlanJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_redirect_page_json")
private func rynatRedirectPageJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_open_store_json")
private func rynatOpenStoreJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_app_bootstrap_json")
private func rynatAppBootstrapJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_save_server_profile_json")
private func rynatSaveServerProfileJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_set_active_server_profile_json")
private func rynatSetActiveServerProfileJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_delete_server_profile_json")
private func rynatDeleteServerProfileJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_save_server_credential_json")
private func rynatSaveServerCredentialJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_update_server_credential_options_json")
private func rynatUpdateServerCredentialOptionsJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_delete_server_credential_json")
private func rynatDeleteServerCredentialJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_encrypt_credential_json")
private func rynatEncryptCredentialJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_decrypt_credential_json")
private func rynatDecryptCredentialJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_list_quick_links_json")
private func rynatListQuickLinksJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_delete_quick_link_json")
private func rynatDeleteQuickLinkJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_connect_json")
private func rynatSmbConnectJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_connect_stored_credential_json")
private func rynatSmbConnectStoredCredentialJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_list_directory_json")
private func rynatSmbListDirectoryJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_cache_file_json")
private func rynatSmbCacheFileJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_upload_file_json")
private func rynatSmbUploadFileJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_create_directory_json")
private func rynatSmbCreateDirectoryJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_rename_json")
private func rynatSmbRenameJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_copy_file_json")
private func rynatSmbCopyFileJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_delete_json")
private func rynatSmbDeleteJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_disconnect_json")
private func rynatSmbDisconnectJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_diagnostics_json")
private func rynatSmbDiagnosticsJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_cancel_operation_json")
private func rynatSmbCancelOperationJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_start_task_json")
private func rynatSmbStartTaskJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_poll_task_json")
private func rynatSmbPollTaskJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_cancel_task_json")
private func rynatSmbCancelTaskJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_smb_clear_task_json")
private func rynatSmbClearTaskJSON(_ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?

@_silgen_name("rynat_free_string")
private func rynatFreeString(_ value: UnsafeMutablePointer<CChar>?)

enum RynatCoreError: Error, LocalizedError {
    case encodingFailed
    case bridgeReturnedNull
    case bridgeError(String, code: String?)

    var errorDescription: String? {
        switch self {
        case .encodingFailed:
            return "无法编码 Core 请求"
        case .bridgeReturnedNull:
            return "Core 没有返回结果"
        case .bridgeError(let message, _):
            return message
        }
    }

    var errorCode: String? {
        switch self {
        case .bridgeError(_, let code):
            return code
        default:
            return nil
        }
    }
}

struct RynatCore {
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()

    func generateLink(serverHost: String, share: String, path: String, kind: RynatLinkKind) throws -> QuickLink {
        let request = GenerateLinkRequest(
            serverHost: serverHost,
            share: share,
            path: path,
            kind: kind
        )
        let response: BridgeResponse<QuickLink> = try call(
            request,
            function: rynatGenerateLinkJSON
        )
        return try unwrap(response)
    }

    func buildLink(serverHost: String, share: String, path: String, kind: RynatLinkKind) throws -> QuickLink {
        let request = BuildLinkRequest(
            serverHost: serverHost,
            share: share,
            path: path,
            kind: kind
        )
        let response: BridgeResponse<QuickLink> = try call(
            request,
            function: rynatBuildLinkJSON
        )
        return try unwrap(response)
    }

    func activateLink(_ rawLink: String) throws -> LinkActivation {
        let request = ActivateLinkRequest(rawLink: rawLink)
        let response: BridgeResponse<LinkActivation> = try call(
            request,
            function: rynatActivateLinkJSON
        )
        return try unwrap(response)
    }

    func previewPlan(serverHost: String, share: String, path: String, kind: RynatLinkKind, maxEdgePx: UInt32 = 512) throws -> PreviewPlan {
        let request = PreviewPlanRequest(
            serverHost: serverHost,
            share: share,
            path: path,
            kind: kind,
            maxEdgePx: maxEdgePx
        )
        let response: BridgeResponse<PreviewPlan> = try call(
            request,
            function: rynatPreviewPlanJSON
        )
        return try unwrap(response)
    }

    func uploadPlan(localPath: String, serverHost: String, share: String, remotePath: String) throws -> TransferPlan {
        let request = UploadPlanRequest(
            localPath: localPath,
            serverHost: serverHost,
            share: share,
            remotePath: remotePath
        )
        let response: BridgeResponse<TransferPlan> = try call(
            request,
            function: rynatUploadPlanJSON
        )
        return try unwrap(response)
    }

    func redirectPage(targetURL: String) throws -> String {
        let request = RedirectPageRequest(targetURL: targetURL)
        let response: BridgeResponse<String> = try call(
            request,
            function: rynatRedirectPageJSON
        )
        return try unwrap(response)
    }

    func openStore(path: String) throws -> AppBootstrapState {
        let response: BridgeResponse<AppBootstrapState> = try call(
            OpenStoreRequest(path: path),
            function: rynatOpenStoreJSON
        )
        return try unwrap(response)
    }

    func appBootstrap() throws -> AppBootstrapState {
        let response: BridgeResponse<AppBootstrapState> = try call(
            EmptyRequest(),
            function: rynatAppBootstrapJSON
        )
        return try unwrap(response)
    }

    func saveServerProfile(
        id: String?,
        displayName: String,
        host: String,
        username: String?,
        setActive: Bool
    ) throws -> StoredServerProfile {
        let response: BridgeResponse<StoredServerProfile> = try call(
            SaveServerProfileRequest(
                id: id,
                displayName: displayName,
                host: host,
                username: username,
                authMode: "username_password",
                dialectPreference: "smb3_preferred",
                setActive: setActive
            ),
            function: rynatSaveServerProfileJSON
        )
        return try unwrap(response)
    }

    func setActiveServerProfile(id: String) throws -> AppBootstrapState {
        let response: BridgeResponse<AppBootstrapState> = try call(
            SetActiveServerProfileRequest(id: id),
            function: rynatSetActiveServerProfileJSON
        )
        return try unwrap(response)
    }

    func deleteServerProfile(id: String) throws -> AppBootstrapState {
        let response: BridgeResponse<AppBootstrapState> = try call(
            DeleteServerProfileRequest(id: id),
            function: rynatDeleteServerProfileJSON
        )
        return try unwrap(response)
    }

    func saveServerCredential(
        serverProfileID: String,
        username: String,
        password: String,
        rememberPassword: Bool,
        autoLogin: Bool
    ) throws -> StoredServerCredential {
        let response: BridgeResponse<StoredServerCredential> = try call(
            SaveServerCredentialRequest(
                serverProfileID: serverProfileID,
                username: username,
                password: password,
                rememberPassword: rememberPassword,
                autoLogin: autoLogin
            ),
            function: rynatSaveServerCredentialJSON
        )
        return try unwrap(response)
    }

    func updateServerCredentialOptions(
        serverProfileID: String,
        rememberPassword: Bool,
        autoLogin: Bool
    ) throws -> StoredServerCredential? {
        let response: BridgeResponse<StoredServerCredential> = try call(
            UpdateServerCredentialOptionsRequest(
                serverProfileID: serverProfileID,
                rememberPassword: rememberPassword,
                autoLogin: autoLogin
            ),
            function: rynatUpdateServerCredentialOptionsJSON
        )
        if response.ok {
            return response.data
        }
        throw RynatCoreError.bridgeError(response.error ?? "Core 返回未知错误", code: response.errorCode)
    }

    func deleteServerCredential(serverProfileID: String) throws {
        let response: BridgeResponse<Bool> = try call(
            DeleteServerCredentialRequest(serverProfileID: serverProfileID),
            function: rynatDeleteServerCredentialJSON
        )
        _ = try unwrap(response)
    }

    func encryptCredential(password: String) throws -> String {
        let response: BridgeResponse<String> = try call(
            EncryptCredentialRequest(password: password),
            function: rynatEncryptCredentialJSON
        )
        return try unwrap(response)
    }

    func decryptCredential(encrypted: String) throws -> String {
        let response: BridgeResponse<String> = try call(
            DecryptCredentialRequest(encrypted: encrypted),
            function: rynatDecryptCredentialJSON
        )
        return try unwrap(response)
    }

    func listQuickLinks() throws -> [QuickLink] {
        let response: BridgeResponse<[QuickLink]> = try call(
            EmptyRequest(),
            function: rynatListQuickLinksJSON
        )
        return try unwrap(response)
    }

    func deleteQuickLink(id: String) throws {
        let response: BridgeResponse<Bool> = try call(
            DeleteQuickLinkRequest(id: id),
            function: rynatDeleteQuickLinkJSON
        )
        _ = try unwrap(response)
    }

    func smbConnect(host: String, username: String, password: String, connectionID: String? = nil) throws -> SmbConnectResult {
        let request = SmbConnectRequest(
            host: host,
            username: username,
            password: password,
            connectionID: connectionID
        )
        let response: BridgeResponse<SmbConnectResult> = try call(
            request,
            function: rynatSmbConnectJSON
        )
        return try unwrap(response)
    }

    func smbConnectStoredCredential(serverProfileID: String, connectionID: String? = nil) throws -> SmbConnectResult {
        let request = SmbConnectStoredCredentialRequest(
            serverProfileID: serverProfileID,
            connectionID: connectionID
        )
        let response: BridgeResponse<SmbConnectResult> = try call(
            request,
            function: rynatSmbConnectStoredCredentialJSON
        )
        return try unwrap(response)
    }

    func smbListDirectory(share: String, path: String, connectionID: String? = nil, operationID: String? = nil) throws -> [SmbFileItem] {
        let request = SmbListDirectoryRequest(
            connectionID: connectionID,
            share: share,
            path: path,
            operationID: operationID
        )
        let response: BridgeResponse<[SmbFileItem]> = try call(
            request,
            function: rynatSmbListDirectoryJSON
        )
        return try unwrap(response)
    }

    func smbCacheFile(share: String, path: String, localPath: String, maxBytes: UInt64? = nil, connectionID: String? = nil, operationID: String? = nil) throws -> SmbCachedFile {
        let request = SmbCacheFileRequest(
            connectionID: connectionID,
            share: share,
            path: path,
            localPath: localPath,
            maxBytes: maxBytes,
            operationID: operationID
        )
        let response: BridgeResponse<SmbCachedFile> = try call(
            request,
            function: rynatSmbCacheFileJSON
        )
        return try unwrap(response)
    }

    func smbUploadFile(share: String, localPath: String, remotePath: String, replaceExisting: Bool = false, connectionID: String? = nil, operationID: String? = nil) throws -> SmbWriteResult {
        let request = SmbUploadFileRequest(
            connectionID: connectionID,
            share: share,
            localPath: localPath,
            remotePath: remotePath,
            replaceExisting: replaceExisting,
            operationID: operationID
        )
        let response: BridgeResponse<SmbWriteResult> = try call(
            request,
            function: rynatSmbUploadFileJSON
        )
        return try unwrap(response)
    }

    func smbCreateDirectory(share: String, path: String, connectionID: String? = nil, operationID: String? = nil) throws -> SmbWriteResult {
        let request = SmbCreateDirectoryRequest(
            connectionID: connectionID,
            share: share,
            path: path,
            operationID: operationID
        )
        let response: BridgeResponse<SmbWriteResult> = try call(
            request,
            function: rynatSmbCreateDirectoryJSON
        )
        return try unwrap(response)
    }

    func smbRename(share: String, fromPath: String, toPath: String, connectionID: String? = nil, operationID: String? = nil) throws -> SmbWriteResult {
        let request = SmbRenameRequest(
            connectionID: connectionID,
            share: share,
            fromPath: fromPath,
            toPath: toPath,
            operationID: operationID
        )
        let response: BridgeResponse<SmbWriteResult> = try call(
            request,
            function: rynatSmbRenameJSON
        )
        return try unwrap(response)
    }

    func smbCopyFile(sourceShare: String, sourcePath: String, targetShare: String, targetPath: String, replaceExisting: Bool = false, connectionID: String? = nil, operationID: String? = nil) throws -> SmbWriteResult {
        let request = SmbCopyFileRequest(
            connectionID: connectionID,
            sourceShare: sourceShare,
            sourcePath: sourcePath,
            targetShare: targetShare,
            targetPath: targetPath,
            replaceExisting: replaceExisting,
            operationID: operationID
        )
        let response: BridgeResponse<SmbWriteResult> = try call(
            request,
            function: rynatSmbCopyFileJSON
        )
        return try unwrap(response)
    }

    func smbDelete(share: String, path: String, isDir: Bool, connectionID: String? = nil, operationID: String? = nil) throws -> SmbWriteResult {
        let request = SmbDeleteRequest(
            connectionID: connectionID,
            share: share,
            path: path,
            isDir: isDir,
            operationID: operationID
        )
        let response: BridgeResponse<SmbWriteResult> = try call(
            request,
            function: rynatSmbDeleteJSON
        )
        return try unwrap(response)
    }

    func smbDisconnect(connectionID: String? = nil) throws {
        let response: BridgeResponse<Bool> = try call(
            SmbConnectionScopedRequest(connectionID: connectionID),
            function: rynatSmbDisconnectJSON
        )
        _ = try unwrap(response)
    }

    func smbDiagnostics(connectionID: String? = nil) throws -> SmbDiagnostics {
        let response: BridgeResponse<SmbDiagnostics> = try call(
            SmbConnectionScopedRequest(connectionID: connectionID),
            function: rynatSmbDiagnosticsJSON
        )
        return try unwrap(response)
    }

    func smbCancelOperation(_ operationID: String) throws {
        let response: BridgeResponse<Bool> = try call(
            SmbCancelOperationRequest(operationID: operationID),
            function: rynatSmbCancelOperationJSON
        )
        _ = try unwrap(response)
    }

    func smbStartTask(
        operation: SmbTaskOperation,
        payload: [String: JSONValue],
        operationID: String? = nil,
        serverProfileID: String? = nil,
        useIsolatedConnection: Bool? = nil
    ) throws -> SmbTaskStartResult {
        let response: BridgeResponse<SmbTaskStartResult> = try call(
            SmbStartTaskRequest(
                operation: operation,
                payload: payload,
                operationID: operationID,
                serverProfileID: serverProfileID,
                useIsolatedConnection: useIsolatedConnection
            ),
            function: rynatSmbStartTaskJSON
        )
        return try unwrap(response)
    }

    func smbPollTask(_ taskID: String) throws -> SmbTaskStatus {
        let response: BridgeResponse<SmbTaskStatus> = try call(
            SmbTaskRequest(taskID: taskID),
            function: rynatSmbPollTaskJSON
        )
        return try unwrap(response)
    }

    func smbCancelTask(_ taskID: String) throws -> SmbTaskStatus {
        let response: BridgeResponse<SmbTaskStatus> = try call(
            SmbTaskRequest(taskID: taskID),
            function: rynatSmbCancelTaskJSON
        )
        return try unwrap(response)
    }

    func smbClearTask(_ taskID: String) throws {
        let response: BridgeResponse<Bool> = try call(
            SmbTaskRequest(taskID: taskID),
            function: rynatSmbClearTaskJSON
        )
        _ = try unwrap(response)
    }

    private func call<Request: Encodable, Response: Decodable>(
        _ request: Request,
        function: (UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?
    ) throws -> Response {
        let data = try encoder.encode(request)
        guard let json = String(data: data, encoding: .utf8) else {
            throw RynatCoreError.encodingFailed
        }

        return try json.withCString { pointer in
            guard let output = function(pointer) else {
                throw RynatCoreError.bridgeReturnedNull
            }
            defer { rynatFreeString(output) }
            let outputJSON = String(cString: output)
            let outputData = Data(outputJSON.utf8)
            return try decoder.decode(Response.self, from: outputData)
        }
    }

    private func unwrap<T>(_ response: BridgeResponse<T>) throws -> T {
        if response.ok, let data = response.data {
            return data
        }
        throw RynatCoreError.bridgeError(response.error ?? "Core 返回未知错误", code: response.errorCode)
    }
}

struct BridgeResponse<T: Decodable>: Decodable {
    let ok: Bool
    let data: T?
    let error: String?
    let errorCode: String?

    enum CodingKeys: String, CodingKey {
        case ok
        case data
        case error
        case errorCode = "error_code"
    }
}

struct GenerateLinkRequest: Encodable {
    let serverHost: String
    let share: String
    let path: String
    let kind: RynatLinkKind

    enum CodingKeys: String, CodingKey {
        case serverHost = "server_host"
        case share
        case path
        case kind
    }
}

struct BuildLinkRequest: Encodable {
    let serverHost: String
    let share: String
    let path: String
    let kind: RynatLinkKind

    enum CodingKeys: String, CodingKey {
        case serverHost = "server_host"
        case share
        case path
        case kind
    }
}

struct ActivateLinkRequest: Encodable {
    let rawLink: String

    enum CodingKeys: String, CodingKey {
        case rawLink = "raw_link"
    }
}

struct PreviewPlanRequest: Encodable {
    let serverHost: String
    let share: String
    let path: String
    let kind: RynatLinkKind
    let maxEdgePx: UInt32

    enum CodingKeys: String, CodingKey {
        case serverHost = "server_host"
        case share
        case path
        case kind
        case maxEdgePx = "max_edge_px"
    }
}

struct UploadPlanRequest: Encodable {
    let localPath: String
    let serverHost: String
    let share: String
    let remotePath: String

    enum CodingKeys: String, CodingKey {
        case localPath = "local_path"
        case serverHost = "server_host"
        case share
        case remotePath = "remote_path"
    }
}

struct RedirectPageRequest: Encodable {
    let targetURL: String

    enum CodingKeys: String, CodingKey {
        case targetURL = "target_url"
    }
}

struct OpenStoreRequest: Encodable {
    let path: String
}

struct SaveServerProfileRequest: Encodable {
    let id: String?
    let displayName: String
    let host: String
    let username: String?
    let authMode: String
    let dialectPreference: String
    let setActive: Bool

    enum CodingKeys: String, CodingKey {
        case id
        case displayName = "display_name"
        case host
        case username
        case authMode = "auth_mode"
        case dialectPreference = "dialect_preference"
        case setActive = "set_active"
    }
}

struct SetActiveServerProfileRequest: Encodable {
    let id: String
}

struct DeleteServerProfileRequest: Encodable {
    let id: String
}

struct SaveServerCredentialRequest: Encodable {
    let serverProfileID: String
    let username: String
    let password: String
    let rememberPassword: Bool
    let autoLogin: Bool

    enum CodingKeys: String, CodingKey {
        case serverProfileID = "server_profile_id"
        case username
        case password
        case rememberPassword = "remember_password"
        case autoLogin = "auto_login"
    }
}

struct UpdateServerCredentialOptionsRequest: Encodable {
    let serverProfileID: String
    let rememberPassword: Bool
    let autoLogin: Bool

    enum CodingKeys: String, CodingKey {
        case serverProfileID = "server_profile_id"
        case rememberPassword = "remember_password"
        case autoLogin = "auto_login"
    }
}

struct DeleteServerCredentialRequest: Encodable {
    let serverProfileID: String

    enum CodingKeys: String, CodingKey {
        case serverProfileID = "server_profile_id"
    }
}

struct EncryptCredentialRequest: Encodable {
    let password: String
}

struct DecryptCredentialRequest: Encodable {
    let encrypted: String
}

struct DeleteQuickLinkRequest: Encodable {
    let id: String
}

struct SmbConnectRequest: Encodable {
    let host: String
    let username: String
    let password: String
    let connectionID: String?

    enum CodingKeys: String, CodingKey {
        case host
        case username
        case password
        case connectionID = "connection_id"
    }
}

struct SmbConnectStoredCredentialRequest: Encodable {
    let serverProfileID: String
    let connectionID: String?

    enum CodingKeys: String, CodingKey {
        case serverProfileID = "server_profile_id"
        case connectionID = "connection_id"
    }
}

struct SmbListDirectoryRequest: Encodable {
    let connectionID: String?
    let share: String
    let path: String
    let operationID: String?

    enum CodingKeys: String, CodingKey {
        case connectionID = "connection_id"
        case share
        case path
        case operationID = "operation_id"
    }
}

struct SmbCacheFileRequest: Encodable {
    let connectionID: String?
    let share: String
    let path: String
    let localPath: String
    let maxBytes: UInt64?
    let operationID: String?

    enum CodingKeys: String, CodingKey {
        case connectionID = "connection_id"
        case share
        case path
        case localPath = "local_path"
        case maxBytes = "max_bytes"
        case operationID = "operation_id"
    }
}

struct SmbUploadFileRequest: Encodable {
    let connectionID: String?
    let share: String
    let localPath: String
    let remotePath: String
    let replaceExisting: Bool
    let operationID: String?

    enum CodingKeys: String, CodingKey {
        case connectionID = "connection_id"
        case share
        case localPath = "local_path"
        case remotePath = "remote_path"
        case replaceExisting = "replace_existing"
        case operationID = "operation_id"
    }
}

struct SmbCreateDirectoryRequest: Encodable {
    let connectionID: String?
    let share: String
    let path: String
    let operationID: String?

    enum CodingKeys: String, CodingKey {
        case connectionID = "connection_id"
        case share
        case path
        case operationID = "operation_id"
    }
}

struct SmbRenameRequest: Encodable {
    let connectionID: String?
    let share: String
    let fromPath: String
    let toPath: String
    let operationID: String?

    enum CodingKeys: String, CodingKey {
        case connectionID = "connection_id"
        case share
        case fromPath = "from_path"
        case toPath = "to_path"
        case operationID = "operation_id"
    }
}

struct SmbCopyFileRequest: Encodable {
    let connectionID: String?
    let sourceShare: String
    let sourcePath: String
    let targetShare: String
    let targetPath: String
    let replaceExisting: Bool
    let operationID: String?

    enum CodingKeys: String, CodingKey {
        case connectionID = "connection_id"
        case sourceShare = "source_share"
        case sourcePath = "source_path"
        case targetShare = "target_share"
        case targetPath = "target_path"
        case replaceExisting = "replace_existing"
        case operationID = "operation_id"
    }
}

struct SmbCancelOperationRequest: Encodable {
    let operationID: String

    enum CodingKeys: String, CodingKey {
        case operationID = "operation_id"
    }
}

enum SmbTaskOperation: String, Codable {
    case cacheFile = "cache_file"
    case uploadFile = "upload_file"
    case copyFile = "copy_file"
    case delete
    case createDirectory = "create_directory"
    case rename
    case listDirectory = "list_directory"
}

enum SmbTaskState: String, Codable {
    case queued
    case running
    case succeeded
    case failed
    case cancelled
}

struct SmbStartTaskRequest: Encodable {
    let operation: SmbTaskOperation
    let payload: [String: JSONValue]
    let operationID: String?
    let serverProfileID: String?
    let useIsolatedConnection: Bool?

    enum CodingKeys: String, CodingKey {
        case operation
        case payload
        case operationID = "operation_id"
        case serverProfileID = "server_profile_id"
        case useIsolatedConnection = "use_isolated_connection"
    }
}

struct SmbTaskRequest: Encodable {
    let taskID: String

    enum CodingKeys: String, CodingKey {
        case taskID = "task_id"
    }
}

struct SmbTaskStartResult: Decodable {
    let taskID: String
    let operationID: String
    let state: SmbTaskState

    enum CodingKeys: String, CodingKey {
        case taskID = "task_id"
        case operationID = "operation_id"
        case state
    }
}

struct SmbTaskStatus: Decodable {
    let taskID: String
    let operationID: String
    let operation: SmbTaskOperation
    let state: SmbTaskState
    let startedAtMS: UInt64
    let finishedAtMS: UInt64?
    let data: JSONValue?
    let error: String?
    let errorCode: String?

    enum CodingKeys: String, CodingKey {
        case taskID = "task_id"
        case operationID = "operation_id"
        case operation
        case state
        case startedAtMS = "started_at_ms"
        case finishedAtMS = "finished_at_ms"
        case data
        case error
        case errorCode = "error_code"
    }
}

struct SmbDeleteRequest: Encodable {
    let connectionID: String?
    let share: String
    let path: String
    let isDir: Bool
    let operationID: String?

    enum CodingKeys: String, CodingKey {
        case connectionID = "connection_id"
        case share
        case path
        case isDir = "is_dir"
        case operationID = "operation_id"
    }
}

struct SmbConnectionScopedRequest: Encodable {
    let connectionID: String?

    enum CodingKeys: String, CodingKey {
        case connectionID = "connection_id"
    }
}

private struct EmptyRequest: Encodable {}

enum JSONValue: Codable {
    case string(String)
    case int(Int)
    case uint64(UInt64)
    case double(Double)
    case bool(Bool)
    case object([String: JSONValue])
    case array([JSONValue])
    case null

    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() {
            self = .null
        } else if let value = try? container.decode(Bool.self) {
            self = .bool(value)
        } else if let value = try? container.decode(Int.self) {
            self = .int(value)
        } else if let value = try? container.decode(UInt64.self) {
            self = .uint64(value)
        } else if let value = try? container.decode(Double.self) {
            self = .double(value)
        } else if let value = try? container.decode(String.self) {
            self = .string(value)
        } else if let value = try? container.decode([String: JSONValue].self) {
            self = .object(value)
        } else if let value = try? container.decode([JSONValue].self) {
            self = .array(value)
        } else {
            throw DecodingError.dataCorruptedError(in: container, debugDescription: "Unsupported JSON value")
        }
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .string(let value):
            try container.encode(value)
        case .int(let value):
            try container.encode(value)
        case .uint64(let value):
            try container.encode(value)
        case .double(let value):
            try container.encode(value)
        case .bool(let value):
            try container.encode(value)
        case .object(let value):
            try container.encode(value)
        case .array(let value):
            try container.encode(value)
        case .null:
            try container.encodeNil()
        }
    }
}

enum RynatLinkKind: String, Codable {
    case file
    case dir
    case unknown
}

struct QuickLink: Decodable {
    let id: String
    let target: QuickLinkTarget
    let httpURL: String
    let deepLinkURL: String
    let createdAt: String

    enum CodingKeys: String, CodingKey {
        case id
        case target
        case httpURL = "http_url"
        case deepLinkURL = "deep_link_url"
        case createdAt = "created_at"
    }
}

struct QuickLinkTarget: Decodable {
    let serverHost: String
    let share: String
    let path: String
    let name: String?
    let kind: RynatLinkKind

    enum CodingKeys: String, CodingKey {
        case serverHost = "server_host"
        case share
        case path
        case name
        case kind
    }
}

struct LinkActivation: Decodable {
    let target: QuickLinkTarget
    let matchedServer: StoredServerProfile?
    let browseLocation: BrowseLocation
    let previewPlan: PreviewPlan?

    enum CodingKeys: String, CodingKey {
        case target
        case matchedServer = "matched_server"
        case browseLocation = "browse_location"
        case previewPlan = "preview_plan"
    }
}

struct AppBootstrapState: Decodable {
    let serverProfiles: [StoredServerProfile]
    let activeServer: StoredServerProfile?
    let activeCredential: StoredServerCredential?

    enum CodingKeys: String, CodingKey {
        case serverProfiles = "server_profiles"
        case activeServer = "active_server"
        case activeCredential = "active_credential"
    }
}

struct StoredServerProfile: Decodable {
    let id: String
    let displayName: String
    let endpoint: StoredServerEndpoint
    let username: String?
    let authMode: String
    let dialectPreference: String
    let createdAt: String
    let updatedAt: String

    var linkHost: String {
        if let port = endpoint.port {
            return "\(endpoint.host):\(port)"
        }
        return endpoint.host
    }

    enum CodingKeys: String, CodingKey {
        case id
        case displayName = "display_name"
        case endpoint
        case username
        case authMode = "auth_mode"
        case dialectPreference = "dialect_preference"
        case createdAt = "created_at"
        case updatedAt = "updated_at"
    }
}

struct StoredServerEndpoint: Decodable {
    let host: String
    let port: UInt16?
}

struct StoredServerCredential: Decodable {
    let serverProfileID: String
    let username: String
    let rememberPassword: Bool
    let autoLogin: Bool
    let updatedAt: String

    enum CodingKeys: String, CodingKey {
        case serverProfileID = "server_profile_id"
        case username
        case rememberPassword = "remember_password"
        case autoLogin = "auto_login"
        case updatedAt = "updated_at"
    }
}

struct BrowseLocation: Decodable {
    let serverHost: String
    let share: String
    let remotePath: String
    let selectedPath: String?

    enum CodingKeys: String, CodingKey {
        case serverHost = "server_host"
        case share
        case remotePath = "remote_path"
        case selectedPath = "selected_path"
    }
}

struct PreviewPlan: Decodable {
    let contentType: String
    let cacheKey: String
    let maxEdgePx: UInt32
    let thumbnail: PreviewAsset?
    let playback: PreviewAsset?

    enum CodingKeys: String, CodingKey {
        case contentType = "content_type"
        case cacheKey = "cache_key"
        case maxEdgePx = "max_edge_px"
        case thumbnail
        case playback
    }
}

struct PreviewAsset: Decodable {
    let kind: String
    let url: String
    let cacheKey: String

    enum CodingKeys: String, CodingKey {
        case kind
        case url
        case cacheKey = "cache_key"
    }
}

struct TransferPlan: Decodable {
    let direction: String
    let source: TransferEndpoint
    let destination: TransferEndpoint
    let bufferBytes: UInt32
    let requiresStreaming: Bool
    let allowUIMemoryCopy: Bool

    enum CodingKeys: String, CodingKey {
        case direction
        case source
        case destination
        case bufferBytes = "buffer_bytes"
        case requiresStreaming = "requires_streaming"
        case allowUIMemoryCopy = "allow_ui_memory_copy"
    }
}

struct TransferEndpoint: Decodable {
    let remote: QuickLinkTarget?
    let localFile: LocalFileEndpoint?

    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if let remote = try? container.decode(RemoteEndpointWrapper.self) {
            self.remote = remote.remote
            self.localFile = nil
            return
        }
        let local = try container.decode(LocalFileEndpointWrapper.self)
        self.remote = nil
        self.localFile = local.localFile
    }
}

private struct RemoteEndpointWrapper: Decodable {
    let remote: QuickLinkTarget
}

private struct LocalFileEndpointWrapper: Decodable {
    let localFile: LocalFileEndpoint

    enum CodingKeys: String, CodingKey {
        case localFile = "local_file"
    }
}

struct LocalFileEndpoint: Decodable {
    let path: String
}

struct SmbConnectResult: Decodable {
    let connectionID: String
    let host: String
    let dialectLabel: String
    let shares: [SmbShare]

    enum CodingKeys: String, CodingKey {
        case connectionID = "connection_id"
        case host
        case dialectLabel = "dialect_label"
        case shares
    }
}

struct SmbShare: Decodable {
    let name: String
    let comment: String
}

struct SmbFileItem: Decodable {
    let name: String
    let path: String
    let isDir: Bool
    let size: UInt64?
    let modifiedTime: Int64?

    enum CodingKeys: String, CodingKey {
        case name
        case path
        case isDir = "is_dir"
        case size
        case modifiedTime = "modified_time"
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        name = try container.decode(String.self, forKey: .name)
        path = try container.decode(String.self, forKey: .path)
        isDir = try container.decode(Bool.self, forKey: .isDir)
        size = try container.decodeIfPresent(UInt64.self, forKey: .size)
        modifiedTime = try container.decodeIfPresent(Int64.self, forKey: .modifiedTime)
    }
}

struct SmbCachedFile: Decodable {
    let localPath: String
    let size: UInt64

    enum CodingKeys: String, CodingKey {
        case localPath = "local_path"
        case size
    }
}

struct SmbWriteResult: Decodable {
    let path: String
    let size: UInt64
    let copyMethod: SmbCopyMethod?
    let copyFallbackReason: String?

    enum CodingKeys: String, CodingKey {
        case path
        case size
        case copyMethod = "copy_method"
        case copyFallbackReason = "copy_fallback_reason"
    }
}

enum SmbCopyMethod: String, Decodable {
    case serverSide = "server_side"
    case streamedFallback = "streamed_fallback"
}

struct SmbDiagnostics: Decodable {
    let connected: Bool
    let connectionID: String?
    let host: String?
    let cachedShareCount: Int
    let lastCopyMethod: SmbCopyMethod?
    let lastCopyFallbackReason: String?

    enum CodingKeys: String, CodingKey {
        case connected
        case connectionID = "connection_id"
        case host
        case cachedShareCount = "cached_share_count"
        case lastCopyMethod = "last_copy_method"
        case lastCopyFallbackReason = "last_copy_fallback_reason"
    }
}
