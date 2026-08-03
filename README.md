# OpenQCY Desktop

OpenQCY Desktop is an open-source Windows controller for QCY earbuds. The first supported target is the **QCY MeloBuds N70 (HT18)**.

> [!IMPORTANT]
> This is an independent community project. It is not affiliated with, endorsed by, or supported by QCY or Dongguan Hele Electronics Co., Ltd. QCY and MeloBuds are trademarks of their respective owners.

[Leia em Português](README.pt-BR.md)

## Project status

OpenQCY Desktop is in an early native-shell milestone. The Windows settings window, notification-area panel, local profile persistence, and read-only Bluetooth discovery are working. Hardware writes remain disabled until commands are captured from the official mobile app and safely reproduced.

![OpenQCY Desktop settings](docs/screenshots/main.png)

![OpenQCY Desktop notification-area panel](docs/screenshots/tray.png)

Battery percentages and device-control responses shown in this milestone are realistic mock data. The local preferences are real and persist across restarts.

Planned daily controls:

- Left, right, and case battery state
- ANC, transparency, normal mode, submodes, and intensity
- EQ presets and a custom multi-band curve
- Touch gesture mapping
- Wear detection with automatic reapply on reconnect
- LDAC, multipoint, game mode, sleep mode, and wind-noise handling
- Prompt volume, disconnect timeout, and find-earbuds action

Firmware flashing, account features, telemetry, ads, and store pages are explicitly out of scope.

## Technology

- C# 14 and .NET 10 LTS
- WinUI 3 / Windows App SDK
- Windows Bluetooth GATT APIs
- MVVM with CommunityToolkit.Mvvm
- Portable, self-contained Windows 11 distribution

The UI design language is called **OpenQCY Glass**: a Windows-native interpretation of the clarity, depth, large radii, and contextual expansion found in modern Apple interfaces. It uses native WinUI controls and original project assets; it does not ship Apple fonts, icons, or artwork.

## Build

Prerequisites:

- Windows 11
- .NET 10 SDK

```powershell
dotnet restore -r win-x64
dotnet build -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet run -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

The GitHub Actions pipeline builds and uploads a self-contained `win-x64` artifact. See [Architecture](docs/architecture.md), [Protocol research](docs/protocol-research.md), and the [performance baseline](docs/performance.md).

## Safety

OpenQCY Desktop will never guess or brute-force commands against connected earbuds. Protocol bytes must be observed in a user-owned Bluetooth capture, documented, replayed in isolation, and covered by tests before they become available in the UI.

## License

[MIT](LICENSE)
