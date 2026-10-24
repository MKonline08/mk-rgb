# Contributing

Build and run tests using the pinned SDK. Changes to cooling require stale-data, ceiling, failure-recovery, and mapping tests. Do not add a verified device entry without recording the exact model, controller, firmware, OS, backend version, and exercised actions.

For catalog submissions, add a version-1 profile under `catalog/profiles/` and an index record with a SHA-256 checksum. Include an original description and tags. Submissions may not include credentials, machine paths, personal identifiers, or executable effect scripts. Use the simulator for effect development; label previews as virtual.

For hardware extensions, implement the public contracts, keep device identifiers stable, distinguish read/write capabilities, and document default-control recovery. Assemblies are privileged trusted code and require review and administrator installation. Do not download and execute unreviewed binaries automatically.

Run the Windows workflow and keep lockfiles updated. Add focused regression tests for behavior changes. A successful simulator test does not establish hardware compatibility.
