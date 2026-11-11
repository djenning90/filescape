# Evidence: the planetary FileScape (2026-11-11)

All runs on DJ's machine (Lenovo 21RX, Radeon 890M iGPU, Windows 11), at 1600×900 and UI scale 1.0 unless noted.
Only owned data: the app's own Starter Sky (`%APPDATA%\Godot\app_userdata\FileScape\samples\Starter Sky`), the
Wayfarer fixture (`%TEMP%\FileScape-fixtures\walk-…`) and the WSL fixture (`/tmp/filescape-wsl-fixtures/wsl-…`).

| Folder | What | Build |
|---|---|---|
| `gallery/` | the art pass: every planet theme, file object, landmark and the traveller (`--experiment=planet-gallery`) | art branch |
| `journey/` | the first minutes on the Starter Sky (`--automate=planets-journey`): opening shot, arrival, overlook, launcher, landing, picture preview, Places, return, piles, the list, Quiet Moon, workshop, Music Box; `planets-journey.log` | staged `63dfed091e8b` (full tier) |
| `fixture/` | the same system on the Wayfarer fixture and a WSL folder (`--automate=planets-fixture`) | staged `88899d32ccf3` |
| `checks/` | verification tier summaries | see each |

The recorded reel (`--automate=planets-reel --write-movie … --fixed-fps 30`, 1280×720, with its sound) is kept outside
the repository for size: `%LOCALAPPDATA%\FileScape\evidence\2026-11-11-planets\planets-reel.avi`.
