# Development phases

The phases summarize the work we completed. The [QA report](QA_REPORT.md) records the verification results.

| Phase | Completed work | Verification |
| --- | --- | --- |
| 0 | We defined the makerspace scope, architecture and course criteria. | We documented the scope and mapped the implementation to the checklist. |
| 1 | We set up the solution, build conventions, CI and container. | We restored and tested the solution and built the Compose release from a clean checkout. |
| 2 | We implemented immutable models, booking rules, recovery matching and use cases. | We ran unit tests for overlap, deadlines, deterministic matching, stale plans and authorization boundaries. |
| 3 | We implemented EF storage, identity, HTTP routes, audit and SignalR. | We ran integration tests for permissions, concurrent writes, migrations and CSV export. Browser sessions received committed updates. |
| 4 | We implemented the board, booking and staff views, reusable components and the PWA shell. | We tested reducer transitions, component callbacks and the published workflow in the browser. |
| 5 | We implemented offline and reconnect handling, export and accessible schedule controls. | We checked keyboard booking, browser reflow, both themes, offline reload and recovery after reconnecting. |
| 6 | We recorded coverage, browser results and cold startup measurements. | We ran the .NET and browser suites and measured published sign-in with the desktop and mobile presets. |
| 7 | We documented the user and developer workflows and prepared the defense slides and mockups. | We verified the seeded booking-to-recovery flow and compared the mockups and slide screenshot with the published app. |
| 8 | We packaged and audited the submission archive. | We built and tested from a clean checkout and checked the ZIP inventory against the committed tree. |
