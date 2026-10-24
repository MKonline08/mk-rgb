# Hardware compatibility and cooling

Support is tracked per capability. **Upstream support** means the pinned backend recognizes a device; **verified** means an MK RGB workflow was physically exercised on that exact device and firmware. Discovery is not verification of writes.

| Reference hardware | Evidence in this workspace | Control status |
| --- | --- | --- |
| ASUS TUF GAMING B650-PLUS WIFI | Board identity read | Motherboard RGB/fan writes unverified |
| AMD Ryzen 5 9600X | CPU identity and available metrics read; invalid zero temperature suppressed | Privileged temperature access requires service/driver validation |
| NVIDIA GeForce RTX 3060 Ti | Real monitoring values exposed | GPU fan/RGB writes unverified |
| AMD integrated graphics | Real monitoring values exposed | RGB/fan writes unverified |

No claim is made that every motherboard, controller, AIO, or GPU supports writes. OEM firmware, controller wiring, third-party software, and driver availability can restrict access. A shared PWM splitter does not expose independent control of its attached fans. Supported upstream devices may still need device-specific validation.

## Commissioning

Select a physical channel, its RPM feedback, and a temperature guard. Confirm whether it is a pump. The service validates the mapping by sweeping 100% down to a conservative floor and observing RPM response. Pumps never use the fan stop sweep. Missing temperature/RPM data or temperatures at least 75 °C abort commissioning and restore default control.

The default commissioning sweep does not stop a fan. A successful sweep records a conservative minimum and start duty. A curve must reach 100% by 85 °C or earlier; the software does not claim this is a universal safe operating limit for every component. Keep manufacturer firmware thermal protection enabled and choose lower thresholds where appropriate.

Stale control temperatures or failed RPM feedback request full cooling. Manual duty still has a temperature guard. Each curve source can independently trigger the full-cooling ceiling before mixing. Hysteresis and rate limits are bypassed for protective action.

The hardware service keeps operating when the desktop closes. On controlled service shutdown it restores only channels MK RGB actually took ownership of. Installation upgrades and uninstall stop the service before replacing/removing files. An OS freeze, power failure, inaccessible driver, or abrupt service termination cannot guarantee immediate firmware recovery on software-only controllers. Hardware-watchdog support is adapter-specific; no universal watchdog is assumed.

## Driver access

Use only upstream-supported signed drivers. MK RGB does not disable Memory Integrity, install test-signed drivers, or bypass security controls. If the operating system blocks a backend, the app reports missing capabilities and continues with available functions. OpenRGB's backend files are bundled; Windows may still require its supported kernel driver to be provisioned through its official installer.
