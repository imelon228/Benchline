# HTTP and real-time API

The OpenAPI document is available at `/openapi/v1.json`. The host uses JSON request bodies and returns ProblemDetails for rejected requests. The browser sends its session cookie automatically for same-origin requests.

| Method and route | Role | Purpose |
| --- | --- | --- |
| GET /api/token | Any | Acquire an antiforgery cookie and paired header token |
| POST /api/session | Any | Sign in with a username and password |
| GET /api/session | Signed in | Read the current name and role |
| POST /api/sign-out | Signed in | End the current session |
| GET /api/board | Signed in | Read the revision, station and slot data, and own bookings |
| POST /api/bookings | Member | Reserve an available slot with a member deadline |
| POST /api/bookings/{id}/cancel | Member | Cancel an owned booking |
| PUT /api/stations/{id} | Technician or coordinator | Change station availability |
| GET /api/recovery/{id} | Coordinator | Preview a plan and capture its revision |
| POST /api/recovery/{id} | Coordinator | Publish the exact preview |
| GET /api/audit?before=10&limit=25 | Coordinator | Page backward through audit entries |
| GET /api/audit.csv | Coordinator | Download up to 100 recent audit entries with UTC and Astana time |

Each mutation requires a fresh `X-Workshop-Token` value from GET /api/token. The client preserves cookies across requests and fetches a new token after sign-in.

The CSV retains **Recorded UTC** and appends **Recorded Astana time** in ISO 8601 with the +05:00 offset. JSON responses and request timestamps use UTC instants.

## Reservation example

```json
{
  "revision": 4,
  "bookingId": "557283da-84ce-4d55-92b4-bf33ee09d3db",
  "slotId": "0aae33b1-a0ef-4b9e-b047-372e92c125cd",
  "deadline": "2026-11-15T12:00:00Z"
}
```

A reservation request uses the slot identifier and current revision from the board. Success returns the committed revision. A response with status 409 and code `stale-revision` means the board changed before the write. The client refreshes the board after a conflict.

## Recovery example

The response from `GET /api/recovery/{id}` includes `revision`, `moves`, and an `unmatched` count. Publishing sends the reviewed moves and their revision back to the same route. The server recalculates the plan so that changes caused by elapsed time cannot silently alter an accepted preview.

The authenticated `/live` hub broadcasts `ScheduleChanged` with a revision number after a committed write. The browser fetches the board after a notification and after reconnecting. The hub carries revision numbers without booking or account data.
