# Camera-only README photography

Captured November 25, 2026 (Pacific) from release source
`e07392560983a2e0e9ebf8de7cd72c65c8d2da01` in an isolated detached worktree.
The two patches are alternative capture harnesses against that source, not cumulative.
They were built with `tools/fs.sh build` and run with `tools/fs.sh run --root=sample
--vis=planets --automate=planets-photography --test-shell=record --quality=high
--render-scale=1 --window=1920x1080 --ui-scale=1 --out=OUTPUT`.

The patches add only a temporary capture route, camera placement, and CanvasLayer hiding.
The second harness positions the Overview camera just outside Constellations' ring, looking
past nearby bodies toward its centre. These are engine-rendered, full-frame screenshots;
world geometry, composition, art, lighting, sky, shaders, and file data are unchanged.
They are photography views, not evidence that a HUD toggle ships in the released game.
No pixel editing, image generation, or compositing was used.

Both capture runs ended with zero failures. No full gameplay suite was run: the application
changes existed only in the disposable capture worktree and are not part of the shipping build.
The original project README and Hazard World gameplay shots remain from the released executable.
See [media provenance](../../media/README.md) for the selected files and their hashes.
