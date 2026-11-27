# garmin/: Connect IQ app (Venu Sq 2 Music)

A watch app that finds the Windows BLE server, connects, and sends PIN-signed slide commands.

## First launch: the PIN

The first time the app opens it shows a number pad. Type the PIN shown in the Windows app (6 to 12 digits) and tap **OK** (or press the button). The watch remembers it. If the PC rejects the PIN, the top strip says **Check PIN** with **Set PIN**; tap it to type the PIN again.

## Controls

| Control | Action |
|---------|--------|
| **NEXT** (right half) or the watch's **physical button** | Next slide |
| **PREV** (left half) | Previous slide |
| **START** | Start slideshow (F5) and start the talk timer |
| **BLACK** | Black screen toggle (B) |
| **END** | End slideshow (Esc) and clear the talk timer |
| Status strip, connected (shows **Timer**) | Start or stop the talk timer |
| Status strip, disconnected (shows **Retry**) | Search for the PC again |
| Status strip, "Check PIN" / "No PIN set" (shows **Set PIN**) | Tap to open the number pad |
| Swipe right, twice within 2.5 s | Exit the app (a single swipe only shows a hint, so a stray swipe can't drop the connection) |

The pressed button flashes white and the watch buzzes lightly; one long buzz means you aren't connected, and a double buzz means a live connection just dropped.

## Build

The launcher icon (`resources/drawables/launcher_icon.png`) is committed; `tools/make_icon.py` regenerates it if you want to start over.

### On your computer

1. Install the [Connect IQ SDK](https://developer.garmin.com/connect-iq/sdk/) and the **Monkey C** extension for VS Code.
2. In VS Code: **Monkey C: Build for Device**, pick `venusq2m`, and save `PptRemote.prg`.
3. Plug the watch in over USB and copy the `.prg` into `GARMIN/Apps/` on the watch.
4. Open **PPT Remote** from the watch's app list.

The simulator can't talk to a real BLE peripheral, so test on the watch itself. Set the watch's display timeout to something generous so the screen stays on during a talk.

### In GitHub Actions

`.github/workflows/garmin.yml` builds a dev `.prg` on every push that touches `garmin/`. It needs your Garmin login stored as repo secrets (Garmin doesn't allow anonymous SDK downloads); without them it skips itself and explains why in the run summary. Setup steps are in the root [README](../README.md#github-actions).

## How the text stays inside the screen

All text goes through `TextFit` (`source/Protocol.mc`), which measures it with `dc.getTextWidthInPixels()` and picks the largest font that fits, then truncates with "..." as a last resort. The status text gets whatever room the right-hand text leaves, and the PIN pad's labels shrink to fit their keys.

## Notes

- Only `venusq2m` is targeted. Garmin's BLE API docs list the Venu Sq 2 **Music** as supported, and the plain Venu Sq 2 is not in that list.
- The manifest `id` is a placeholder UUID; generate your own if you publish to the Connect IQ store.
- PIN signing uses `Toybox.Cryptography` (HMAC-SHA256, available since API 3.0.0). The PIN and a counter live in `Application.Storage`.
- Only one BLE operation can be in flight, so quick taps are queued (up to 4). A write that never completes is abandoned after 4 seconds.
- Connect IQ allows only 3 `Timer` objects per app, so the app uses a single 250 ms ticker for everything (retries, timeouts, heartbeat, button flash, talk timer).
- The watch sends a signed heartbeat every 10 s so the PC can show whether it is still connected.
- If the PC doesn't answer within 12 seconds of a connection attempt, the app gives up and searches again.
- The button is handled in `onKey` rather than `onSelect`, because some touch watches never deliver `onSelect` for the button.
- The launcher icon is 40x40, the size the Connect IQ compiler reports for the `venusq2m`.

## Troubleshooting

- **Stuck on "Searching..."**: confirm the Windows app says it's waiting for the watch, move closer, and tap the status strip.
- **"Wrong device"**: the UUIDs in `source/Protocol.mc` and `windows/Protocol.cs` must match.
- **"Check PIN"**: type the PIN from the Windows window again (tap the strip). If it keeps failing, click **New PIN** in the Windows app and type the new one.
- **Connects but nothing happens**: PowerPoint must be the focused window, and not running as admin (see the Windows README).
