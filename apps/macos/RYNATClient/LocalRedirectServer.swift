import Foundation

final class LocalRedirectServer {
    private let port: UInt16
    private let core = RynatCore()
    private var socketFD: Int32 = -1
    private var isRunning = false
    private var onOpenLink: ((String) -> Void)?

    init(port: UInt16 = 19527) {
        self.port = port
    }

    func start(onLog: @escaping (String) -> Void, onOpenLink: @escaping (String) -> Void) {
        guard !isRunning else { return }
        isRunning = true
        self.onOpenLink = onOpenLink

        DispatchQueue.global(qos: .utility).async { [weak self] in
            self?.run(onLog: onLog)
        }
    }

    func stop() {
        isRunning = false
        if socketFD >= 0 {
            shutdown(socketFD, SHUT_RDWR)
            close(socketFD)
            socketFD = -1
        }
    }

    private func run(onLog: @escaping (String) -> Void) {
        socketFD = socket(AF_INET, SOCK_STREAM, 0)
        guard socketFD >= 0 else {
            onLog("LocalRedirectServer socket failed")
            return
        }

        var yes: Int32 = 1
        setsockopt(socketFD, SOL_SOCKET, SO_REUSEADDR, &yes, socklen_t(MemoryLayout<Int32>.size))

        var addr = sockaddr_in()
        addr.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = port.bigEndian
        addr.sin_addr = in_addr(s_addr: inet_addr("127.0.0.1"))

        let bindResult = withUnsafePointer(to: &addr) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                bind(socketFD, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }

        guard bindResult == 0 else {
            onLog("LocalRedirectServer bind 127.0.0.1:\(port) failed. Another RYNAT helper may be running.")
            close(socketFD)
            socketFD = -1
            return
        }

        guard listen(socketFD, 16) == 0 else {
            onLog("LocalRedirectServer listen failed")
            close(socketFD)
            socketFD = -1
            return
        }

        onLog("LocalRedirectServer listening on http://127.0.0.1:\(port)")

        while isRunning {
            let client = accept(socketFD, nil, nil)
            guard client >= 0 else {
                if isRunning {
                    continue
                }
                break
            }
            configureClientSocket(client)
            handle(client: client, onLog: onLog)
            close(client)
        }
    }

    private func configureClientSocket(_ client: Int32) {
        var timeout = timeval(tv_sec: 2, tv_usec: 0)
        setsockopt(client, SOL_SOCKET, SO_RCVTIMEO, &timeout, socklen_t(MemoryLayout<timeval>.size))
        setsockopt(client, SOL_SOCKET, SO_SNDTIMEO, &timeout, socklen_t(MemoryLayout<timeval>.size))
    }

    private func handle(client: Int32, onLog: (String) -> Void) {
        guard let request = readHTTPRequest(client: client) else {
            return
        }
        guard let firstLine = request.split(separator: "\r\n").first else { return }
        let parts = firstLine.split(separator: " ")
        guard parts.count >= 2 else { return }

        let rawPath = String(parts[1])
        guard rawPath.hasPrefix("/s") else {
            sendResponse(client: client, status: "404 Not Found", headers: ["Content-Type": "text/plain; charset=utf-8"], body: "Not Found")
            return
        }

        let query = rawPath.split(separator: "?", maxSplits: 1).dropFirst().first.map(String.init) ?? ""
        let target = "rynat://s" + (query.isEmpty ? "" : "?\(query)")
        onLog("HTTP bridge page -> \(target)")
        DispatchQueue.main.async { [weak self] in
            self?.onOpenLink?(target)
        }

        sendResponse(
            client: client,
            status: "200 OK",
            headers: [
                "Content-Type": "text/html; charset=utf-8",
                "Cache-Control": "no-store",
            ],
            body: bridgeHTML(target: target, onLog: onLog)
        )
    }

    private func readHTTPRequest(client: Int32) -> String? {
        var request = Data()
        var buffer = [UInt8](repeating: 0, count: 4096)
        while request.count < 64 * 1024 {
            let count = recv(client, &buffer, buffer.count, 0)
            guard count > 0 else {
                break
            }
            request.append(buffer, count: Int(count))
            if request.range(of: Data("\r\n\r\n".utf8)) != nil {
                break
            }
        }
        guard !request.isEmpty else {
            return nil
        }
        return String(decoding: request, as: UTF8.self)
    }

    private func sendResponse(client: Int32, status: String, headers: [String: String], body: String) {
        var lines = ["HTTP/1.1 \(status)"]
        var allHeaders = headers
        allHeaders["Content-Length"] = String(body.utf8.count)
        allHeaders["Connection"] = "close"
        for (key, value) in allHeaders {
            lines.append("\(key): \(value)")
        }
        let response = lines.joined(separator: "\r\n") + "\r\n\r\n" + body
        let data = Data(response.utf8)
        data.withUnsafeBytes { rawBuffer in
            guard let pointer = rawBuffer.baseAddress else {
                return
            }
            _ = send(client, pointer, data.count, 0)
        }
    }

    private func bridgeHTML(target: String, onLog: (String) -> Void) -> String {
        do {
            return try core.redirectPage(targetURL: target)
        } catch {
            onLog("Redirect page generation failed: \(error.localizedDescription)")
            return """
            <!doctype html>
            <html><head><meta charset="utf-8"><title>RYNAT 共享网盘</title></head>
            <body><a href="\(escapeHTML(target))">打开 RYNAT 共享网盘</a></body></html>
            """
        }
    }

    private func escapeHTML(_ value: String) -> String {
        value
            .replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "\"", with: "&quot;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
    }
}
