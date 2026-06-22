import AppKit
import Foundation

// MARK: - Transfer

extension WorkspaceController {
    func copyEntryRecursively(
        _ entry: FileClipboardEntry,
        targetShare: String,
        targetPath: String,
        task: OperationTask,
        context: RemoteWriteSessionContext,
        operation: RemoteOperationContext,
        onProcessed: () -> Void,
        onDiscovered: (Int) -> Void
    ) throws -> (copied: Int, skipped: Int) {
        if task.isCancelled {
            return (0, 0)
        }

        guard try resolveConflictIfNeeded(
            share: targetShare,
            path: targetPath,
            name: entry.name,
            isDirectory: entry.isDirectory,
            task: task,
            operation: operation
        ) == .replace else {
            onProcessed()
            return (0, 1)
        }

        let existingItem = try operation.existingItem(share: targetShare, path: targetPath)
        if let existingItem, entry.isDirectory || existingItem.isDirectory {
            onDiscovered(1)
            try deleteRemotePathRecursively(
                share: targetShare,
                path: targetPath,
                isDirectory: existingItem.isDirectory,
                task: task,
                context: context,
                operation: operation,
                onProcessed: onProcessed,
                onDiscovered: onDiscovered
            )
        }

        if entry.isDirectory {
            _ = try runSmbWithReconnect(context: context) { core, connectionID in
                try core.smbCreateDirectory(
                    share: targetShare,
                    path: targetPath,
                    connectionID: connectionID,
                    operationID: task.operationID
                )
            }
            operation.markCreated(share: targetShare, path: targetPath, isDirectory: true)
            onProcessed()
            var copied = 1
            var skipped = 0
            let children = try runSmbWithReconnect(context: context) { core, connectionID in
                try core.smbListDirectory(
                    share: entry.share,
                    path: entry.remotePath,
                    connectionID: connectionID,
                    operationID: task.operationID
                )
            }
            if !children.isEmpty {
                onDiscovered(children.count)
            }
            for child in children {
                if task.isCancelled {
                    break
                }
                let childEntry = FileClipboardEntry(
                    name: child.name,
                    share: entry.share,
                    remotePath: child.path,
                    displayPath: Self.displayPathForRemote(share: entry.share, remotePath: child.path),
                    isDirectory: child.isDir
                )
                let childTarget = appendPathComponent(directory: targetPath, fileName: child.name)
                let result = try copyEntryRecursively(
                    childEntry,
                    targetShare: targetShare,
                    targetPath: childTarget,
                    task: task,
                    context: context,
                    operation: operation,
                    onProcessed: onProcessed,
                    onDiscovered: onDiscovered
                )
                copied += result.copied
                skipped += result.skipped
            }
            return (copied, skipped)
        }

        _ = try runSmbWithReconnect(context: context) { core, connectionID in
            try core.smbCopyFile(
                sourceShare: entry.share,
                sourcePath: entry.remotePath,
                targetShare: targetShare,
                targetPath: targetPath,
                replaceExisting: existingItem != nil,
                connectionID: connectionID,
                operationID: task.operationID
            )
        }
        operation.markCreated(share: targetShare, path: targetPath, isDirectory: false)
        onProcessed()
        return (1, 0)
    }

    func deleteRemotePathRecursively(
        share: String,
        path: String,
        isDirectory: Bool,
        task: OperationTask,
        context: RemoteWriteSessionContext,
        operation: RemoteOperationContext,
        onProcessed: () -> Void,
        onDiscovered: (Int) -> Void
    ) throws {
        if task.isCancelled {
            return
        }
        if isDirectory {
            let children = try runSmbWithReconnect(context: context) { core, connectionID in
                try core.smbListDirectory(
                    share: share,
                    path: path,
                    connectionID: connectionID,
                    operationID: task.operationID
                )
            }
            if !children.isEmpty {
                onDiscovered(children.count)
            }
            for child in children {
                try deleteRemotePathRecursively(
                    share: share,
                    path: child.path,
                    isDirectory: child.isDir,
                    task: task,
                    context: context,
                    operation: operation,
                    onProcessed: onProcessed,
                    onDiscovered: onDiscovered
                )
            }
        }
        _ = try runSmbWithReconnect(context: context) { core, connectionID in
            try core.smbDelete(
                share: share,
                path: path,
                isDir: isDirectory,
                connectionID: connectionID,
                operationID: task.operationID
            )
        }
        operation.markDeleted(share: share, path: path)
        onProcessed()
    }

