import Foundation

enum PreviewCacheKind {
    case preview
    case playback
}

struct PreviewCacheResult {
    let kind: PreviewCacheKind
    let operationID: String
    let itemPath: String
    let localURL: URL
}

final class PreviewCoordinator {
    private let cancelOperation: (String) -> Void
    private var taskIDsByOperationID: [String: String] = [:]
    private var activePreviewOperationIDs: Set<String> = []
    private var currentPreviewOperationID: String?
    private var activePlaybackOperationIDs: Set<String> = []
    private var currentPlaybackOperationID: String?

    init(cancelOperation: @escaping (String) -> Void) {
        self.cancelOperation = cancelOperation
    }

    func cache(
        kind: PreviewCacheKind,
        itemPath: String,
        share: String,
        remotePath: String,
        localURL: URL,
        maxBytes: UInt64? = nil,
        connectionID: String,
        serverProfileID: String? = nil,
        completion: @escaping (Result<PreviewCacheResult, Error>) -> Void
    ) {
        cancelCurrent(kind: kind)
        let operationID = UUID().uuidString
        setCurrent(operationID, for: kind)

        DispatchQueue.global(qos: .utility).async {
            do {
                var isStillActive = false
                DispatchQueue.main.sync { [weak self] in
                    isStillActive = self?.contains(operationID, kind: kind) ?? false
                }
                guard isStillActive else {
                    return
                }
                let core = RynatCore()
                let payload: [String: JSONValue] = [
                    "connection_id": .string(connectionID),
                    "share": .string(share),
                    "path": .string(remotePath),
                    "local_path": .string(localURL.path),
                    "max_bytes": maxBytes.map(JSONValue.uint64) ?? .null,
                ]
                let started = try core.smbStartTask(
                    operation: .cacheFile,
                    payload: payload,
                    operationID: operationID,
                    serverProfileID: serverProfileID,
                    useIsolatedConnection: true
                )
                DispatchQueue.main.async { [weak self] in
                    guard let self, self.contains(operationID, kind: kind) else {
                        _ = try? core.smbCancelTask(started.taskID)
                        return
                    }
                    self.taskIDsByOperationID[operationID] = started.taskID
                }
                try Self.waitForTask(core: core, taskID: started.taskID)
                try? core.smbClearTask(started.taskID)
                DispatchQueue.main.async { [weak self] in
                    guard let self, self.finish(operationID, kind: kind) else {
                        return
                    }
                    completion(.success(PreviewCacheResult(
                        kind: kind,
                        operationID: operationID,
                        itemPath: itemPath,
                        localURL: localURL
                    )))
                }
            } catch {
                DispatchQueue.main.async { [weak self] in
                    guard let self, self.finish(operationID, kind: kind) else {
                        return
                    }
                    completion(.failure(error))
                }
            }
        }
    }

    private func contains(_ operationID: String, kind: PreviewCacheKind) -> Bool {
        switch kind {
        case .preview:
            activePreviewOperationIDs.contains(operationID)
        case .playback:
            activePlaybackOperationIDs.contains(operationID)
        }
    }

    func cancelAll() {
        for operationID in activePreviewOperationIDs {
            cancel(operationID: operationID)
        }
        activePreviewOperationIDs.removeAll()
        currentPreviewOperationID = nil

        for operationID in activePlaybackOperationIDs {
            cancel(operationID: operationID)
        }
        activePlaybackOperationIDs.removeAll()
        currentPlaybackOperationID = nil
        taskIDsByOperationID.removeAll()
    }

    private func cancelCurrent(kind: PreviewCacheKind) {
        switch kind {
        case .preview:
            if let operationID = currentPreviewOperationID {
                cancel(operationID: operationID)
            }
        case .playback:
            if let operationID = currentPlaybackOperationID {
                cancel(operationID: operationID)
            }
        }
    }

    private func setCurrent(_ operationID: String, for kind: PreviewCacheKind) {
        switch kind {
        case .preview:
            currentPreviewOperationID = operationID
            activePreviewOperationIDs.insert(operationID)
        case .playback:
            currentPlaybackOperationID = operationID
            activePlaybackOperationIDs.insert(operationID)
        }
    }

    private func finish(_ operationID: String, kind: PreviewCacheKind) -> Bool {
        taskIDsByOperationID.removeValue(forKey: operationID)
        switch kind {
        case .preview:
            activePreviewOperationIDs.remove(operationID)
            guard currentPreviewOperationID == operationID else {
                return false
            }
            currentPreviewOperationID = nil
            return true
        case .playback:
            activePlaybackOperationIDs.remove(operationID)
            guard currentPlaybackOperationID == operationID else {
                return false
            }
            currentPlaybackOperationID = nil
            return true
        }
    }

    private func cancel(operationID: String) {
        if let taskID = taskIDsByOperationID[operationID] {
            _ = try? RynatCore().smbCancelTask(taskID)
        }
        cancelOperation(operationID)
    }

    private static func waitForTask(core: RynatCore, taskID: String) throws {
        while true {
            let status = try core.smbPollTask(taskID)
            switch status.state {
            case .queued, .running:
                Thread.sleep(forTimeInterval: 0.08)
            case .succeeded:
                return
            case .cancelled:
                throw RynatCoreError.bridgeError("操作已取消", code: "cancelled")
            case .failed:
                throw RynatCoreError.bridgeError(status.error ?? "操作失败", code: status.errorCode)
            }
        }
    }
}
