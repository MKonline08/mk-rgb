Download **MK-RGB-0.1.2-Setup.exe** below. It updates the installed app and hardware service. Initial builds are unsigned development previews; checksums are in SHA256SUMS.txt.

- Lighting is now the first screen: color wheel, swatches, brightness, and Solid / Breathe / Rainbow / Color wave.
- Pick a color and press **Apply to all devices**. Connection, output, and saving happen together. A device selector supports separate colors.
- Old LED paint no longer blocks a new whole-device color. Quick lighting is separate from advanced layers, with an explicit switch back to the studio composition.
- Devices needing component setup appear first. Add fans/strips by LED count and quantity, including custom components. Header lengths are saved and restored after backend reconnect/restart.
- Spatial layouts, LED indexes, timelines, and node graphs moved under Advanced. Profile tools are hidden from the everyday Lighting/Devices screens.
- Existing cooling, monitoring, startup/tray preferences, and advanced profiles are preserved.

Passive ARGB fans and strips cannot identify their model or LED count automatically; unused headers must remain unused. Backend support and driver access determine hardware availability. This update does not claim universal hardware compatibility or verified physical GPU/motherboard output.

Automated checks cover whole-device color application, old-paint removal, per-device isolation, persistence, header limits/restoration, and the existing engine/backend/API tests. No desktop control, screenshots, or physical lighting tests were performed for this update, at the user's request.
