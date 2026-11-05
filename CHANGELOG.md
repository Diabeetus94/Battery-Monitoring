# Changelog – Akku-Anzeige (Battery Display)

## 1.3.2 Alpha – 2026-11-05
- Data sources only show what was actually found on this PC: built-in readers (HyperX, DualSense, AirPods, Logitech receiver) only appear when matching hardware is connected
- "Scan drivers & devices" reports newly found sources and installed but not (yet) supported vendor software
- Setup: built-in readers are no longer listed as "driver software"

## 1.3.1 Alpha – 2026-11-05
- New: swap devices right on the taskbar – clicking a tile opens a window with all devices above it; the choice is saved instantly (Settings → Taskbar, can be turned off)
- New: Windows notification when an update is available (Settings → Updates, can be turned off)
- New: automatic updates – download, verify and install themselves (Settings → Updates, off by default)
- Right-click menu matches the program design (light/dark)

## 1.3.0 Alpha – 2026-11-05
- The icon in the taskbar notification area (bottom right) now matches the program icon

## 1.2.0.4 Alpha – 2026-11-05
- New: "Report a bug" under Settings → About – leads straight to GitHub: "Create issue" (report bugs, ideas) or "Create pull request" (suggest a code change)
- © 2026 Bobohatt & Diabeetus
- Fixed: gaps in the group borders (e.g. "Alarm sound") – borders now match the light and dark design
- Fixed: leftover image fragments when scrolling the "Settings" tab
- Fixed: the mouse wheel changed drop-down fields and sliders (e.g. the alarm sound) while scrolling – it now scrolls the page
- Fixed: white box at the top right of the device list
- Fixed: light scrollbars in the changelog, update and bug windows
- Fixed: "&" was not shown in labels

## 1.2.0.3 Alpha – 2026-11-05
With contributions from garpu-ng (pull requests #1 and #2) – thank you!
- New built-in device sources without vendor software: Logitech directly via the receiver (HID++), HyperX Cloud III S Wireless, PlayStation DualSense, AirPods/Beats (Bluetooth LE)
- "Search drivers & devices" under data sources: finds software installed later and connected devices
- Custom tile colors with the Windows color picker
- Support for an auto-hiding taskbar – the tiles move along with it
- Fixed: typing in the "Display name" field lost focus after every letter
- Fixed: maximized windows were treated as fullscreen games when the taskbar is auto-hidden
- Fixed: tiles briefly disappeared when clicking the taskbar
- New installations start without preset example devices and show your own devices automatically
- Lower CPU usage while idle
- Changelog window: only one blank line between versions

## 1.2.0.2 Alpha – 2026-11-05
- Updates are now downloaded and installed directly inside the app – no browser needed
- Before installing, the update is verified to be signed with the Akku-Anzeige certificate and unmodified
- New update window showing the version number and the changes of the new version
- Changelog right inside the app (Settings → About → Changelog)
- Setup: silent update mode that keeps your existing options and restarts the app afterwards

## 1.2.0.1 Alpha – 2026-11-05
- About section moved into the "Settings" tab (version, publisher, update check)
- "Settings" tab is scrollable – room for more sections, dark scrollbar
- Update check via an update info file, GitHub releases as fallback; four-part version numbers
- Disabled drop-down fields in dark mode now use white text

## 1.2.0 Alpha – 2026-11-05
- First alpha version
- Battery tiles on the taskbar, own devices, order and width per monitor – also for switched-off monitors
- Logitech G HUB (battery level, charging, active/standby/off) and Windows Bluetooth
- Disconnected and sleeping devices shown in gray with the last battery level
- Own warning window with warning sound – also with "Do not disturb" and in game mode; volume and sound selectable
- Adjustable red/orange/green color ranges
- Automatic light/dark mode, English/German
- Visible above the Start menu and taskbar menus (UIAccess)
- Setup detects installed, compatible driver software
