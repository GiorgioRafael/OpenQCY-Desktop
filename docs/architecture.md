# Architecture

OpenQCY Desktop separates UI, device behavior, transport, and protocol encoding so no view ever writes raw Bluetooth bytes.

## Projects and layers

1. **Presentation** — the WinUI 3 window, tray flyout, and `MainPageViewModel`. Controls request typed operations and render live device state.
2. **Device** — `QcyDeviceClient` owns the connected session, serializes commands, waits for notifications, parses state, and confirms writes through an acknowledgement or a follow-up query.
3. **Bluetooth** — `WindowsBluetoothTransport` performs active manufacturer-data scanning; `WindowsBluetoothDeviceConnection` owns service `A001`, its characteristics, subscriptions, reads, and writes.
4. **Protocol** — the platform-neutral `OpenQCY.Protocol` project implements advertisement parsing, `0xFF` framing, typed commands, and parsers. It has no WinUI or Windows Bluetooth dependency.
5. **Persistence** — `ProfileStore` keeps a versioned JSON profile under the current user's local application data and migrates older profile shapes.

The diagnostic console uses the same Bluetooth, device, and protocol projects as the desktop app. It is read-only by default; its only mutating option is the explicit `--disable-wear-detection` switch.

## Connection flow

1. Scan active BLE advertisements for QCY company ID `0x521C`.
2. Parse the advertised vendor ID, battery values, and control address without persisting addresses.
3. Prefer N70 vendor IDs `23872` and `23877`.
4. Open the QCY control address and service `0000A001-0000-1000-8000-00805F9B34FB`.
5. Subscribe to `00001002`, then read direct characteristics and query supported command state through `00001001`.
6. Compare the live state with the local desired profile and write only mismatches. LDAC and multipoint are applied last because they may restart the link.

## Capability model

Capabilities are inferred from characteristics and successful typed responses, not just from the product name. For example, firmware 3.0.13 exposes direct key mappings at `0000000D` and the parametric EQ response `0x22`, but not the legacy preset characteristic `0000000B`. The UI consequently enables gestures and the custom curve while leaving direct preset switching disabled.

## Safety boundary

- Unknown vendor IDs are discoverable for diagnostics but are not selected as an N70 target automatically.
- Commands are serialized through a lock and must receive a notification or query confirmation.
- Missing characteristics and unconfirmed writes produce an error instead of a success state.
- No address, Bluetooth capture, account data, or device telemetry is uploaded.
- Account and firmware operations are intentionally absent.

## Supported runtime

- Windows 11 build 22000 or newer
- x64 validated; ARM64 project support remains unverified on hardware
- self-contained, unpackaged WinUI 3 release
