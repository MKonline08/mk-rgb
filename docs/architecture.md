# Architecture

`MkRgb.Core` defines versioned JSON contracts, effect evaluation, curves, automation, persistence, and extension interfaces. `MkRgb.Hardware` provides the LibreHardwareMonitor adapter, bounded named-pipe framing, and the OpenRGB SDK client. `MkRgb.Service` owns persistent cooling and the bundled RGB backend. `MkRgb.Desktop` owns WPF views, Skia canvases, session capture, profile automation, and local HTTP events.

The service's named pipe permits the installing user, local administrators, and SYSTEM. Requests are bounded to 4 MB and protocol version 1. External HTTP requests cannot issue fan writes. The OpenRGB SDK binds only to 127.0.0.1:6743. MK RGB negotiates SDK protocol 3, supported by the bundled 1.1 server, to obtain stable client-side identities and compatible controller descriptions. Device-list change notifications invalidate indexes before further frames are sent.

Settings and profiles use version 1 JSON. Atomic writes retain a .bak copy. Unknown versions are rejected rather than silently overwritten. Imported profiles receive new identities, virtual device bindings, and disabled cooling policies. SQLite telemetry uses WAL, bounded queries, and configurable retention (seven days by default).

## Local HTTP API

Enable in Automations. Default address: `http://127.0.0.1:19420`. No CORS permission is granted. Request bodies are limited to 64 KiB. Send `Authorization: Bearer <token>` for both public endpoints:

- `POST /v1/events` with `{"name":"notification","value":100}` publishes a numeric lighting/event input. Inputs expire after 15 seconds.
- `POST /v1/profiles/{id}` activates an existing profile, preserving manual cooling protection and service-side validation.
- `POST /v1/cs2` accepts CS2 GSI JSON with the generated `auth.token` field. Supported inputs are health, armor, flashed, burning, ammo, damage, and round_live.

Use **Save CS2 integration file** and place the generated file in CS2's `game/csgo/cfg` directory. Restart the game. The token is stored locally; diagnostic reports exclude it.

## Extensions

Public interfaces: `IRgbController`, `ISensorProvider`, `IFanController`, and `IEventSource`. Hardware adapters report explicit capabilities, stable IDs, timestamps, RPM mappings, and recovery support. A fan write is not inferred from an RPM reading.

Compiled extensions are trusted code, not sandboxed scripts. Only reviewed administrator-installed assemblies belong in the application's protected `extensions/<id>/` directory. A manifest declares id, name, version, assembly filename, author, license, and capabilities (`sensors`, `fans`, `rgb`). Permissions are review metadata, not a security sandbox. Declarative effect graphs cannot load assemblies or access the network/filesystem.

Profile graphs allow Color, Gradient, Time, Noise, Audio, Sensor, Event, Transform, Mask, and Blend. Node IDs must be unique, input arity must match the node type, and cycles/missing references are rejected. Graph depth, node count, profile size, LED count, and evaluation work are bounded.
