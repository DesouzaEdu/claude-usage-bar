# Claude Usage Bar

[![CI](https://github.com/LeoFalco/claude-usage-bar/actions/workflows/ci.yml/badge.svg)](https://github.com/LeoFalco/claude-usage-bar/actions/workflows/ci.yml)

A tiny macOS menu bar app that shows your Claude plan usage, the same numbers as `/usage` in Claude Code.

```
✳︎ 23% · 41%            ← highest session % · highest weekly %
┌──────────────────────────────────────┐
│ Session                          23% │
│ ████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
│ resets in 2h 14m · Sat 21 Nov, 15:00 │
│ Weekly                           41% │
│ ███████████████░░░░░░░░░░░░░░░░░░░░░ │
│ resets in 6d 16h · Sat 28 Nov, 03:00 │
│ Updated 10:19        [Refresh] [Quit]│
└──────────────────────────────────────┘
```

Each bar shows the countdown and the exact reset date and time in your locale. Bars turn orange at 70% and red at 90%. It refreshes every 5 minutes, or right away with Refresh (⌘R).

## Install

1. Download `ClaudeUsageBar.zip` from the [latest release](https://github.com/LeoFalco/claude-usage-bar/releases/latest) and unzip it.
2. Move `ClaudeUsageBar.app` to `/Applications`.
3. The app is not notarized, so clear the quarantine flag once:
   ```bash
   xattr -dr com.apple.quarantine /Applications/ClaudeUsageBar.app
   ```
4. Open it. To start it at login: System Settings → General → Login Items → add it.

Requires macOS 13+ and [Claude Code](https://docs.claude.com/en/docs/claude-code) logged in with a Claude subscription (`claude` then `/login`).

## How it works

Claude Code stores its OAuth credentials in your login Keychain (item `Claude Code-credentials`). The app reads the access token with `/usr/bin/security` and calls the same endpoint `/usage` uses:

```
GET https://api.anthropic.com/api/oauth/usage
Authorization: Bearer <token>
anthropic-beta: oauth-2025-04-20
```

The token never leaves your machine except in that request to Anthropic. The app does not refresh the token itself: if it shows "Token expired", run Claude Code once.

This endpoint is undocumented and may change; the app reads the `limits` list and falls back to the older `five_hour` / `seven_day` fields.

## Build from source

```bash
swift test                 # needs Xcode (XCTest)
scripts/bundle.sh          # builds dist/ClaudeUsageBar.app for this Mac
scripts/bundle.sh --arch arm64 --arch x86_64   # universal build (needs Xcode)
open dist/ClaudeUsageBar.app
```

## Releases

CI runs tests and builds the app on every push to main and on every pull request. Pushing a tag `vX.Y.Z` builds a universal app and publishes it as a GitHub release.

## License

MIT. Not affiliated with Anthropic.
