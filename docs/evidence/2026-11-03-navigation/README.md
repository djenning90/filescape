# Navigation evidence, 2026-11-03

| File | What | Input path |
|---|---|---|
| `baseline.txt`, `baseline-after-W.png` | The old export before changes: W worked, arrows unbound, stuck drag after Alt-Tab | Real OS input (SendInput) |
| `navigation.log` | 56 PASS checks on the new export: aliases, diagonals, speeds, fly, look, pitch limits, transitions, Escape, toolbar, text-field focus, dialog, focus loss, Home, stale layout | Injected InputEvents through Godot's pipeline (set-up exceptions labelled in the log) |
| `os-probe-results.txt` | W/arrows, W+Up, right-hold fly, look, wheel, Alt-Tab mid-flight, Escape, middle-drag pan | Real OS input (SendInput + real Alt-Tab), judged from `--state-file` |
| `journey-regression.log` | The earlier synthetic journey on the new build (0 failures) | Mostly direct method calls (see status doc) |
| `args.log` | `tools/fs.sh run` preserves arguments with spaces and an apostrophe | WSL interop |
| `classic-home-with-hint.png` | The first-run hint and the Help (F1) button | Screenshot |
