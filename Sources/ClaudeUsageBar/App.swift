import SwiftUI
import UsageCore

@main
struct ClaudeUsageBarApp: App {
    @StateObject private var model = UsageModel()

    var body: some Scene {
        MenuBarExtra {
            VStack(alignment: .leading, spacing: 12) {
                ForEach(model.usage?.rows ?? [], id: \.title) { row in
                    VStack(alignment: .leading, spacing: 4) {
                        HStack {
                            Text(row.title).font(.headline)
                            Spacer()
                            Text("\(Int(row.percent.rounded()))%").monospacedDigit()
                        }
                        ProgressView(value: min(row.percent, 100), total: 100)
                            .tint(row.percent >= 90 ? .red : row.percent >= 70 ? .orange : .accentColor)
                        if let resetsAt = row.resetsAt {
                            Text("\(resetDescription(resetsAt)) · \(resetsAt.formatted(.dateTime.weekday().day().month().hour().minute()))")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                    }
                }
                if let error = model.error { Text(error).foregroundStyle(.red) }
                Divider()
                HStack {
                    if let updated = model.updatedAt {
                        Text("Updated \(updated.formatted(date: .omitted, time: .shortened))")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button("Refresh") { Task { await model.refresh() } }.keyboardShortcut("r")
                    Button("Quit") { NSApplication.shared.terminate(nil) }.keyboardShortcut("q")
                }
            }
            .padding()
            .frame(width: 280)
        } label: {
            Text("✳︎ " + (model.usage?.menuBarTitle ?? (model.error == nil ? "…" : "⚠︎")))
        }
        .menuBarExtraStyle(.window)
    }
}

@MainActor
final class UsageModel: ObservableObject {
    @Published var usage: Usage?
    @Published var error: String?
    @Published var updatedAt: Date?
    private var timer: Timer?

    init() {
        Task { await refresh() }
        timer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in
            Task { await self?.refresh() }
        }
    }

    func refresh() async {
        do {
            usage = try await fetchUsage()
            error = nil
            updatedAt = Date()
        } catch {
            self.error = error.localizedDescription
        }
    }
}

struct UsageError: LocalizedError {
    let errorDescription: String?
    init(_ message: String) { errorDescription = message }
}

func fetchUsage() async throws -> Usage {
    guard let token = readToken() else { throw UsageError("Not logged in: run `claude` and /login") }
    var request = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!)
    request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
    request.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
    let (data, response) = try await URLSession.shared.data(for: request)
    let status = (response as? HTTPURLResponse)?.statusCode ?? 0
    // shortcut: no token refresh of our own; Claude Code refreshes it on its next run. Upgrade if expired tokens become common.
    if status == 401 { throw UsageError("Token expired: open Claude Code once to refresh it") }
    guard status == 200 else { throw UsageError("API returned HTTP \(status)") }
    return try Usage.decode(data)
}

/// Claude Code keeps its OAuth credentials in the login Keychain on macOS. Reading through
/// /usr/bin/security reuses the item's existing ACL, so there is no extra Keychain prompt.
func readToken() -> String? {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/usr/bin/security")
    process.arguments = ["find-generic-password", "-s", "Claude Code-credentials", "-w"]
    let pipe = Pipe()
    process.standardOutput = pipe
    process.standardError = FileHandle.nullDevice
    if (try? process.run()) != nil {
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        if process.terminationStatus == 0, let token = accessToken(fromCredentials: data) { return token }
    }
    let file = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".claude/.credentials.json")
    return (try? Data(contentsOf: file)).flatMap(accessToken(fromCredentials:))
}
