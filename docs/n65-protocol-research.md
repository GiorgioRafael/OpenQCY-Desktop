# QCY MeloBuds N65 protocol research

Status: **hardware validated for the Windows control path**.

## Tested device

| Field | Result |
| --- | --- |
| Product | QCY MeloBuds N65 |
| Advertised vendor ID | `32894` (`0x807E`) |
| QCY company ID | `0x521C` |
| Firmware | `2.0.13` (opcode `30` returns `02 00 0D`) |
| Chipset | Jieli (Bluetooth PnP vendor `0x05D6`) |
| Windows service | `0000A002-0000-1000-8000-00805F9B34FB` |
| Command / notification | `00000001` (write without response) / `00000002` (notify) |
| Capture source | Official QCY Android app 1.2.2 (`com.qcy.audio.google`), Bluetooth HCI snoop log on a user-owned phone |
| Validation date | 2026-11-12 |

Bluetooth addresses, phone and PC names are not recorded in this repository. Opcodes `4D` (paired devices) and `4F` (multipoint connection state) carry names and addresses, so their payloads are deliberately left out.

## Discovery

The N65 advertises the same QCY manufacturer data as the N70: vendor ID in bytes 0-1, left/right/case battery in bytes 5-7 (bit 7 = charging). The control address in bytes 11-16 is the classic BR/EDR address and cannot be opened over LE on Windows. The GATT connection uses the address of the advertisement itself.

The N65 does not expose service `A001`. It exposes `A002` with only two characteristics, and no direct firmware, battery, equalizer or key-function characteristics. Everything goes through the command channel.

## Packet framing

The QCY opcode/length/parameter blocks are wrapped in Jieli RCSP frames:

```text
FE DC BA <flags> FF <length, u16 big endian> [status] <sequence> <QCY blocks> EF
```

| Flags | Meaning |
| --- | --- |
| `C0` | Command from the app; a response is expected |
| `00` | Response; carries a status byte (`00` = OK) before the sequence |
| `80` | Push from the earbuds; no response expected |

- `length` counts `[status] + sequence + QCY blocks`.
- The response echoes the command's sequence number. Pushes use the earbuds' own counter.
- A write is acknowledged with an empty response (`FE DC BA 00 FF 00 02 00 <sequence> EF`). The new value is **not** echoed, so OpenQCY reads it back.
- Queries use the QCY request block `FE 01 <opcode>`, as on the N70.
- Before restarting, the earbuds may send native Jieli commands (opcode other than `FF`, e.g. `C4`). They are not part of the QCY protocol and are ignored.

## Redacted hardware transcript

| Purpose | TX | RX |
| --- | --- | --- |
| Firmware | `FEDCBAC0FF000423FE0130EF` | `FEDCBA00FF00070023300302000DEF` (2.0.13) |
| Battery | `FEDCBAC0FF000424FE012FEF` | `FEDCBA00FF000700242F03646464EF` (100/100/100) |
| ANC query | `FEDCBAC0FF000426FE0117EF` | `FEDCBA00FF000700261703010500EF` (noise cancellation) |
| ANC off | `FEDCBAC0FF0006301703020000EF` | `FEDCBA00FF00020030EF` (ack), then push `FEDCBA80FF0006181703020000EF` |
| LDAC off | `FEDCBAC0FF000498230100EF` | `FEDCBA00FF00020098EF` (ack), then the link restarts |
| Game mode on | `FEDCBAC0FF0005C64A020100EF` | ack, then the link restarts |
| Game mode off | `FEDCBAC0FF0005D64A020000EF` | ack, then the link restarts |
| Dynamic EQ on | `FEDCBAC0FF0004E8270101EF` | ack |
| Channel balance 60 | `FEDCBAC0FF0004EF16013CEF` | ack |
| Gesture: single tap left = no action | `FEDCBAC0FF0005402B020100EF` | ack |

The unit tests in `tests/QcyN65Tests.cs` use these frames as fixtures.

## Commands

