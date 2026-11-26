# Publication candidate verification, 2026-11-25

Candidate source: `e07392560983a2e0e9ebf8de7cd72c65c8d2da01`, clean working tree.
The build timestamp is 2026-11-26 UTC (2026-11-25 Pacific).

- Application, Core, and test sources match the seventh-pass candidate `e229fa313504`;
  this preparation changed history identities and documentation. The prior full gameplay
  results remain in [implementation status](../../implementation-status.md).
- Fresh Windows release export: staged smoke loaded the sample world, moved the walker,
  and exited with zero failures. See `staged-smoke.log`.
- ZIP: `FileScape-e07392560983-win-x64.zip`, 77,424,650 bytes, 205 entries.
- SHA-256: `8f0f3d0a9257c8ed9c5e9258d73909678486d564d9209145c3b9866c79426416`.
- All ZIP paths use forward slashes; no log or smoke directories ship.
- Packaged `LICENSE.txt` exactly matches the source MIT license naming David Jennings.
  Godot and .NET license and third-party notices are included, with a standalone README.
- Bundled runtime: Microsoft.NETCore.App 10.0.15 (`net10.0`).
- Extracted to a new empty Windows temp folder. The extracted executable's SHA-256
  matches the manifest. Launched that copy directly with the synthetic smoke arguments:
  sample composition and grounded movement passed; process exited 0. See `extracted-smoke.log`.
- No running user instance was closed or replaced. The candidate remains staged.

Still pending: ordinary Explorer double-click on a clean Windows profile, Windows Terminal
shutdown check, and DJ's publication approval. The automated extracted launch does not
establish Explorer association behavior or the terminal check. Known UI and intermittent
measurement limitations remain in the release notes. No tag or GitHub release was created;
the GitHub repository remains private.
