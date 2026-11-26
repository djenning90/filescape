# FileScape first public playtest release notes draft

Explore your files as little worlds you can run around. Launch between folders, visit a project workbench, look
through a telescope, and walk among photographs while each place plays its own music.

This first public playtest invites you to help shape a useful and playful way to inhabit your computer.

## Start playing

Download the Windows x64 ZIP attached to the release, extract the whole folder, and open FileScape.exe.
Choose Learn to play for the guided Starter Sky tour. Press F1 for controls and use the sample world before
exploring your own drives and WSL folders.

The Windows package includes the .NET 10 runtime. The executable is unsigned, so Windows may show a SmartScreen
prompt. Compare the download's SHA-256 with the value on the release page.

## Things to try

- Fly between folders and return through their launchers.
- Open a software project and use its workbench to find the README and useful entry points.
- Explore Pictures as a gallery and open a larger viewer to browse images.
- Press T to explore the surrounding hierarchy as star systems.
- Visit a Hazard World and try a jump, twirl, and rim recovery.
- Listen to the original synthesized music and try ringing the Music Box bell.

## Share your first session

What made you want to keep exploring? Where did you first get stuck?
[Open an issue](https://github.com/djenning90/filescape/issues) or read the
[contributor guide](../CONTRIBUTING.md) to help with code, design, documentation, or music.

## Known limitations

At 1280 by 720 with UI scale 1.5, the toolbar and preview can overlap other content.
The first arrival may pause briefly while the world is composed.
Some automated jump measurements have failed intermittently and passed on rerun.
The final Windows Terminal shutdown and ordinary Explorer launch checks remain pending.

Cloud-only pictures may download when Windows has no cached thumbnail. FileScape bounds automatic downloads.
Native Linux and macOS builds are future work.

## Release preparation

Before publishing, replace this section with the build ID, source commit, ZIP name, SHA-256, and the verification
results for the actual release. Finish the pending desktop checks and refresh the limitations.
Source and release documentation use the MIT License, copyright 2026 David Jennings.
