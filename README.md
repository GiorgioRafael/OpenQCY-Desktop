# OpenQCY Desktop

[![build](https://github.com/GiorgioRafael/OpenQCY-Desktop/actions/workflows/build.yml/badge.svg)](https://github.com/GiorgioRafael/OpenQCY-Desktop/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/GiorgioRafael/OpenQCY-Desktop?display_name=tag&sort=semver)](https://github.com/GiorgioRafael/OpenQCY-Desktop/releases/latest)
[![license](https://img.shields.io/github/license/GiorgioRafael/OpenQCY-Desktop)](LICENSE)

## Download

[![Download for Windows](https://img.shields.io/badge/Download-Windows_11_x64-25c98b?style=for-the-badge&logo=windows11&logoColor=white)](https://github.com/GiorgioRafael/OpenQCY-Desktop/releases/latest/download/OpenQCY-Desktop-Setup.exe)

Download and run `OpenQCY-Desktop-Setup.exe`. The installer is self-contained, installs only for the current user, and does not require the .NET runtime or administrator access.

[Portable ZIP](https://github.com/GiorgioRafael/OpenQCY-Desktop/releases/latest/download/OpenQCY-Desktop-win-x64.zip) · [All releases](https://github.com/GiorgioRafael/OpenQCY-Desktop/releases)

> [!NOTE]
> Community builds are currently unsigned. Windows may show an "Unknown publisher" or Microsoft Defender SmartScreen warning. Verify the download with the release's `SHA256SUMS.txt` file.

OpenQCY Desktop is an open-source Windows controller for QCY earbuds. The first supported target is the **QCY MeloBuds N70 (HT18)**.

> [!IMPORTANT]
> This is an independent community project. It is not affiliated with, endorsed by, or supported by QCY or Dongguan Hele Electronics Co., Ltd. QCY and MeloBuds are trademarks of their respective owners.

[Leia em Português](README.pt-BR.md)

## Project status

OpenQCY Desktop now has a hardware-backed QCY MeloBuds N70 implementation. On August 3, 2026, the Windows app was validated against vendor ID `23877`, firmware `L 3.0.13 · R 3.0.13`:

- active BLE discovery through QCY manufacturer data (`0x521C`)
- connection to service `A001` and notification-based command responses
- real firmware and left/right/case battery readings
- automatic reconnect from the locally remembered control address, cached Windows GATT service, or the next N70 advertisement
- state reads for ANC, game mode, sleep mode, LDAC, multipoint, wind detection, prompt volume, and auto power-off
- wear detection enabled and disabled from the desktop UI, followed by device readback
- a local profile that is reapplied when the N70 reconnects

![OpenQCY Desktop settings](docs/screenshots/main.png)

![OpenQCY Desktop notification-area panel](docs/screenshots/tray.png)

Implemented daily controls:

- Left, right, and case battery state
- ANC, transparency, normal mode, and the five real N70 ANC scenes (adaptive, indoor, commuting, noisy, and anti-wind)
- EQ presets and a custom multi-band curve
- Touch gesture mapping
- Wear detection with automatic reapply on reconnect
- LDAC, multipoint, game mode, sleep mode, and wind-noise handling
- Prompt volume and auto power-off

The N70 firmware tested here exposes touch mappings directly and supports the parametric EQ command, but does not expose the legacy `0000000B` preset characteristic. OpenQCY therefore enables custom ten-band curves and only enables direct preset switching when that characteristic is actually available. Not every write path has yet been exercised on every N70 firmware variant; unsupported capabilities stay disabled instead of sending guessed commands.

Firmware flashing, account features, telemetry, ads, and store pages are explicitly out of scope.

## Technology

- C# 14 and .NET 10 LTS
- WinUI 3 / Windows App SDK
- Windows Bluetooth GATT APIs
- MVVM with CommunityToolkit.Mvvm
- Self-contained Windows 11 installer and portable distribution

The UI design language is called **OpenQCY Glass**: a Windows-native interpretation of the clarity, depth, large radii, and contextual expansion found in modern Apple interfaces. It uses native WinUI controls and original project assets; it does not ship Apple fonts, icons, or artwork.

## Build

Prerequisites:

- Windows 11
- .NET 10 SDK
- Inno Setup 6.7 or later (only when building the installer locally)

```powershell
dotnet restore -r win-x64
dotnet build -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet run -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet test tests/OpenQCY.Desktop.Tests.csproj -c Release
pwsh ./scripts/build-installer.ps1 -Version 0.1.0
```

The GitHub Actions pipeline builds both distributions. Pushing a semantic version tag such as `v0.1.0` publishes the installer, portable ZIP, and checksums to GitHub Releases. See the [release guide](docs/releasing.md), [Architecture](docs/architecture.md), [Protocol research](docs/protocol-research.md), and the [performance baseline](docs/performance.md).

## Safety

OpenQCY Desktop never guesses or brute-forces commands against connected earbuds. A command must have public interoperability evidence or a redacted capture from user-owned hardware, deterministic tests, and a readback/acknowledgement path before it is enabled. See the exact N70 evidence in [Protocol research](docs/protocol-research.md).

## License

[MIT](LICENSE)
