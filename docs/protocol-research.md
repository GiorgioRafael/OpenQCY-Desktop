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

Bluetooth addresses are deliberately neither recorded here nor persisted by the application.

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
