# OpenQCY Glass

OpenQCY Glass is a Windows-native visual language inspired by the clarity and contextual depth of modern Apple settings and control surfaces.

## Principles

- A quiet graphite base for the main settings window. Mica is intentionally avoided while unpackaged WinUI tray popups have a documented CoreMessaging crash with system backdrops.
- Desktop Acrylic only for temporary surfaces such as the tray flyout.
- Large 18–28 px corner radii and thin translucent borders.
- Compact controls grouped into clear cards instead of dense forms.
- Motion between 140–240 ms with easing; no decorative looping animation.
- Strong legibility when transparency, battery saver, high contrast, or remote desktop disables blur.
- Segoe UI Variable and Segoe Fluent Icons; no Apple fonts or copied assets.

## Tray surface

The tray flyout is a borderless, light-dismiss window anchored above the notification area. Its default view prioritizes connection, batteries, noise mode, wear detection, and the active sound profile. A control can expand in place to reveal its detailed choices, then collapse without navigating away.

## Settings window

The main window uses a persistent sidebar and scrollable detail pages: Overview, Sound, Controls, Connection, and General. Status and controls remain visible without requiring an account or internet connection.
