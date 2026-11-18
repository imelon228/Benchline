# Implementation backlog

| Phase | Deliverable | Verification |
| --- | --- | --- |
| 0 | Topic, scope, architecture and grading plan | Compare the plan with the course rubric. |
| 1 | Solution, conventions, CI and container | Restore and build on the installed SDK. Validate Compose configuration. |
| 2 | Immutable models, booking rules, recovery planner and use cases | Unit-test overlap, deadlines, deterministic matching, stale plans and authorization boundaries. |
| 3 | EF storage, identity, API, audit and SignalR | Test real HTTP and database writes, permission failures and concurrent schedule changes. |
| 4 | Board, booking, staff controls, reusable UI and PWA | Verify reducer transitions and exercise the flow in a browser. |
| 5 | Offline and reconnect handling, export and accessibility | Check keyboard, mobile layout and failure paths. |
| 6 | Coverage, browser tests and QA | Record actual coverage, browser results and performance measurements. |
| 7 | User and developer guides, presentation and defense | Rehearse the seeded scenario. Both teammates should understand all layers. |
| 8 | Final audit and submission | Exercise a clean clone and inspect the ZIP inventory. |

Both teammates should contribute code. One can own the matching rules and deadline test cases. The other can own a reusable Blazor component and its keyboard and rendering tests. Review each other's changes and rehearse the booking-to-recovery flow together.
