Download **MK-RGB-0.1.3-Setup.exe** below. It installs everything MK RGB needs and updates an existing install. Builds are unsigned development previews; checksums are in SHA256SUMS.txt.

**Works out of the box**
- The Setup EXE now also installs the signed **PawnIO** driver (skipped if you already have it). RAM lighting, many motherboard lighting chips, and CPU temperatures need it, and it was the most common reason devices were missing.
- New installs scan for RGB devices when MK RGB starts. If PawnIO is ever missing, the Devices page has a one-click **Install PawnIO** button.

**Better device detection**
- Waits for OpenRGB to finish detecting instead of reading a half-filled device list.
- Other RGB apps (iCUE, Armoury Crate, SignalRGB...) now cause a warning instead of blocking the scan, and FanControl no longer blocks it at all.
- Two identical devices (for example a matched RAM kit) no longer break the whole scan.
- The Devices page explains why something might be missing and has **Identify** to flash a device so you can tell which one it is.

**New look**
- Fire logo, dark custom title bar, sidebar with every page, and animations throughout (turn them down with **Reduce motion**).
- Redesigned Lighting page: color wheel, RGB sliders, palette, whites, recent colors, a second color, nine effects, a live preview, and optional live updates to your devices.

**Fixes**
- Fans no longer stay loud after your PC cools down slowly.
- Other Windows accounts on the same PC, and IT-managed installs, can now use the hardware service.
- Cooling warnings clear once a fan recovers; less UI stutter.

Fans and strips on RGB headers still can't report themselves; set them up once in Devices. Some USB devices need a WinUSB driver that OpenRGB documents separately. CI verifies install, repair, crash recovery and uninstall, including that the bundle installs PawnIO; physical lighting output is not tested automatically.
