# Performance baseline

Initial measurements were taken on Windows 11 x64 with the native WinUI shell, tray icon, local profile, and the first Bluetooth discovery implementation. They predate the complete control session and must be repeated before the first tagged release.

| Metric | Initial result |
| --- | ---: |
| Foreground working set after warm-up | 130.9 MiB |
| Foreground private memory after warm-up | 64.1 MiB |
| Self-contained portable folder | 271.1 MiB |
| Files in portable folder | 521 |

Working set includes shared Windows and WinUI pages, so private memory is the more useful process-specific comparison. These are development-baseline measurements, not release guarantees; they should be repeated on each tagged release.

The main window remained alive and responsive after being hidden to the notification area. It enters Windows 11 Efficiency Mode when hidden and leaves it when reopened. The first distribution target remains self-contained for reliability. A smaller framework-dependent package can be offered later for users who already have .NET 10 and the Windows App Runtime.
