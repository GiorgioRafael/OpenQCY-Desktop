# QCY N70 protocol research

Status: capture pending.

## Windows discovery result

The read-only Windows 11 probe finds the N70 as a paired audio device, but the tested system does not currently expose a matching `BluetoothLEDevice` configuration endpoint to the application. No raw addresses are stored or published. This means the next protocol step is an Android HCI capture while changing one setting at a time in the official mobile app.

## Safety rules

- Capture only traffic from hardware owned by the tester.
- Turn off unrelated Bluetooth devices before capturing.
- Redact phone names, Bluetooth addresses, account tokens, and unrelated packets before publishing.
- Never brute-force opcodes or write to an unknown characteristic.
- Do not publish firmware binaries or official-app credentials.

## Capture workflow

1. Enable Android developer options, USB debugging, and Bluetooth HCI snoop logging.
2. Reboot Bluetooth and connect only the QCY MeloBuds N70.
3. Start a clean capture and open the official QCY app.
4. Change one setting at a time from a known baseline, waiting between actions.
5. Generate an Android bug report through ADB and extract the Bluetooth snoop log.
6. Filter by the N70/app transport and correlate ATT, GATT, L2CAP, or RFCOMM writes with each action.
7. Repeat each action twice and confirm the same request and acknowledgement shape.
8. Document redacted fixtures and implement read-only discovery before enabling writes.

## Capture matrix

| Area | Actions |
| --- | --- |
| Wear detection | on, off |
| Noise control | normal, ANC, transparency, adaptive/submodes, intensity |
| EQ | each preset, one-band changes, full custom curve |
| Gestures | every action for each supported gesture and side |
| Connectivity | multipoint, LDAC, game mode |
| Comfort | sleep mode, wind handling, prompt volume, disconnect timeout |
| Utility | battery query, case battery, find left/right |

Firmware traffic is deliberately excluded.
