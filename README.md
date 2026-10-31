# MK RGB

An open-source Windows studio for lighting, cooling, and hardware monitoring.

Open **Lighting**, pick a color, and press **Apply to all devices**. Use **Devices** to configure attached fans and strips. Layouts, LED painting, timelines, and node graphs are optional tools under **Advanced**.

**Development preview. The complete release acceptance plan is not finished.** Hardware support is capability-based. See [validation](docs/validation.md) for the checks actually performed and remaining work, and [compatibility](docs/compatibility.md) before enabling control.

## Features

- Color wheel, named swatches, brightness, and one-click application to all connected devices or one selected device.
- Guided RGB component setup with saved header lengths restored when reconnecting.
- Spatial RGB layout, groups, layered effects, palettes, masks, timeline frames, and node graphs.
- Static, breathing, pulse, rainbow, wave, chase, gradient, sparkle, ripple, temperature, fan-speed, audio, screen, and game-driven effects.
- Real hardware monitoring, persistent SQLite history, CSV export, alerts, sensor aliases, tray readings, and desktop overlay.
- Cooling service with mapped temperature guards, fan commissioning, curve interpolation/mixing, hysteresis, independent rise/fall limits, and firmware restoration.
- Profiles, application/schedule/sensor/power rules, playlists, CS2 Game State Integration, and a loopback event API.
- Offline community presets, checksum-verified catalog downloads, portable profiles, and trusted hardware extension interfaces.

[**Download the Windows EXE installer**](https://github.com/MKonline08/mk-rgb/releases/latest)

## Run

Windows 11 x64 is the target. Run **MK-RGB-0.1.2-Setup.exe** to install the self-contained app and hardware service. The installed program is **MK RGB.exe**. An MSI is also provided for managed deployments. Start **MK RGB** from the Start menu. In Lighting, pick a color and press **Apply to all devices**; connection, output, and saving happen together. **Find / refresh devices** discovers controllers without enabling output. Configure passive fans/strips in Devices once. Commission each fan mapping in Cooling before enabling a cooling policy.

The portable ZIP runs monitoring and editing without installation. Privileged motherboard access and persistent cooling need the installed service. Unsupported or blocked sensors are shown as unavailable. MK RGB does not change Windows driver-security settings.

Closing the window sends the app to the tray. Use the tray's **Exit** command to exit the desktop process. Cooling remains owned by the service. **Ctrl+Alt+M** cycles profiles.

## Build

Install the .NET SDK pinned in `global.json`, then:

```powershell
dotnet restore MkRgb.slnx --locked-mode
dotnet build MkRgb.slnx -c Release
dotnet test tests/MkRgb.Tests/MkRgb.Tests.csproj -c Release
dotnet run --project src/MkRgb.Desktop
```

Create the self-contained EXE installer, MSI, and portable ZIP with WiX 6.0.2:

```powershell
dotnet tool install wix --version 6.0.2 --tool-path .tools
./scripts/package.ps1 -Wix "$PWD/.tools/wix.exe"
```

The packaging script downloads a checksum-pinned OpenRGB 1.1 archive. Generated files are under `artifacts/`, excluded from Git. Releases must also include the matching third-party source archives described in [notices](THIRD-PARTY-NOTICES.md).

## Documentation

- [Architecture and local API](docs/architecture.md)
- [Install and recovery](docs/installation.md)
- [Compatibility and cooling recovery](docs/compatibility.md)
- [Validation record](docs/validation.md)
- [Contribution guide](CONTRIBUTING.md)
- [Security reporting](SECURITY.md)

Original MK RGB code is GPL-3.0-only. OpenRGB, LibreHardwareMonitor, and other dependencies retain their licenses. No account, subscription, or cloud service is required for core operation.
