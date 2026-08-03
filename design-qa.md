# Design QA

Result: **passed for the native-shell milestone**.

## Evidence

- Reference: the supplied iOS Control Center screenshot.
- Implementation: `docs/screenshots/tray.png` and `docs/screenshots/main.png`.
- Comparison state: connected N70 mock state, wear detection enabled, ANC selected.

## Visible checks

- Rounded control groups, compact contextual controls, clear hierarchy, and a dark depth-based surface match the reference's interaction language without copying Apple assets.
- The tray surface keeps the primary actions visible: connection, three battery values, noise mode, wear detection, game mode, open, and exit.
- The main settings window uses a persistent sidebar and scrollable detail area with consistent 12–24 px spacing.
- Text is legible at 100% scaling, labels are not clipped, and every primary control has a realistic state.
- Shared state was changed from the tray and verified in the main window and local JSON profile.
- Navigation through Overview, Sound, Controls, Connection, and General completed without a crash.

## Accepted deviation

The reference uses an Acrylic-like blurred background. The implementation uses an opaque graphite surface because the third-party WinUI notification popup crashes with `0xc000027b` when combined with detached popup surfaces. OpenQCY uses a dedicated borderless WinUI window instead, retaining rounded cards and light-dismiss behavior without the crash.
