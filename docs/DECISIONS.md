# Decisions

| Decision | Reason |
| --- | --- |
| Benchline targets one makerspace. | The demo can show the complete disruption and recovery workflow with a compact, reproducible schedule. |
| .NET 10 LTS and C# 12. | We chose an LTS runtime and kept the course language baseline. [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) |
| Fixed one-hour sessions with a UTC timeline. | The slot is the indivisible capacity unit. UTC instants make overlap and deadline comparisons independent of the display date. |
| Fixed offset for Astana display. | Kazakhstan's UTC+5 policy fits the demo's current dates. A fixed +05:00 conversion gives identical display values in WebAssembly and Docker without a host time-zone database. Calendar grouping uses Astana dates. [Government notice](https://www.gov.kz/org/mfa-jakarta/press/news/details/715495?lang=en) |
| Maximum matching through augmenting paths. | Simple first-fit allocation can waste a slot. A reassignment path can move a flexible booking to free a slot for a more constrained booking. |
| Revision-checked recovery. | Publishing a plan can affect multiple reservations. A changed revision triggers a fresh preview before any reservations move. |
| Same-origin hosting with secure cookies and antiforgery protection. | One deployment serves the client, API and hub. The browser can authenticate HTTP and SignalR without persisting a bearer token in script-accessible storage. |
| PWA client. | We chose a responsive browser build for installation and offline shell support on the demo platforms. |
| Trimmed interpreter build. | We measured transfer and main-thread work for the published WebAssembly build. The [cold sign-in measurements](QA_REPORT.md#lighthouse) cover that build. |
