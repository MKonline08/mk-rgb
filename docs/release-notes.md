Download **MK-RGB-0.1.1-Setup.exe** below to install MK RGB and its hardware service. The MSI is optional for managed deployments. Compare downloads against SHA256SUMS.txt. This is an unsigned development preview.

- Fix OpenRGB immediately exiting because its configuration directory did not exist.
- Wait up to 60 seconds for the backend to become ready, report early exits, and keep monitoring responsive during RGB startup.
- Add opt-in Windows startup, tray launch, automatic RGB connection/output, and graceful closure of competing RGB app windows. Fan controllers and background services are never terminated automatically.
- Keep disconnected devices in saved layouts without sending frames to them or pausing every connected device.
- Explain zero-length ARGB headers and retain manual channel resizing; configure the actual connected LED count.
- Report fan restoration failures, invalidate failed RGB writes, and check complete protocol descriptions.
- Publish tested installers and corresponding third-party sources on version tags.

Read-only discovery on the reference PC found ASUS TUF GAMING B650-PLUS WIFI, ASUS TUF GeForce RTX 3060 Ti OC, and SteelSeries Rival 5. Discovery and SDK frame tests do not certify visible physical output. Fan commissioning, real audio/screen/CS2 capture, other hardware, and the full clean-VM matrix remain subject to the validation record.