    func resolveConflictIfNeeded(
        share: String,
        path: String,
        name: String,
        isDirectory: Bool,
        task: OperationTask,
        operation: RemoteOperationContext
    ) throws -> ConflictDecision {
        guard try operation.itemExists(share: share, path: path) else {
            return .replace
        }
        if let decision = conflictApplyAllDecision {
            return decision
        }
        return try requestConflictDecision(name: name, isDirectory: isDirectory, task: task)
    }

    func requestConflictDecision(
        name: String,
        isDirectory: Bool,
        task: OperationTask
    ) throws -> ConflictDecision {
        if task.isCancelled {
            return .skip
        }

        let semaphore = DispatchSemaphore(value: 0)
        var decision: ConflictDecision = .skip
        DispatchQueue.main.async { [weak self] in
            let alert = NSAlert()
            alert.messageText = "已存在同名项目"
            alert.informativeText = "目标位置已存在“\(name)”。请选择处理方式。"
            alert.alertStyle = .warning
            alert.addButton(withTitle: "替换")
            alert.addButton(withTitle: "跳过")

            let checkbox = NSButton(checkboxWithTitle: "对本次操作全部应用", target: nil, action: nil)
            checkbox.state = .off
            alert.accessoryView = checkbox

            let response = alert.runModal()
            decision = response == .alertFirstButtonReturn ? .replace : .skip
            if checkbox.state == .on {
                self?.conflictApplyAllDecision = decision
            }
            semaphore.signal()
        }
        semaphore.wait()
        return decision
    }

    func runSmbWithReconnect<T>(_ operation: (RynatCore, String) throws -> T) throws -> T {
        guard let session else {
            throw RynatCoreError.bridgeError("未连接到服务器", code: "reconnectable")
        }
        return try runSmbWithReconnect(
            context: RemoteWriteSessionContext(
                serverProfileID: session.server.id,
                connectionID: session.connectionID,
                generation: sessionGeneration
            ),
            operation
        )
    }

    func runSmbWithReconnect<T>(
        context: RemoteWriteSessionContext,
        _ operation: (RynatCore, String) throws -> T
    ) throws -> T {
        guard isCurrentSession(context) else {
            throw RynatCoreError.bridgeError("连接已断开，请重新登录", code: "credential")
        }
        return try smbGateway.run(context: context, operation: operation)
    }

    func performRemoteWrite(
        title: String,
        total: Int = 0,
        operation: @escaping (OperationTask, RemoteWriteSessionContext, @escaping (Int, Int) -> Void) throws -> String
    ) {
        guard let session else {
            setStatus("未连接")
            setActivityMessage("请先登录服务器")
            return
        }

        let request = RemoteWriteRequest(
            title: title,
            total: total,
            context: RemoteWriteSessionContext(
                serverProfileID: session.server.id,
                connectionID: session.connectionID,
                generation: sessionGeneration
            ),
            operation: operation
        )
        transferCoordinator.enqueue(request)
    }

    func isCurrentSession(_ context: RemoteWriteSessionContext) -> Bool {
        sessionGeneration == context.generation &&
            session?.connectionID == context.connectionID &&
            session?.server.id == context.serverProfileID
    }

    func showTask(_ task: OperationTask) {
        uploadProgress.isHidden = false
        cancelTaskButton.isHidden = false
        if task.total > 0 {
            uploadProgress.isIndeterminate = false
            uploadProgress.minValue = 0
            uploadProgress.maxValue = Double(task.total)
            uploadProgress.doubleValue = 0
        } else {
            uploadProgress.isIndeterminate = true
            uploadProgress.startAnimation(nil)
        }
        setStatus(task.total > 0 ? "\(task.title) 0/\(task.total) 项" : task.title)
    }

