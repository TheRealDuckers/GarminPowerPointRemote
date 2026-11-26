# windows/: BLE server + keypress injector

A small .NET 8 Windows app. It advertises a BLE GATT service, accepts PIN-signed one-byte commands from the watch, and sends the matching keypress to whatever window has focus.

## What you see

- **Status header:** blue while waiting for the watch, green once it connects, amber if a device was turned away (wrong PIN), red if Bluetooth has a problem.
- **Last action:** the most recent command and when it arrived.
- **Watch PIN:** the PIN the watch must use. It is generated on first run (6 digits). **Hide/Show** masks it (it hides itself after the first successful connection); **New PIN** makes a fresh 6, 8, 10 or 12 digit one, which stops the old PIN working immediately.
- **Settings:** choose arrow keys or Page Up / Page Down for slide navigation, pause keypress sending without disconnecting, and start with Windows.
- **Test button:** sends Next after 3 seconds so you can check key injection without the watch.
- **Activity log:** a timestamped list of everything that happened, including rejected messages.
- **Tray icon:** closing the window hides it to the tray. Right-click the tray icon to quit.

## Requirements

- Windows 10 (2004 / build 19041) or Windows 11
- A Bluetooth 4.0+ adapter that supports the **LE peripheral role** (the app tells you if not)
- To build: the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Run

```powershell
cd windows
dotnet run -c Release
```

## Self-contained build (no .NET needed to run it)

This is what the GitHub Actions workflow does; the .NET runtime ends up inside the exe:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The exe is in `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`. Use this build for "Start with Windows", so the registry entry points at the real exe.

## Key mapping

| Command | Arrow keys | Page keys |
|---------|-----------|-----------|
| Next | Right arrow | Page Down |
| Previous | Left arrow | Page Up |
| Start | F5 | F5 |
| End | Esc | Esc |
| Black screen | B | B |

For other mappings, edit `KeySender.Execute`, for example Shift+F5 to start from the current slide.

## How the PIN works

See [`docs/protocol.md`](../docs/protocol.md). In short: each packet is `[command][counter][8-byte HMAC tag]`; the app verifies the tag, requires the counter to go up, answers anything else with a BLE "insufficient authorization" error, and pauses for a minute after 5 bad packets. The PIN and the last counter are saved in `%AppData%\GarminPowerPointRemote\settings.json` (plain text; anyone who can read your user profile can read it). At startup the app checks its signing code against a known test packet and logs "Security self-test passed".

## Notes

- **Don't pair the watch in Windows Bluetooth settings.** The watch connects directly to the GATT service; no OS-level pairing is needed.
- The slideshow window must be focused. If PowerPoint runs **as administrator**, run this app as administrator too (Windows blocks injected input into elevated windows otherwise).
- The watch sends a signed heartbeat every 10 s. The status turns green on the first valid one, and back to blue when Windows reports the session closed or 30 seconds pass with nothing heard.
- If advertising stops (for example Bluetooth is toggled), the app restarts it after 2 seconds.

## Troubleshooting

- **"does not support the BLE peripheral role"**: try a different or newer USB Bluetooth adapter.
- **Watch never finds the PC**: check Bluetooth is on, and that the UUIDs here and in `garmin/source/Protocol.mc` match.
- **Amber header / "Rejected a message"**: the watch has the wrong PIN. Type the PIN shown here into the watch (tap the top strip on the watch).
- **Status stays blue but slides advance**: the connection works; the first heartbeat just hasn't arrived yet (up to 10 s).
- **Windows shows a pairing prompt**: accept it. The app doesn't require pairing, but some adapters ask anyway.
