# Notch

A fluid, Apple-style notch for Windows: a small black pill at the top of the screen that shows what's playing and your focus timer, and opens into a hub when you hover it, click it, or pull it down.

- `notch-playground.html`: the motion lab used to design the feel, states and settings (open it in a browser).
- `src/NotchApp`: the Windows app (WPF on .NET 11).

## Run

```
cd src/NotchApp
dotnet run
```

Requires the .NET 11 SDK on Windows 10 (19041) or later. The app lives in the tray; left-click the tray icon or right-click the notch for Settings.

Launch options for checking states without the mouse: `--hub`, `--hub=clip` (or `notif`, `shelf`, `dev`, `apps`, `system`), `--timer`, `--edit-timer`, `--sample` (example clipboard items in memory, plus this project's folder on the shelf).

## What works now

- Transparent, click-through, always-on-top window that never steals focus, crisp on mixed-DPI setups.
- Spring engine from the motion spec (damping ratio + response, momentum spring for flicks and arrivals, press feedback).
- Hover to open (with intent delay), click to pin, pull down to open and flick up to close (1:1 tracking, rubber band, momentum projection, velocity hand-off), click anywhere else to close.
- Now playing from any app (Windows media sessions): album art, colour-matched glow, play/pause/next/previous, progress, swipe the pill to skip.
- Focus timer with the watch-style dial, phases (focus, break, long break), auto-start, "ends at". Digits roll into place as they change.
- Timer editor: tap the time on the dial and the card grows into an editor. Scrub the minute ruler like a watch crown (flick it and it glides, rubber-bands past 1 and 180), scroll it a minute at a time, or tap 10/15/25/45/60; steppers for breaks and sessions and an auto-start switch. Changing the length of the phase that's running restarts it at the new length.
- Scroll on the notch for volume (no Windows flyout), charging pop-up, free up memory.
- System tab: CPU with history, memory, battery.
- Notifications: when an app posts a Windows notification, it drops into the notch with the app's logo or initial and a glow in its colour; click it to open the app, or pull down for the hub. The Alerts tab keeps recent ones. Settings → Notifications: on/off, hide the message text (for screen sharing), how long it stays, mute individual apps, and stay quiet during Windows Do not disturb (on by default; notifications wait in Alerts). The Devices tab's Do not disturb tile shows whether it's on. Windows still shows its own banner too; Notch can read notifications but can't hide them.
- Clipboard tab: history of text, links, colours, code and images with the app each came from; click a row to copy it back, pin, remove, open links. Copies marked private by password managers are skipped. History lives in memory; only pinned text is saved.
- Devices tab: Wi-Fi and Bluetooth on/off, current network, paired Bluetooth devices with connection state and battery, a pop-up when one connects. Airplane mode and Do not disturb open their Settings pages (Windows has no public switch for them), and so does connecting a device.
- Apps tab: launcher for apps, files, folders and websites with real Windows icons. Drop apps, shortcuts or folders onto the Apps tab to add them; add websites and reorder or remove items in Settings → Apps.
- Shelf tab: drag files onto the notch (it opens into a drop zone), drag them back out into any app, double-click to open, Add files, Save all (copies to Downloads\Shelf). The shelf remembers files across restarts without moving them.
- Settings (Windows 11 Fluent): display choice with an arrangement preview, follow-cursor mode, left/center/right, floating/attached, sizes, tab order and visibility, live activities, feel, glow, timer lengths, start with Windows, hide in full screen.
- Moving between displays animates out toward the target screen and in from the matching side.

## Next

Ideas: reply and action buttons where Windows allows them.

Diagnostics go to `%AppData%\Notch\log.txt`.
