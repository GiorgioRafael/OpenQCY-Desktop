# QCY MeloBuds N70 protocol research

Status: **hardware validated for the core Windows control path**.

## Tested device

| Field | Result |
| --- | --- |
| Product | QCY MeloBuds N70 / HT18 |
| Advertised vendor ID | `23877` |
| QCY company ID | `0x521C` |
| Firmware | `L 3.0.13 · R 3.0.13` |
| Windows service | `0000A001-0000-1000-8000-00805F9B34FB` |
| Command / notification | `00001001` / `00001002` |
| Validation date | 2026-08-03 |

Bluetooth addresses are deliberately not recorded in this repository, logs, or telemetry. The application stores the last validated N70 addresses only in the user's local profile so it can reconnect without waiting for a new advertisement.

## Discovery result

The classic Windows audio pairing exposes the N70 audio and AVRCP endpoints, but not a directly reusable BLE configuration object. Active `BluetoothLEAdvertisementWatcher` scanning solved this: QCY manufacturer data advertises the product identity, battery state, and a separate control address. The app connects to that address and opens service `A001`.

The tested firmware exposed five characteristics:

| UUID suffix | Properties | Purpose |
| --- | --- | --- |
| `0007` | read | firmware version |
| `0008` | read, notify | battery state |
| `000D` | read, write | touch mappings |
| `1001` | write | framed commands |
| `1002` | read, notify | framed responses |

It did not expose legacy EQ characteristic `000B`. A `0x22` query still returned the ten-band parametric EQ state, so custom EQ is available through the command channel while direct preset switching remains capability-gated.

The desktop reconnect path now tries, in order: the last locally validated addresses, cached Windows instances of service `A001`, paired BLE N70 entries, and finally active manufacturer-data scanning. A dormant BLE control endpoint cannot be woken by software if it is not advertising; while disconnected, the app rescans every 30 seconds and captures the next advertisement caused by opening the case or removing a bud.

## Battery sources

Battery detection uses progressively less specific sources:

1. QCY manufacturer data: left, right, case, and charging flags.
2. Connected GATT characteristic `0008`: uncached read plus notifications.
3. Command query `0x2F` when `0008` is absent or unreadable.
4. Windows `System.Devices.BatteryLife`, when the Bluetooth audio endpoint exposes it. This is clearly marked as an aggregate estimate because it cannot distinguish the two buds and case.

The connected value is refreshed once a minute. A real QCY reading always replaces the aggregate Windows fallback.

## Packet framing

Commands use this form on characteristic `1001`:

```text
FF <body length> <opcode> <parameter length> <parameters...>
```

Requests use opcode `FE` with the queried opcode as the single parameter. Responses arrive on `1002` and may contain one or more opcode/length/parameter blocks.

## Redacted hardware transcript

These reads were reproduced on the tested N70:

| Purpose | TX | RX |
| --- | --- | --- |
| Wearing detection | `FF03FE012C` | `FF052C03000101` (off) |
| ANC | `FF03FE0117` | `FF051703010302` |
| Game mode | `FF03FE0109` | `FF03090102` (off) |
| Sleep mode | `FF03FE0110` | `FF03100100` (off) |
| LDAC | `FF03FE0123` | `FF03230101` (on) |
| Multipoint | `FF03FE0124` | `FF03240101` (on) |
| Wind detection | `FF03FE012A` | `FF032A0101` (on) |
| Prompt volume | `FF03FE011D` | `FF041D02040F` (4/15) |
| Auto power-off | `FF03FE0114` | `FF061404FFFF0000` (never) |

### N70 ANC scenes

Opcode `0x17` uses a three-byte state. The first byte selects ANC/transparency/normal; the remaining bytes select the ANC scene and its firmware parameter. The five scenes below were mapped on firmware 3.0.13:

| UI mode | Parameters | Framed write |
| --- | --- | --- |
| Adaptive | `01 05 00` | `FF051703010500` |
| Indoor | `01 01 02` | `FF051703010102` |
| Commuting | `01 02 02` | `FF051703010202` |
| Noisy | `01 03 02` | `FF051703010302` |
| Anti-wind | `01 04 00` | `FF051703010400` |

OpenQCY confirms all three bytes instead of accepting any `0x17` response as success. UI requests use a latest-selection-wins queue, preventing a delayed response from an earlier click from reverting a newer choice.

### Fix for automatic play/pause

N70 firmware 3.0.13 uses advanced wearing-detection opcode `0x2C`, not the older `0x06` toggle.

```text
Query:   FF 03 FE 01 2C
On:      FF 05 2C 03 01 01 01
Disable: FF 05 2C 03 02 01 01
Off RX:  FF 05 2C 03 00 01 01
```

The write preserves the music and ANC action bytes read from the device. The desktop UI was used to turn the feature on, read it back, turn it off, and read it back again. The final tested state was off, which prevents removal/insertion from automatically pausing or resuming playback.

## Implementation evidence and attribution

The C# implementation is independent. Public interoperability information was compared with:

- [Quicky protocol and product database](https://github.com/hui1601/Quicky)
- [HttpKiwi/OpenQCY](https://github.com/HttpKiwi/OpenQCY), MIT-licensed prior art
- [Microsoft Bluetooth LE advertisement watcher documentation](https://learn.microsoft.com/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementwatcher)

No official QCY source, firmware binary, account credential, or proprietary asset is included.

## Capture workflow for other firmware or models

If a variant does not match the validated capability set:

1. Capture only traffic from user-owned hardware and turn off unrelated Bluetooth devices.
2. Change one setting at a time from a known baseline in the official mobile app.
3. Redact addresses, phone names, tokens, and unrelated packets.
4. Repeat the action and verify the same request and response shape.
5. Add the redacted fixture and deterministic parser/encoder tests before enabling a write.

Firmware traffic remains deliberately out of scope.
