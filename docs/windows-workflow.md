# Windows build, run and test workflow

The source of truth is this WSL repository (`/home/david/src/filescape`). Windows tools operate on it directly through `\\wsl.localhost\Ubuntu-24.04\home\david\src\filescape`. There is no staging copy, so there are no sync rules.

## Installed tools (user-local; nothing system-wide)

| Tool | Version | Location | Source / verification |
|---|---|---|---|
| Godot .NET editor (portable, self-contained mode via `_sc_`) | 4.7.2-stable mono (`4.7.2.stable.mono.official.ed1daf0bf`) | `C:\Users\David\tools\godot\4.7.2-stable-mono\Godot_v4.7.2-stable_mono_win64\` | https://github.com/godotengine/godot/releases/tag/4.7.2-stable, `Godot_v4.7.2-stable_mono_win64.zip`, SHA-512 `79229fd1…367757` (matched `SHA512-SUMS.txt`) |
| Godot .NET export templates (Windows x86_64 only extracted) | 4.7.2.stable.mono | `…\Godot_v4.7.2-stable_mono_win64\editor_data\export_templates\4.7.2.stable.mono\` | `Godot_v4.7.2-stable_mono_export_templates.tpz`, SHA-512 `bb5c41d7…ff6bd5a` (matched); the full .tpz is kept alongside |
| .NET SDK (pre-existing) | Windows 10.0.403 (runtimes 8.0.33, 10.0.14); WSL 10.0.400 | system | not installed by this work |

Project packages (restored from nuget.org): `Godot.NET.Sdk` 4.7.2 (MIT), xunit 2.9.3 (Apache-2.0), xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.0.1 (MIT), FluentAssertions 7.2.0 (Apache-2.0; see AD-10). Godot exports also bundle GodotSharp (MIT).

To remove everything: delete `C:\Users\David\tools\godot` and `%LOCALAPPDATA%\FileScape`. Godot writes run logs to `%APPDATA%\Godot\app_userdata\FileScape\logs`.

## Commands (from WSL)

```
tools/fs.sh build        # Windows dotnet build of app + Core (fails loudly on errors)
tools/fs.sh test         # full test suite with Windows dotnet.exe (includes Windows-only tests)
tools/fs.sh test-wsl     # Linux SDK; the 10 Windows-only tests skip with a reason
tools/fs.sh run -- --root='C:\some\folder'      # run from the project via the editor binary
tools/fs.sh editor       # open the Godot editor on app/
tools/fs.sh export       # standalone build → %LOCALAPPDATA%\FileScape\build\FileScape.exe (+ data_FileScape.App_windows_x86_64\)
```

App arguments go after `--`:
`--root=PATH`, `--synthetic=N`, `--synthetic-rate=N/s`, `--vis=classic|district`, `--quality=high|low`, `--window=WxH`, `--ui-scale=F`, `--automate=journey|perf|scanperf|live|qualitystress --out=DIR [--open-test=NAME]`.
Engine arguments go before `--`, for example `--rendering-driver d3d12`.

Fixture tool (Windows): `dotnet src\FileScape.Fixtures\bin.win\Debug\net8.0\FileScape.Fixtures.dll create|delete <dir>|delete-all`. It only creates under `%TEMP%\FileScape-fixtures` by default, and refuses to delete anything without its marker file.

## Quirks found and how they are handled

1. **Shared tree, two hosts.** WSL and Windows builds would clobber each other's `obj/project.assets.json` (different absolute paths). `Directory.Build.props` sends Windows builds to `obj.win/` and `bin.win/`, and excludes both hosts' generated sources from compilation.
2. **Hung pipes.** `Godot --build-solutions` and `dotnet build` with node reuse can leave build-server processes holding the PowerShell pipe open, so WSL waits forever. `fs.sh build` uses `dotnet build -nodeReuse:false /p:UseSharedCompilation=false`.
3. **apphost copy denied.** MSBuild intermittently got "access denied" copying `apphost.exe` to `bin.win\…\FileScape.Fixtures.exe` on the UNC share, while a plain PowerShell copy of an .exe to the same folder worked. Root cause not established. Mitigation: `UseAppHost=false` for the fixtures tool.
4. **Export needs a solution file named after the assembly** (`app/FileScape.App.sln`). Without it, Godot exported a bare exe with no .NET assemblies, which then crashed on launch (0xC0000005).
5. **Vulkan device loss (KD-01).** D3D12 is the project default (AD-11). `tools/compare-drivers.sh` repeats the Vulkan/D3D12 stability comparison on the exported build.
