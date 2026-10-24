# Validation record

MK RGB 0.1.0 is a **development preview**, not a completed acceptance release. This record separates implemented code from evidence collected on physical hardware. No claim of universal motherboard or component support is made.

## Checks recorded locally on 2026-10-24

- Windows x64 Release restore, build, and automated tests. Test results are exported as TRX and published by CI.
- Effect evaluation for every built-in effect, timeline interpolation/hold, graph cycle/reference checks, LED overrides, curve interpolation/mixing, hysteresis, ramp limits, stale and future sensor fallback, pump minimums, validated stop/restart ramping, automation priorities, overnight schedules, undo/redo, atomic backup, unsupported settings versions, profile import isolation, SQLite history/CSV, bounded pipe framing, and synthetic OpenRGB parsing.
- HTTP integration: token enforcement, malformed JSON, CS2 fields, profile activation, missing profiles, and absence of fan-write endpoints.
- WPF app launched on the reference PC. Live discovery reported ASUS TUF GAMING B650-PLUS WIFI, AMD Ryzen 5 9600X, NVIDIA GeForce RTX 3060 Ti, integrated Radeon graphics, and memory devices. NVIDIA temperature was readable (approximately 41–43 °C during observation). CPU temperature was unavailable without privileged driver access and was correctly displayed as unavailable. These observations do not validate fan or RGB control.
- Overview, Lighting/virtual-device layout, Studio, and Monitoring have received visual inspection. Additional workflows and scaling checks remain open below.
- Self-contained MSI and portable ZIP packaging succeeded locally. CI runs installation, read-only service IPC, repair, service termination/recovery, and uninstall on a disposable Windows runner. Consult the actual workflow result before treating those checks as passed.

## Acceptance work still required

| Area | Remaining evidence |
| --- | --- |
| Physical RGB | Enumerate connected controllers and LEDs; validate identity, channel resizing, color order, output, reconnect, and hot-plug on each controller. |
| Physical cooling | Identify the actual fan/pump and RPM/temperature mappings, commission each channel, confirm stop/restart where applicable, stall and stale-input behavior, firmware restoration and watchdogs. No fan writes have been validated on the reference PC. |
| Session capture | Validate real audio capture, screen/window capture, resize and disconnect, and CS2 in-game GSI. Automated HTTP fixtures are not in-game evidence. |
| UI | Complete every workflow at 100%, 150%, and 200% scaling, keyboard-only operation, reduced motion, and disconnected transitions. |
| Installer | Clean Windows 11 VM, major-version upgrade, rollback/interrupted update, reboot and sleep/resume. Hosted runner installation is useful but does not replace a clean reference VM. |
| Extensions | Review and test third-party adapters before adding them to the catalog. No physical hardware extensions are currently certified. |
| Performance | Measure idle and 30/60 FPS output overhead with actual connected hardware. |
| Distribution | Verify corresponding source and redistribution provenance for every native backend dependency before a stable release. |

Virtual devices, generated frames, protocol fixtures, and unit tests are explicitly simulator/software evidence. They never populate real hardware temperature readings or certify a physical controller. Software-only fan controllers cannot guarantee immediate recovery during an OS freeze.