    func updateTask(_ task: OperationTask, completed: Int, total: Int) {
        task.completed = completed
        task.total = total
        if total > 0 {
            uploadProgress.isIndeterminate = false
            uploadProgress.maxValue = Double(total)
            uploadProgress.doubleValue = Double(completed)
            setStatus("\(task.title) \(completed)/\(total) 项")
        } else {
            setStatus(task.title)
        }
    }

    func finishTask(_ task: OperationTask, state: OperationTask.State) {
        task.state = state
        uploadProgress.stopAnimation(nil)
        uploadProgress.isHidden = true
        cancelTaskButton.isHidden = true
        switch state {
        case .completed:
            setStatus("已完成")
        case .cancelled:
            setStatus("已取消")
        case .failed:
            setStatus("操作失败")
        case .running:
            setStatus(task.title)
        }
    }

    func clearWriteQueueForLogout() {
        transferCoordinator.clearForLogout()
    }

    @objc
    func cancelActiveTask() {
        guard transferCoordinator.cancelActiveTask() else {
            cancelPreviewOperations()
            return
        }
        cancelPreviewOperations()
        setStatus("正在取消")
    }

    func cancelPreviewOperations() {
        previewCoordinator.cancelAll()
    }

    func handleDroppedFiles(_ urls: [URL]) {
        let server = currentServer()
        guard let location = session?.activeLocation() else {
            setActivityMessage("请先进入一个共享或共享内目录，再拖拽上传。")
            setStatus("请选择上传目标")
            return
        }
        let directory = location.remotePath

        let fileURLs = urls.filter { url in
            var isDirectory: ObjCBool = false
            let exists = FileManager.default.fileExists(atPath: url.path, isDirectory: &isDirectory)
            return exists && !isDirectory.boolValue
        }
        guard !fileURLs.isEmpty else {
            setActivityMessage("暂不支持拖拽上传文件夹，请拖入文件。")
            setStatus("未上传")
            return
        }
        setActivityMessage("正在上传 \(fileURLs.count) 个文件\n目标：\(server.host) / \(location.share)\(directory == "/" ? "" : directory)")

        performRemoteWrite(title: "正在上传", total: fileURLs.count) { task, context, progress in
            let operation = RemoteOperationContext(
                listDirectory: { share, path in
                    try self.runSmbWithReconnect(context: context) { core, connectionID in
                        try core.smbListDirectory(
                            share: share,
                            path: path,
                            connectionID: connectionID,
                            operationID: task.operationID
                        )
                    }
                },
                log: { [weak self] message in self?.appendLog(message) }
            )
            var uploaded = 0
            var skipped = 0
            for (index, url) in fileURLs.enumerated() {
                if task.isCancelled {
                    break
                }
                let remotePath = self.appendPathComponent(directory: directory, fileName: url.lastPathComponent)
                guard try self.resolveConflictIfNeeded(
                    share: location.share,
                    path: remotePath,
                    name: url.lastPathComponent,
                    isDirectory: false,
                    task: task,
                    operation: operation
                ) == .replace else {
                    skipped += 1
                    progress(index + 1, fileURLs.count)
                    continue
                }
                let existingItem = try operation.existingItem(share: location.share, path: remotePath)
                if existingItem?.isDirectory == true {
                    try self.deleteRemotePathRecursively(
                        share: location.share,
                        path: remotePath,
                        isDirectory: true,
                        task: task,
                        context: context,
                        operation: operation,
                        onProcessed: {},
                        onDiscovered: { _ in }
                    )
                }
                _ = try self.runSmbWithReconnect(context: context) { core, connectionID in
                    try core.smbUploadFile(
                        share: location.share,
                        localPath: url.path,
                        remotePath: remotePath,
                        replaceExisting: existingItem != nil,
                        connectionID: connectionID,
                        operationID: task.operationID
                    )
                }
                operation.markCreated(share: location.share, path: remotePath, isDirectory: false)
                uploaded += 1
                progress(index + 1, fileURLs.count)
            }
            return skipped > 0 ? "上传完成，跳过 \(skipped) 项" : "上传完成 \(uploaded) 项"
        }
    }
}