| Opcode | Function | Values |
| --- | --- | --- |
| `30` | Firmware version | `02 00 0D` = 2.0.13 |
| `2F` | Battery left/right/case | percent, also pushed |
| `17` | ANC | `01 05 00` noise cancellation · `03 01 04` transparency · `02 00 00` off (same values as the N70) |
| `22` | Equalizer | see below |
| `27` | Dynamic EQ | `01` on / `00` off; only with a preset, choosing a preset pushes `00` |
| `23` | LDAC | `01` on / `00` off (the N70 uses `02` for off); restarts the link |
| `4A` | Game mode | `01 00` on / `00 00` off; restarts the link |
| `16` | Channel balance | `0`-`100`, `50` = centered |
| `2B` | Gestures | see below |
| `11` | Ear-tip fit test | write `01 01 01`; the result is pushed (e.g. `02 03 03`) |

Read by the official app but not used by OpenQCY: `10`, `14`, `1D`, `1F`, `24`, `50`, `54`, `5B`. The app also queries `44`, which the N65 does not answer. "Find my earbuds" sends no BLE command; the app plays the sound over the audio stream.

### ANC transitions

The N65 acknowledges an ANC change immediately and pushes the new mode about one second later. A change sent while the previous transition is still running (about 2-3 seconds) is acknowledged but ignored. OpenQCY waits 3 seconds after the last change and resends once if the pushed state does not match.

### Equalizer (`22`)

```text
<preset> <master gain, i16 LE / 100 dB> { <frequency u16 LE Hz> <gain i16 LE / 100 dB> <Q u16 LE / 100> <type> } x N
```

- Presets carry the full band data. The official app lists: `01` default, `13` superior quality, `02` pop, `03` bass boost, `04` rock, `05` soft, `06` classical, `11` pure vocals, `10` original sound, `0F` soft immersion, `0D` game. The captured bytes are in `QcyN65Equalizer.Presets`.
- The default preset is the factory tuning: 7 bands with gains from -15 to +13 dB. The custom curve (`81`) starts from it.
- Custom curve editor in the official app: 1-10 bands, 20-20000 Hz, gain -8 to +8 dB, Q 0.30-20.00. Bands added to a curve use 32, 64, 125, 250, 500, 1000 and 2000 Hz, gain 0, Q 0.70, peak.
- Filter types: `00` high-pass, `01` low-pass, `02` peak, `03` high shelf, `04` low shelf. `05` only appears in the factory presets.
- Master gain: fixed at -14 dB in the presets. For custom curves the app computes `min(-14, -(peak + 5 dB))`, where `peak` is the maximum of the summed RBJ biquad response at 48 kHz. This reproduces 52 of 59 captured curves byte for byte; the other 7 (extreme Q near 20 kHz) come out quieter, never louder.
- Saving a curve in the official app sends nothing to the earbuds: saved curves live in the app.

### Gestures (`2B`)

Write: `2B 02 <gesture> <function>`, one gesture per command. Read: `FE 01 2B` returns every `<gesture> <function>` pair.

| Gesture | Left | Right |
| --- | --- | --- |
| Single tap | `01` | `02` |
| Double tap | `03` | `04` |
| Triple tap | `05` | `06` |
| Press and hold | `09` | `0A` |

| Function | Value |
| --- | --- |
| No action | `00` |
| Play / pause | `01` |
| Previous / next track | `02` / `03` |
| Voice assistant | `04` |
| Volume up / down | `05` / `06` |
| Noise control | `0B` |

Factory mapping: single tap volume down / volume up, double tap play-pause / next, triple tap previous / noise control, press and hold voice assistant on both sides. The read also returns `07`, `08` (`00`) and `41`-`4A` (`80`); the official app never writes them and OpenQCY leaves them untouched.

## Capture workflow

1. Enable the Bluetooth HCI snoop log on the phone (developer options, **Enabled**, not Filtered), then turn Bluetooth off and on.
2. On Samsung phones, confirm with `adb shell dumpsys bluetooth_manager` that `sSnoopLogSettingAtEnable = FULL` before recording.
3. Change one setting at a time in the official app, about 10 seconds apart, and note the time.
4. Collect the log with `adb bugreport` (`FS/data/log/bt/btsnoop_hci.log` on Samsung).
5. Remove addresses, device names and unrelated packets before sharing any fixture.
