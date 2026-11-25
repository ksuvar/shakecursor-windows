# Shake Cursor for Windows

KDE Plasma's **Shake Cursor** for Windows: shake the mouse and the pointer grows so you can find it.
Keep shaking and it grows more. Stop, and two seconds later it shrinks back.

The shake detection and animation are a straight port of KWin's
[`src/plugins/shakecursor`](https://invent.kde.org/plasma/kwin/-/tree/master/src/plugins/shakecursor)
by Vlad Zahorodnii (Plasma 6). The original files are kept in [`upstream/`](upstream/)
(KWin commit `0be32096d8af3fa47eecfa585a938f91c3a27f3a`).

## Install

**Option 1: download.** Get `ShakeCursor.exe` from the
[latest release](https://github.com/ksuvar/shakecursor-windows/releases/latest), run it, and click **Install**.

**Option 2: one line in PowerShell.**

```powershell
irm https://raw.githubusercontent.com/ksuvar/shakecursor-windows/main/install.ps1 | iex
```

The installer is per-user, so it needs no admin rights. It asks you:

- **Use the KDE Breeze mouse pointers.** Ticked by default; your current pointers are saved first.
  Untick it to keep the pointers you already have. Shaking works with any pointer set either way.
- **Start with Windows.**
- **Add to the Start menu.**

It also adds an entry to **Settings → Apps → Installed apps**, so you can uninstall it like any other app.
There is also **Just try it without installing**.

> Windows SmartScreen may warn about the download because the exe is not code-signed.
> Click **More info → Run anyway**, or build it yourself (see below).

## Using it

Shake the mouse. That's it.

Shake Cursor lives in the hidden part of the notification area, under the **^** arrow on the taskbar.
Click the icon to open the settings, or right-click it for the menu:

| Menu item | What it does |
|---|---|
| Shake to find pointer | Turns the effect on or off |
| Settings... | Shake strength, size, growth, largest size, time window, pointers, autostart |
| Mouse pointers | Switch between **KDE Breeze**, **Windows default**, and **My previous pointers** |
| Start with Windows | Toggles autostart |
| Check for updates... | Shows your version and opens the latest release on GitHub. Running the new exe updates it and keeps your settings and pointers |
| About / GitHub | Opens this page |
| Please give it a ★ on GitHub | Opens this page, if you'd like to star it |
| Uninstall... | Removes the app and puts your previous pointers back |

Opening Shake Cursor from the Start menu while it's already running opens its settings.

Shake Cursor never updates itself and never connects to the internet. **Check for updates...** just opens the
releases page in your browser.

## Works with any pointer set

KWin always draws the arrow. Here the big pointer is **whatever shape is showing**: arrow, hand, text beam,
resize arrows, busy, and so on. It comes from **your current Windows pointer scheme**, whatever that is
(Breeze, Windows default, or any third-party set), loaded at up to 256 px so it stays sharp.
Old-style "invert" pointers, like Windows' default text beam, get a white outline so they stay visible on dark
backgrounds. Pointers that an app draws itself are enlarged too.

## Same behaviour as KWin

| | KWin | This port |
|---|---|---|
| Detection | Over the last `TimeInterval` ms, the trail length divided by the trail's bounding-box diagonal is greater than `Sensitivity`. The diagonal must be at least 100 px, and moves in the same direction are merged. | Same code (`ShakeDetector.cs`), in logical pixels |
| Mouse button held | Resets the detector | Same |
| Pointer confined (games) | Ignored | Same, using `GetClipCursor` |
| First shake | Grows to `Magnification` (3×) | Same |
| Further shakes | Adds `OverMagnification` (+1×) each time | Same, with a Windows-only cap (`MaxMagnification`, 10×) |
| Animation | 200 ms, InOutCubic | Same |
| Shrink | 2 s after the last shake | Same |
| Rendering | Hides the real cursor and draws a high-resolution one in the overlay | Hides the system cursors and draws a high-resolution one in a click-through, top-most layered window |

Settings are saved in `%APPDATA%\ShakeCursor\shakecursor.ini`. The first four keys are KWin's, with KWin's defaults:

```ini
[Effect-shakecursor]
TimeInterval=1000
Sensitivity=4
Magnification=3
OverMagnification=1
[Windows]
Enabled=true
MaxMagnification=10
```

## Command line

| | |
|---|---|
| `ShakeCursor.exe` | Not installed yet: opens the installer. Installed: runs, or opens settings if it's already running. |
| `--install --silent` | Installs with the defaults and no questions. Add `--keep-cursors`, `--no-autostart`, or `--no-start-menu` to change them. |
| `--portable` | Runs without installing. |
| `--uninstall [--silent]` | Uninstalls. |
| `--debug` | Logs pointer events and detections to `%TEMP%\shakecursor.log`. |

## Build

```
build.cmd
```

This uses the C# compiler that ships with Windows (.NET Framework 4.x), so there is nothing to install. The output is
`bin\ShakeCursor.exe`, a single file with the Breeze pointers embedded. GitHub Actions builds every push, and
pushing a `v*` tag attaches the exe to a release.

## Notes

- If the app is killed while the pointer is enlarged, the real pointer stays hidden. Starting the app again brings it back,
  and so does **Mouse settings → Additional mouse settings → Pointers → Apply**.
- Uninstalling restores the pointers you had before installing.

## License and credits

- Code: GPL-2.0-or-later, the same as KWin. See [LICENSE](LICENSE).
  The shake detection and effect logic are based on KWin's shakecursor plugin by Vlad Zahorodnii.
- Breeze pointers: the KDE Breeze cursor theme, converted to Windows format by
  [black7375/Breeze-Cursors-for-Windows](https://github.com/black7375/Breeze-Cursors-for-Windows), CC BY 3.0.
  See [assets/cursors/breeze/NOTICE.md](assets/cursors/breeze/NOTICE.md).
