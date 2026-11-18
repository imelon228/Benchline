# Benchline QA report

## Release verification

From a clean checkout, Docker Compose built and started the published Release app. The .NET suites passed 50 tests. Playwright passed 11 cases against a fresh Compose volume and one extra case against a restarted old volume. The old-volume case is opt-in and ran separately. Before submission, we rebuilt the Compose image from a clean checkout, reran all 50 .NET tests and passed the three Chromium smoke cases for booking, offline reload and four-account recovery.

| Suite | Passed | Checks |
| --- | ---: | --- |
| Unit | 18 | Schedule rules, deterministic matching, audit bounds and Astana offset and date rollover |
| Integration project | 11 | Permissions, concurrency, both CSV timestamps, paging, migrations, throttling and in-place demo upgrades |
| Components | 21 | Timetable states, date grouping, accessible Astana labels, disabled reasons, dialogs and state transitions |
| Browser | 12 | Demo display names, Astana timetable, campus recovery destination, audit CSV, keyboard booking and cancellation, offline shell, theme checks, reflow and old-volume booking |

```sh
docker compose up --build
dotnet test Benchline.slnx -c Release --nologo --collect "XPlat Code Coverage" --results-directory TestResults
cd tests/browser
npm ci --omit=optional
npx playwright test
```

The [developer notes](DEVELOPER_NOTES.md) describe the old-clock restart and the separate browser case.

Coverage comes from the relevant Release Cobertura report for each project.

| Project | Suite | Lines | Branches |
| --- | --- | ---: | ---: |
| Benchline.Domain | Unit | 96.15% | 90.90% |
| Benchline.Application | Unit | 95.83% | 92.85% |
| Benchline.Infrastructure | Integration | 90.40% | 87.50% |
| Benchline.Api | Integration | 57.14% | 32.68% |
| Benchline.Components | Components | 90.12% | 84.17% |
| Benchline.Client | Components | 28.41% | 25.63% |

## Demo renewal

We restarted the same SQLite file with an injected clock at one hour, twelve hours and three days after seeding. The short restart kept the original slots and revision. The later restarts renewed the slots, kept the station IDs, left an opening for every online station and yielded two eligible backup assignments within their deadlines.

The Compose browser check seeded a volume with a January 2024 clock and restarted it with the normal clock. A member then reserved and cancelled a new Desktop CNC session. The fresh-volume browser flow signed in all four accounts, reported a laser outage, checked the stale preview, published recovery, verified both member boards and downloaded the audit CSV.

The current localization run started the previous published release on a SQLite volume, then installed the localized release on the same volume at the same fixed clock. It kept the original session cookie, station IDs, session IDs, booking IDs and revision. After switching to the normal clock, the same volume renewed and the member reserved and cancelled a session. The .NET integration test also checked that Production leaves legacy labels in place and Demo preserves password hashes during the upgrade.

## Desktop, tablet and phone review

Chromium checked both themes at widths of 360, 820 and 1440px. Firefox covered tablet and WebKit covered phone reflow and filtering. Axe found zero violations in both themes on sign-in, the member board, booking and recovery dialogs, the audit log and the component library. Chromium checked all six views. WebKit repeated the sign-in, member, booking and component checks.

We reviewed 36 screenshots spanning those six views at 1440, 820 and 390px in both themes. The localized screenshots verify the new station labels, booking times and recovery destination across sizes. The review found a long deadline option on phones, so the booking dialog uses short offsets after the displayed finish time. Screenshot capture found no unexpected console errors. A signed-out session check returns the expected HTTP 401.

| Contrast pair | Light | Dark |
| --- | ---: | ---: |
| Ink and paper, including primary actions and focus | 19.80:1 | 18.16:1 |
| Muted and paper, including disabled text and input borders | 7.00:1 | 7.85:1 |
| Ink and hatch base | 17.68:1 | 14.24:1 |
| Muted and hatch base | 6.26:1 | 6.15:1 |
| Decorative hairline and paper | 1.41:1 | 1.74:1 |

The ratios use sRGB relative luminance. Tested text pairs exceed 4.5:1 and functional control outlines exceed 3:1. Labels, fills and patterns identify session states.

## Lighthouse

Lighthouse 13.6.0 measured cold sign-in on the local published Release builds using the desktop and default mobile presets. Each cell is the median of three runs for that metric.

| Profile and build | Score | LCP | TBT | Transfer |
| --- | ---: | ---: | ---: | ---: |
| Desktop, before localization | 62 | 0.33 s | 1,726 ms | 2,690,112 bytes |
| Desktop, localized timetable | 80 | 0.43 s | 401 ms | 2,694,334 bytes |
| Mobile, before localization | 62 | 1.39 s | 7,161 ms | 2,690,112 bytes |
| Mobile, localized timetable | 66 | 1.00 s | 2,276 ms | 2,694,334 bytes |

The runs measure sign-in over loopback. Main-thread blocking varied across individual runs. The browser still loads and initializes WebAssembly before sign-in becomes interactive.

## Findings and fixes

| Finding | Resolution |
| --- | --- |
| A persisted demo volume could contain only expired sessions after a long gap. | Demo startup checks scenario readiness and renews the four stations' schedules in a transaction. The audit event records the renewal. |
| The timetable included ended columns and disabled Reserve without an explanation. | The grid starts at the current hour in Astana, omits ended slots, leaves gaps blank and pairs disabled actions with visible reasons. |
| A recovery preview could reference a slot removed by a demo renewal. | The stale preview remains dismissible and identifies the missing replacement. |
| Long audit IDs and the showcase toolbar needed more room at phone width. | Audit details wrap, and a gap separates the showcase controls from the timetable. |
| The WebKit contrast scan could start during the dialog's fade. | The contrast scan runs after the dialog animation settles. |
| A usable volume still had the previous display names and places. | Demo startup applies the display catalog in place. |
| A user could read the board date or deadline in the wrong local day. | Time labels and column groups use the same Astana offset, including rollover at 19:00 UTC. |
| The CSV only included the UTC timestamp. | The CSV includes the same instant as an ISO 8601 timestamp with +05:00. |

## Remaining validation

Physical iOS and Android installations, native macOS execution, manual screen-reader interaction and formal WCAG 2.1 AA sign-off remain unverified. The local Lighthouse runs use simulated navigation profiles. Production account and station setup still require deployment integration.
