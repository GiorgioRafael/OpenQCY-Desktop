# Architecture

OpenQCY Desktop uses a layered, transport-aware architecture:

1. **Presentation** — WinUI 3 windows, tray flyout, views, and view models.
2. **Application** — device session, desired profile, reconnect reconciliation, and commands.
3. **Protocol** — typed QCY messages, validation, parsing, checksums, and acknowledgements.
4. **Transport** — Windows BLE GATT or RFCOMM, selected only after capture evidence identifies the N70 app channel.
5. **Persistence** — local JSON profile under the current user's local application data.

The UI never writes raw bytes. It requests a typed operation from the application layer; the protocol layer validates and encodes it; the transport layer sends it and returns a typed result.

## Safety boundary

The first hardware implementation is read-only: device discovery, connection state, and enumeration of GATT services. The code enforces this boundary through two separate contracts:

- `IBluetoothTransport` discovers paired QCY devices and enumerates services without writing.
- `IQcyProtocolAdapter` owns model-specific commands. The default `ProtocolPendingAdapter` reports `SupportsWrites = false`, so a UI preference cannot accidentally become an unvalidated Bluetooth write.

## Reconnect behavior

The locally selected profile is the desired state. On a confirmed device connection, the app reads every state the protocol exposes, computes the difference, and reapplies only mismatched settings. If a value cannot be read, it is applied once per connection rather than polled continuously.

This avoids fighting with the official mobile app while still fixing settings that the earbuds forget after a power cycle.

## Supported runtime

- Windows 11 build 22000 or newer
- x64 initially, ARM64 after transport verification
- Self-contained, unpackaged WinUI 3 release
