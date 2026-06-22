import Foundation

final class TransferCoordinator {
    struct UIHooks {
        let setQueued: (Int) -> Void
        let started: (OperationTask) -> Void
        let progressed: (OperationTask, Int, Int) -> Void
        let completed: (OperationTask, OperationTask.State, String) -> Void
        let failed: (OperationTask, Error) -> Void
        let requestRefresh: (RemoteWriteSessionContext) -> Void
        let cancelOperation: (String) -> Void
        let log: (String) -> Void
    }

    private var activeTask: OperationTask?
    private var pendingRequests: [RemoteWriteRequest] = []
    private let hooks: UIHooks

    init(hooks: UIHooks) {
        self.hooks = hooks
    }

    var currentTask: OperationTask? {
        activeTask
    }

    func enqueue(_ request: RemoteWriteRequest) {
        if activeTask != nil {
            pendingRequests.append(request)
            hooks.setQueued(pendingRequests.count)
            return
        }
        start(request)
    }

    func clearForLogout() {
        pendingRequests.removeAll()
        if let task = activeTask {
            task.isCancelled = true
            hooks.cancelOperation(task.operationID)
            finish(task, state: .cancelled, message: "已取消")
        }
    }

    func cancelActiveTask() -> Bool {
        guard let task = activeTask else {
            return false
        }
        task.isCancelled = true
        hooks.cancelOperation(task.operationID)
        return true
    }

    private func startNextIfNeeded() {
        guard activeTask == nil, !pendingRequests.isEmpty else {
            return
        }
        start(pendingRequests.removeFirst())
    }

    private func start(_ request: RemoteWriteRequest) {
        let task = OperationTask(title: request.title, total: request.total)
        activeTask = task
        hooks.started(task)

        DispatchQueue.global(qos: .userInitiated).async { [weak self, weak task] in
            guard let self, let task else {
                return
            }
            do {
                let message = try request.operation(task, request.context) { completed, total in
                    DispatchQueue.main.async { [weak self, weak task] in
                        guard let self, let task, self.activeTask?.id == task.id else {
                            return
                        }
                        self.hooks.progressed(task, completed, total)
                    }
                }
                DispatchQueue.main.async { [weak self, weak task] in
                    guard let self, let task, self.activeTask?.id == task.id else {
                        return
                    }
                    let state: OperationTask.State = task.isCancelled ? .cancelled : .completed
                    self.finish(task, state: state, message: task.isCancelled ? "已取消" : message)
                    if state == .completed {
                        self.hooks.requestRefresh(request.context)
                    }
                    self.startNextIfNeeded()
                }
            } catch {
                DispatchQueue.main.async { [weak self, weak task] in
                    guard let self, let task, self.activeTask?.id == task.id else {
                        return
                    }
                    self.activeTask = nil
                    self.hooks.failed(task, error)
                    if !task.isCancelled {
                        self.hooks.requestRefresh(request.context)
                    }
                    self.startNextIfNeeded()
                }
            }
        }
    }

    private func finish(_ task: OperationTask, state: OperationTask.State, message: String) {
        task.state = state
        if activeTask?.id == task.id {
            activeTask = nil
        }
        hooks.completed(task, state, message)
    }
}
