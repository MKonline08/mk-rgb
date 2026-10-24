# Third-party software

MK RGB original source is GPL-3.0-only. The following independently licensed components retain their own terms.

| Component | Pinned version | License/source |
| --- | --- | --- |
| OpenRGB (separate process) | 1.1 / 8e16de2 | GPL-2.0-or-later; https://codeberg.org/OpenRGB/OpenRGB/src/tag/release_1.1 |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 and notices; https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6 |
| .NET runtime | 10.0.13 | MIT and third-party notices; https://github.com/dotnet/runtime |
| Microsoft.Data.Sqlite | 10.0.13 | MIT; https://github.com/dotnet/efcore |
| SkiaSharp | 4.153.1 | MIT; Skia has separate BSD notices; https://github.com/mono/SkiaSharp |
| NAudio | 2.2.1 | MIT; https://github.com/naudio/NAudio |

OpenRGB's pinned Windows distribution includes Qt 6.8.3 (Core, Core5Compat, Gui, OpenGL, Widgets and plugins), libusb 1.0.27, hidapi-hotplug 0.15.0, PawnIOLib, PawnIO modules, and Microsoft DirectX shader libraries. Their independent licenses continue to apply. Qt and libusb shared libraries remain replaceable. MK RGB adds no static proprietary linking.

The package includes license texts in `licenses/`, including Qt's license inventory, Skia third-party notices, LibreHardwareMonitor notices, HIDAPI's BSD license, libusb LGPL, and DirectX shader compiler notices. Runtime-generated .NET notices are preserved in the application folder. OpenRGB embeds additional dependencies such as mbedTLS, JSON, and hueplusplus; its release source includes their headers and attribution.

`scripts/sources.json` pins the source archives distributed next to the installer: OpenRGB 1.1, LibreHardwareMonitor 0.9.6, Qt Base/5Compat 6.8.3, libusb 1.0.27, and PawnIO/PawnIO.Modules source snapshots. The PawnIO snapshots are identified by commit; exact correspondence to the third-party DLLs/modules bundled by upstream remains part of the stable-release provenance review. Do not represent this preview as a completed redistribution audit.

The separate PawnIO kernel-driver installer is **not** bundled or installed. Some motherboard and DRAM access requires an independently installed compatible driver. Obtain it from https://pawnio.eu/ and follow upstream terms. MK RGB never disables Windows security features or installs unsigned kernel drivers.

Build/test tools are not installed as runtime components. Package lockfiles identify transitive managed dependencies. The MSI is unsigned unless a release explicitly states otherwise.
