# Benchline user guide

On the Demo sign-in screen, select a username to fill it in. Password entry stays separate.

## Reserve a session

1. Sign in with a member account and open **Workshop**.
2. Filter the timetable by equipment type or find the station by name and campus space. Station rows show availability. All times on the board are in Astana time (UTC+5).
3. Select an **Open** session to prefill its time, or select **Reserve** on the station to start with its first open session. Choose the latest finish you can accept if staff move the booking.
4. Confirm the reservation. Open **My bookings** to review or cancel it before it starts.
5. If staff report an outage, look for **Awaiting replacement**. Once the coordinator publishes a replacement, the booking shows the new station and time.

## Report a station outage

1. Sign in as a technician and find the affected station.
2. Select **Report outage**. The station stops accepting new bookings, and future reservations move into the recovery queue.
3. Select **Restore station** when the machine is usable again. A coordinator can still review reservations displaced during the outage.

## Recover the schedule

1. Sign in as a coordinator and identify the station in **Recovery queue**.
2. Select **Preview plan**. Review the replacement station, campus space and start time, the number of matches and any still waiting.
3. Select **Publish recovery**. If the board has changed, reopen the preview to review the current slots.
4. Open **Audit log** to confirm that the recovery was recorded. **Export CSV** downloads the most recent 100 entries.

In the demo, Aidos Serikov and Aruzhan Nurgaliyeva have reservations on Laser Cutter 01 in Coworking Zone. Nurlan Tolegenov reports the outage, and Dana Kassymova previews the replacements at Laser Cutter 02 in Open Space. The audit log shows local times. The CSV keeps **Recorded UTC** and appends **Recorded Astana time** with a `+05:00` offset.

## Connection and accessibility

The board header shows the Astana date, connection state and schedule revision. **Refresh board** requests the current revision. During a browser-reported network outage, the open board stays readable and writes are disabled. An offline reload opens the installed shell. Reconnect to restore the current session and schedule.

Use **Dark mode** or **Light mode** to change the monochrome palette. Your preference is saved in the browser. Tab through controls. While an open session is focused, use arrow keys to move between available cells in its row or column. Press Enter to open the booking dialog and Escape to close it. An outlined **Open** cell can be selected, a solid **Yours** cell is already reserved by you, a hatched **Taken** cell is booked, and a cross-hatched **Offline** cell belongs to a station out of service. Blank cells mean the station has no session in that hour. The phone layout stacks stations and wraps their session cells. On supported browsers, install Benchline from the browser's install menu while viewing the published app.

The grid starts at the current hour in Astana. If no sessions are open, refresh the board or ask the workshop team about the next schedule. A dashed button shows its reason below it, such as **No open sessions** or **Station is out of service**.
