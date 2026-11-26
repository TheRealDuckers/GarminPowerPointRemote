# Garmin PowerPoint Remote

Control a PowerPoint slideshow from a **Garmin Venu Sq 2 Music** over Bluetooth Low Energy.

```
┌────────────────────┐   BLE (GATT write)   ┌──────────────────────────┐
│  garmin/           │ ───────────────────▶ │  windows/                │
│  Connect IQ app    │  PIN-signed commands │  BLE GATT server (C#)    │
│  (BLE central)     │                      │  -> SendInput keypresses │
└────────────────────┘                      └──────────────────────────┘
```

A Garmin watch can only be a BLE *central*, so the Windows app is the BLE *peripheral*: it advertises a custom GATT service, the watch finds and connects to it, then writes a signed one-byte command. The Windows app checks the PIN signature and turns the command into a keypress.

| Folder | What it is |
|--------|------------|
| [`garmin/`](garmin) | Connect IQ watch app (Monkey C) for `venusq2m` |
| [`windows/`](windows) | .NET 8 tray app: BLE GATT server, status window, keyboard injection |
| [`website/`](website) | Small static site with beginner-friendly install instructions |
| [`docs/`](docs) | [Protocol and PIN signing spec](docs/protocol.md) |
| [`.github/workflows/`](.github/workflows) | Builds for Windows and Garmin, and publishes the website |

## For users

Follow the website: **https://therealduckers.github.io/GarminPowerPointRemote/** (after you enable GitHub Pages, see below), or open `website/index.html` locally.

## For developers

### Build

- **Windows:** `cd windows && dotnet run -c Release` (needs the .NET 8 SDK). See [`windows/README.md`](windows/README.md).
- **Watch:** build `garmin/` for `venusq2m` with the Connect IQ SDK. See [`garmin/README.md`](garmin/README.md).

### Security (PIN)

Every command is signed with a PIN (6 to 12 digits). The PC generates it and shows it in its window; you type it into the watch once. Packets carry an HMAC-SHA256 tag and an increasing counter, so strangers can't send commands and recorded packets can't be replayed. The PC locks out guessing after 5 wrong packets in a minute. Details and a test vector: [`docs/protocol.md`](docs/protocol.md).

The Bluetooth link itself isn't encrypted, so a short PIN stops casual and drive-by devices but not someone who records a genuine packet and tries every PIN offline. Use a 10 or 12 digit PIN if that matters.

### GitHub Actions

| Workflow | What it does | Needs setup? |
|----------|--------------|--------------|
| [`windows.yml`](.github/workflows/windows.yml) | Publishes `PptRemoteServer.exe` as a **self-contained** single file (the .NET runtime is inside, so users install nothing) for `win-x64` and `win-arm64`, and uploads zips as artifacts | No |
| [`garmin.yml`](.github/workflows/garmin.yml) | Builds a dev `PptRemote.prg` for `venusq2m` | **Yes**, one time (Garmin login), otherwise it skips itself |
| [`pages.yml`](.github/workflows/pages.yml) | Publishes `website/` with GitHub Pages | Yes: Settings > Pages > Source: **GitHub Actions** |

**Garmin workflow setup.** Garmin only lets logged-in developers download its SDK, so:

1. Repo **Settings > Secrets and variables > Actions > Secrets**: add `GARMIN_USERNAME` and `GARMIN_PASSWORD`.
2. On your own computer, install [`connect-iq-sdk-manager`](https://github.com/lindell/connect-iq-sdk-manager-cli) and run `connect-iq-sdk-manager agreement view`. Read the agreement and copy the hash it prints.
3. Same page, **Variables** tab: add `CONNECT_IQ_AGREEMENT_HASH` with that hash.
4. Optional: secret `CIQ_DEVELOPER_KEY` (base64 of your `developer_key.der`) for a stable signing key, and variable `CIQ_SDK_VERSION` (default `^6.2.2`).

**Releases.** Push a version tag and both workflows attach their files to a GitHub Release, which is what the website's download links point at:

```
git tag v0.1.0
git push origin v0.1.0
```

## Status

The BLE, input and PIN code was checked against Garmin's Connect IQ API docs and Microsoft's WinRT docs. It was written without the Connect IQ SDK or a .NET toolchain, so it has **not been compiled or tested on hardware yet**. The Windows workflow will compile it for you on the first push, and the Garmin one once its secrets are set. Expect to fix a few small things on first build.
