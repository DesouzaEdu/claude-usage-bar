import Foundation
import XCTest
@testable import UsageCore

final class UsageTests: XCTestCase {
    func testPrefersLimitsArray() throws {
        let json = """
        {
          "five_hour": null,
          "seven_day": {"utilization": 3.0, "resets_at": "2026-11-28T06:00:00.366758+00:00"},
          "extra_usage": {"is_enabled": false},
          "limits": [
            {"kind": "session", "group": "session", "percent": 23, "resets_at": "2026-11-21T15:00:00+00:00", "scope": null, "is_active": true},
            {"kind": "weekly_all", "group": "weekly", "percent": 3, "resets_at": "2026-11-28T06:00:00.366758+00:00", "scope": null, "is_active": true},
            {"kind": "weekly_scoped", "group": "weekly", "percent": 41, "resets_at": "2026-11-28T06:00:00+00:00",
             "scope": {"model": {"id": null, "display_name": "Fable"}, "surface": null}, "is_active": false}
          ]
        }
        """
        let usage = try Usage.decode(Data(json.utf8))
        XCTAssertEqual(usage.rows.map(\.title), ["Session", "Weekly", "Weekly · Fable"])
        XCTAssertEqual(usage.rows[1].resetsAt, parseISODate("2026-11-28T06:00:00Z"))
        XCTAssertEqual(usage.menuBarTitle, "23% · 41%")
    }

    func testFallsBackToLegacyWindows() throws {
        let json = """
        {"five_hour": {"utilization": 23.4, "resets_at": "2025-10-17T19:00:00.123456+00:00"},
         "seven_day": {"utilization": 41.0, "resets_at": "2025-10-20T08:00:00+00:00"},
         "seven_day_opus": null}
        """
        let usage = try Usage.decode(Data(json.utf8))
        XCTAssertEqual(usage.rows.map(\.title), ["Session 5h", "Weekly"])
        XCTAssertEqual(usage.menuBarTitle, "23% · 41%")
    }

    func testEmptyResponse() throws {
        XCTAssertEqual(try Usage.decode(Data("{}".utf8)).menuBarTitle, "–")
    }

    func testReadsTokenFromCredentials() {
        let creds = #"{"claudeAiOauth":{"accessToken":"sk-ant-oat01-x","refreshToken":"r","expiresAt":1}}"#
        XCTAssertEqual(accessToken(fromCredentials: Data(creds.utf8)), "sk-ant-oat01-x")
        XCTAssertNil(accessToken(fromCredentials: Data("{}".utf8)))
        XCTAssertNil(accessToken(fromCredentials: Data("not json".utf8)))
    }

    func testResetCountdown() {
        let now = Date(timeIntervalSince1970: 0)
        XCTAssertEqual(resetDescription(now.addingTimeInterval(134 * 60), now: now), "resets in 2h 14m")
        XCTAssertEqual(resetDescription(now.addingTimeInterval(3 * 86400 + 5 * 3600), now: now), "resets in 3d 5h")
        XCTAssertEqual(resetDescription(now.addingTimeInterval(-60), now: now), "resets in 0m")
        XCTAssertEqual(resetDescription(nil, now: now), "")
    }
}
