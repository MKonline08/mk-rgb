# Installation and recovery

Download **MK-RGB-0.1.2-Setup.exe** from the repository's Releases page and compare its SHA-256 with `SHA256SUMS.txt`. Initial builds are unsigned. This preview has not completed the full physical-hardware and clean-VM acceptance matrix.

The EXE installer contains the MSI and installs the .NET runtime with MK RGB, the `MkRgbHardware` Windows service, and a pinned OpenRGB backend under `Program Files/MK RGB`. It creates a Start-menu shortcut. Administrative installation is required for the service. The installing user, administrators, and SYSTEM can access the named pipe. Other Windows users cannot control that installation through the app's pipe.

1. Launch MK RGB. Monitoring starts without enabling lighting or fan writes.
2. Open Lighting, pick a color from the wheel or swatches, and press **Apply to all devices**. Choose a specific device in **Apply to** for a separate color. This connects RGB, enables output, and saves the choice. For discovery without output, use **Find / refresh devices**. Close competing hardware controllers before claiming devices.
3. In Cooling, identify the physical fan/pump, select its matching RPM reading and temperature guard, and commission the channel. Stop/restart testing is an explicit option for non-pump channels that allow zero duty. Keep pumps identified as pumps. Commissioning restores default control on completion or cancellation.
4. Map curve sources and apply the policy. Imported cooling templates can be mapped to a local channel, but remain disabled until commissioned and explicitly applied.
5. Save the profile. Closing the UI leaves the service controlling enabled cooling channels. Exiting the tray application stops session-reactive lighting inputs.

## Startup, tray, and RGB conflicts

Settings provides opt-in **Start with Windows**, **Launch hidden in the system tray**, **Connect RGB on launch**, and **Enable lighting on launch**. Click **Save preferences** to apply them. Closing the window keeps the app in the tray; use **Exit MK RGB** to stop the desktop process. Startup registration is per Windows user and uses the current executable path.

You can ask known competing RGB app windows to close before connection, either manually or with the opt-in setting. This is graceful closure; MK RGB does not kill fan controllers or stop background services. Close remaining services using their own app. An existing OpenRGB SDK server on loopback port 6743 can be reused.

RGB connection waits for the bundled backend to become ready. If a motherboard ARGB header reports **0 LEDs**, it has no configured output. Open **Devices**, choose the connected component's LED count and quantity for that header, then **Save components**. A custom count supports other fans and strips. Counts are saved locally and restored after reconnecting or restarting the backend. Do not guess header lengths; mark unused headers unused.

USB and motherboard controllers identify themselves, but passive ARGB fans and strips do not report their model or length. Support depends on the backend/adapter and available driver access. Detection does not certify visible lighting output.

The old spatial layout and LED-index editors are under **Advanced → Layout**. Layered compositions, timelines, and graphs are under **Advanced → Studio**. Quick lighting affects selected devices directly and clears their old LED paint. Use **Use studio composition on all devices** in Studio to return them to the advanced layers.

## Recovery and updates

The service restores controls it owns on a normal stop. Windows service recovery restarts it after a crash, with a five-second delay. Startup validates persisted policies and local calibration before enabling writes. Stale controlling temperatures force full cooling. A mapped RPM reading that disappears or stalls during commanded rotation also forces full cooling. Hardware-watchdog-capable extensions can implement `IFanWatchdog`; none are certified in this release.

Software-only controllers cannot guarantee immediate restoration during an OS freeze or abrupt service failure. Firmware controls are restored only where the backend supports it. If a channel cannot be restored, the app reports the failure rather than claiming success.

Install a newer MSI to perform a Windows Installer major upgrade. The installer schedules removal after initialization to permit rollback, stops the service during replacement, and starts it after installation. Repair is supported through Windows Installed Apps or `msiexec /fa <installer>`. These mechanisms need the acceptance testing listed in `validation.md` before stable release.

Uninstall through Windows Installed Apps. The service is stopped and removed, and its normal stop attempts to restore owned controls. User profiles/history in `%LOCALAPPDATA%/MK RGB` and service state in `%PROGRAMDATA%/MK RGB` are retained for recovery/reinstallation. Export settings before changes. Unknown or corrupt settings disable saving to preserve the original file; recover a reviewed `.bak` or restore an exported backup through Settings.

## Driver availability

OpenRGB 1.1 and LibreHardwareMonitor need compatible access to some motherboard, RAM, or CPU devices. A blocked driver means that capability is unavailable. For PawnIO installation consult https://pawnio.eu/. MK RGB does not change Memory Integrity, vulnerable-driver protection, Secure Boot, or other Windows security settings.

## Trusted extensions

Compile against the matching `MkRgb.Core` contracts. Install reviewed sensor/fan assemblies under `Program Files/MK RGB/service/extensions/<id>/`; install session RGB/event assemblies under `Program Files/MK RGB/extensions/<id>/`. Include `manifest.json` and dependencies beside the assembly. Restart the relevant process after installation. Administrator-installed hardware extensions execute with service privileges and must be treated as trusted code. Capability metadata is not a sandbox.

A fan extension also implements `ISensorProvider` on the same object so its native channel/sensor identities can be mapped consistently. Sensor IDs exposed by extensions receive a stable prefix derived from assembly/type. `Restore` must relinquish control; `CanRestore` must accurately describe the device. Use explicit local commissioning before every newly mapped hardware channel.
