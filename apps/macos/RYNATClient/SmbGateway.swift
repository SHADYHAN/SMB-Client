import Foundation

final class SmbGateway {
    private let status: (String) -> Void
    private let isSessionActive: (RemoteWriteSessionContext) -> Bool

    init(
        status: @escaping (String) -> Void,
        isSessionActive: @escaping (RemoteWriteSessionContext) -> Bool
    ) {
        self.status = status
        self.isSessionActive = isSessionActive
    }

    func run<T>(
        context: RemoteWriteSessionContext,
        operation: (RynatCore, String) throws -> T
    ) throws -> T {
        try ensureSessionActive(context)
        do {
            let result = try operation(RynatCore(), context.connectionID)
            try ensureSessionActive(context)
            return result
        } catch {
            guard isReconnectable(error) else {
                throw error
            }
            try ensureSessionActive(context)
            DispatchQueue.main.async { [status] in
                status("连接已断开，正在重连")
            }
            do {
                _ = try RynatCore().smbConnectStoredCredential(
                    serverProfileID: context.serverProfileID,
                    connectionID: context.connectionID
                )
            } catch {
                if let coreError = error as? RynatCoreError, coreError.errorCode == "credential" {
                    throw RynatCoreError.bridgeError("连接已断开，请重新登录", code: "credential")
                }
                throw error
            }
            try ensureSessionActive(context)
            return try operation(RynatCore(), context.connectionID)
        }
    }

    private func ensureSessionActive(_ context: RemoteWriteSessionContext) throws {
        guard isSessionActive(context) else {
            throw RynatCoreError.bridgeError("连接已断开，请重新登录", code: "credential")
        }
    }

    private func isReconnectable(_ error: Error) -> Bool {
        if let coreError = error as? RynatCoreError {
            return coreError.errorCode == "reconnectable"
        }
        return false
    }
}
