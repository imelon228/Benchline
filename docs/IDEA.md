# Benchline project rationale

Benchline helps a university makerspace recover its schedule after equipment becomes unavailable. Staff see which sessions are affected, preview replacements and publish a plan. Students see changes on the same board they use to book a machine.

| Candidate | Strengths | Trade-offs |
| --- | --- | --- |
| Makerspace outage recovery | A machine failure creates a visible scheduling conflict that brings together allocation, live updates and role boundaries. | Fixed sessions bound the scheduling problem. Replacement equipment must have the same capability. |
| Campus repair cafe dispatch | Volunteers match repair jobs with available specialists and update visitors in real time. | Repair durations vary, and the workflow depends on representative queue data. |
| Equipment lending exchange | Clubs reclaim cancelled kit loans through a shared priority queue. | Pick-up and return policies add logistics outside the allocation problem. |

We chose makerspace outage recovery because a single disruption exposes the scheduling problem and shows how reservations change for each role.

## Aim and scope

Benchline reduces the work of finding replacement sessions and shows members the result. It covers one workshop with fixed one-hour sessions and uses an installable browser client.

- Members reserve equipment and cancel their own reservations.
- Technicians flag a station out of service and return it to the schedule.
- Coordinators preview a recovery plan, publish it against a specific revision and export the audit.
- The recovery planner uses deterministic maximum bipartite matching. A replacement must start at or after the original slot and finish by the member's deadline.

## Demonstrated workflow

We reserved equipment, reported a laser cutter outage and published replacements for two members with different deadlines. Both member boards showed the new station and the audit CSV recorded the recovery.

## Related products

| Product | Documented workflow | Decision |
| --- | --- | --- |
| Skedda | Space-specific booking conditions and availability windows. [Booking conditions](https://support.skedda.com/en/articles/112700-booking-conditions) | We validated rules at booking time and displayed rejected requests on the same screen. |
| Booked | Equipment is a schedulable resource. Approvals can be scoped to resources or groups. [Resource documentation](https://www.bookedscheduler.com/help/administration/resources/) | We separated equipment and reservations and enforced role permissions on the server. |
| Benchline | Staff preview and publish a batch recovery plan for displaced bookings. | We implemented an outage workflow with compatible alternatives, finish deadlines and an audit trail. |
