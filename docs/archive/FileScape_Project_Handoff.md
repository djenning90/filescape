# FileScape — comprehensive project and working-model handoff

Prepared November 3, 2026. Intended reader: the ChatGPT session connected to DJ's local FileScape folder. Read this as a continuation brief; the previous conversation is not required. Appendix A contains the historical research already collected.

## 1. Your role and how we work

You are ChatGPT, acting as DJ's technical manager, architecture and product-thinking partner, and reviewer. **Claude Code is the developer who investigates implementation details and writes the application code. DJ directs you and retains authority over product direction and consequential decisions.**

DJ describes this as a human outside the implementation loop managing the AI inside that loop: DJ → ChatGPT → Claude Code → implementation evidence → ChatGPT review → DJ's direction. Understand the responsibility split rather than taking the terminology literally: DJ remains the human owner throughout.

Your responsibilities:

- Help DJ imagine, design, prototype, and eventually realize FileScape.
- Turn broad goals into scoped, executable Claude Code assignments.
- Examine code, diffs, architecture, test results, and running prototypes when tools permit. Judge outcomes against explicit acceptance criteria.
- Challenge weak assumptions, detect scope drift, and recommend the next useful experiment.
- Manage follow-up fixes and keep durable project state current.
- Explain tradeoffs and ask DJ for decisions that affect the product; resolve routine engineering choices within the agreed scope.
- Distinguish your recommendation from an established fact or an approved decision.

Claude Code's responsibilities:

- Inspect the repository and relevant primary documentation.
- Perform bounded technical investigations, implement changes, and run appropriate checks.
- Report exact changes, commands, observed results, limitations, and blockers.
- Maintain implementation documentation and provide reproducible launch/build instructions.

**Do not silently become the primary application implementer.** Writing task briefs, reviewing files, running checks, and maintaining management documents fits your role. Delegate application implementation to Claude unless DJ asks you to implement it yourself.

Do not assume you can directly invoke or message Claude Code. First establish the available integration. If there is none, prepare self-contained prompts DJ can paste into Claude Code, then inspect the resulting local changes and reports. That is a valid initial workflow; building an orchestration system is not a prerequisite.

Use “we” for the engineering work. DJ is an experienced software engineer comfortable with C#, .NET, Python, Go, JavaScript/TypeScript, and technical detail. Be candid, concrete, and collaborative. For .NET unit tests, DJ prefers xUnit and Fluent Assertions, subject to actual project/tooling decisions.

## 2. Environment and current state

- **Accepted name:** FileScape.
- **Directory/repository slug:** `filescape`.
- **User-created directory:** `/home/david/src/filescape`.
- **Development environment:** Ubuntu under WSL on Windows.
- **Primary target:** a Windows desktop application that accesses the real Windows filesystem.
- DJ restarted ChatGPT and reports that folder access was verified in the receiving local chat. Nevertheless, verify your own actual working directory and capabilities before claiming access.
- This handoff was prepared in a separate cloud session. Its preparation did not write anything to DJ's WSL folder.
- No application code, engine setup, architecture, Git initialization, or GitHub repository creation was performed in the originating conversation. The local directory may now contain work; inspect it instead of assuming it is empty.
- No GitHub owner, product license, GPU specification, or final engine/language has been selected.
- Naming collisions, domain availability, and trademark availability have not been checked. This does not block a local prototype.

Keep development-host paths distinct from runtime-target paths. A Linux scan in WSL, a Windows executable opening `C:\...`, and Windows access to a WSL UNC path are different scenarios. Identify the simplest appropriate build/run/debug arrangement for the chosen stack. Do not relocate the user's folder merely because another layout might be convenient; explain any material tooling limitation first.

## 3. Origin and product vision

DJ saw SGI's **fsn**, pronounced “fusion,” in a video analyzing the computers shown in Jurassic Park. Its spatial filesystem navigation inspired an initial idea: recreate it on Windows, with real navigation, file opening/execution, and metadata inspection.

The idea then expanded. **FileScape should make a real filesystem into an explorable, beautiful 3D environment.** Classic FSN is an important reference and potential first visualization, but the long-term ambition includes forests, realistic cities, futuristic environments, and alien civilizations.

The environment could include atmospheric lighting, fog, reflections, weather, ambient effects, sound, and rich materials. These are meaningful aspirations, not a commitment to build every effect or environment in the first release.

This is intended to be useful as well as visually compelling. DJ wants to navigate actual files, inspect information, open documents, and execute programs through deliberate actions. Avoid drifting into either a static movie recreation or a generic disk-space chart that loses the spatial-world ambition.

### Candidate semantic mappings — examples, not approved requirements

| Filesystem meaning | Classic FSN | Forest | Futuristic city |
|---|---|---|---|
| Directory | Pedestal | Grove/clearing | District/platform |
| File | Box/tower | Tree/crystal | Building |
| Size | Height | Trunk/canopy dimensions | Building volume |
| Type | Color/marking | Species | Architectural style |
| Parent-child relation | Wire | Trail | Transit connection |
| Selection | Spotlight | Highlight | Holographic marker |
| Collapsed subtree | Summary geometry | Distant grove | District silhouette |

Make size mappings explicit: visual height, footprint, and volume are not interchangeable. If size is capped or transformed, show true numerical size in the inspector and explain the visual scale.

## 4. Decisions, preferences, and uncertainties

### Established direction

- The name is FileScape and the user-created folder is `/home/david/src/filescape`.
- Windows is the primary application target.
- The application should operate on real filesystem data.
- Investigate existing open-source work before duplicating it.
- Preserve the ambition for interchangeable immersive visualizations.
- Use the ChatGPT-manager / Claude-Code-developer working model.

### Strong preferences, not final commitments

- **C# is preferred:** DJ values expressiveness, managed memory, maintainability, and its performance. There is no demonstrated requirement for custom C++ just to draw FSN geometry.
- A capable game engine is attractive because cinematic environments have become central to the vision.
- Unreal merits serious evaluation, but choosing it entails its C++/Blueprint programming model and additional workflow complexity.

### Still open

- Unreal versus Godot .NET, Stride, desktop .NET with a 3D viewport, or extending an existing project.
- How closely the first visualization should reproduce original FSN layout, perspective, controls, and UI.
- Minimum GPU, target resolution, frame rate, dataset size, memory budget, startup time, and power usage.
- Initial navigation controls and degree of native Windows integration.
- Which file mutations belong in the first useful release.
- Whether theme packages are bundled, loaded separately, or eventually authored by third parties.
- Whether ChatGPT invokes Claude directly or DJ relays prompts initially.

**Earlier assistant recommendations evolved with the goal.** WPF/DirectX was suggested for a conventional utility. Unreal was later suggested for investigation after DJ described cinematic worlds. Neither is an approved final stack. Do not treat the most recent suggestion as a binding architecture decision.

## 5. Historical research and its limits

The originating session downloaded the surviving SGI distribution and extracted its 13-page manual and X resource defaults. The manual identifies **FSN 1.2**, printed March 22, 1996, requiring IRIX 5.3+. This postdates the 1993 film. It is a functional reference, not proof of exactly what the film version did.

Implemented behavior includes landscape navigation; directory warp mode; overview map; split views; bookmarks; height/color mappings; file opening/execution; a basic text viewer/editor; printing; file copy/move/link; staged deletions; indexing and caching; live directory monitoring; and preferences.

The manual also lists ideas that were unfinished. In particular, do not infer implemented functionality from broader patent descriptions. Appendix A retains the detailed inventory and uncertainties.

The original executable was not run in the research session. No candidate clone was built, benchmarked, or tested by us. Historical rendering behavior, search details, metadata behavior, and exact layout remain partly unverified.

Original sources:

- https://archive.irixnet.org/siliconsurf/free/cool_sw_01.html
- https://nekofiles.irixnet.org/General/fsn/
- https://nekofiles.irixnet.org/General/fsn/fsn.tar.Z
- https://nekofiles.irixnet.org/General/fsn/fsn.COFF.tar.Z
- https://patents.google.com/patent/US5528735A/en

The surviving distribution contains a binary, manual, defaults, icon, and Makefile, not ordinary application source. A patent refers to a software microfiche appendix, which was not obtained. Avoid describing the distribution as open-source application code.

The separate research file is `FSN_Requirements_Research.md`, Library ID `libfile_948b5633b23c819183b977b7b44abc09`. If attached or accessible, it is supplementary; this handoff includes its contents so access is not required to start.

## 6. Existing projects to assess

These are **prior-session research leads**, not freshly validated endorsements. Recheck repository availability, license files, activity, actual source, buildability, and functionality before choosing a foundation. Do not assume the original research's package versions or release claims remain current.

| Project | Prior research finding | Potential value |
|---|---|---|
| https://github.com/sp00nznet/fsvng | C++17 rewrite of FSV; SDL2/ImGui/OpenGL; advertised Windows release; LGPL family | Most promising previously found Windows candidate; assess before rewriting |
| https://github.com/thiekus/fsvwin | Older Windows FSV port; GTK+ 2; documented stability/build limitations | Windows porting and historical comparison |
| https://github.com/mcuelenaere/fsv | Archived FSV descendant; C/GTK/OpenGL | Layout, camera, animation, visualization reference |
| https://github.com/jtsiomb/fsnav | Smaller FSN-inspired navigator; GPL family | Navigation, picking, geometry, metadata ideas |
| https://codeberg.org/squidd/fsvng | Different project with the same name; author described Rust/Bevy and Linux-only runtime testing | Alternative implementation and large-tree lessons; repository was inaccessible to our web tool |
| https://github.com/unixmonkey/fsn_jurassic_park | Original-FSN configuration plus generated film-style tree | Reference scene and colors; not a replacement application |
| https://github.com/rafaehlers/fsn | Search surfaced a web reinterpretation, but direct retrieval later returned 404 | Unconfirmed availability; do not rely on it |
| https://github.com/kfarr/jurassic-file-navigator | WebVR experiment surfaced in search | Secondary lead only; not deeply inspected |

The C++ fsvng README claimed Windows Open, Properties, Explorer context menus, two visualization modes, and configuration persistence. Its roadmap said built-in move/rename/delete were out of scope and listed unfinished DPI, framing, and TreeV interaction work. Verify these claims directly. A modern FSV descendant may not resemble original FSN closely enough for our desired experience.

If evaluating source reuse, distinguish borrowing ideas, porting code, linking components, and forking a full application. Check actual file-level licenses and dependencies. An open-source product license for FileScape has not been chosen.

## 7. Architecture direction to investigate

Favor separable concerns without building an elaborate plugin platform prematurely:

1. **Filesystem model and operations:** node identity, hierarchy, metadata, enumeration, refresh, Windows actions, errors.
2. **Spatial layout:** positions, grouping, size transformations, subtree summarization, spatial stability.
3. **Visualization:** geometry/assets, materials, lighting, weather, labels, effects, transitions.
4. **Interaction:** selection, picking, search, camera/navigation history, bookmarks, opening files, inspection.

Distinguish:

- A **theme** changes atmosphere/materials/decorative content while retaining a representation/layout.
- A **visualization** changes the mapping and possibly the layout: classic FSN, forest, city, etc.

Keep file identity and selected context independent from rendered object identity. A visualization switch should ideally preserve the selected file/directory and meaningful navigation context. Whether identical spatial positions are possible across layouts is an open design question.

If choosing Unreal, evaluate a straightforward C++/Blueprint application against an optional C# core plus bridge. Do not introduce an extra service, IPC protocol, or unofficial language binding merely to preserve C# unless its value outweighs complexity.

If choosing .NET graphics, demonstrate the required custom perspective, instancing, labels, picking, and visual quality rather than assuming a generic 3D control is sufficient.

Avoid one heavyweight scene entity and one unique material per file. Investigate batching, instancing, visibility management, levels of detail, bounded labels, and compact data. Measure frame-time behavior during navigation and scanning, not just average FPS in a static tiny scene.

## 8. Candidate requirements and sequencing

Classify requirements as historical reference, accepted product direction, proposed MVP, or future experiment. Do not promote every item in Appendix A into an MVP obligation.

### Proposed first useful slice

- Choose a root folder; scan metadata asynchronously with progress and cancellation.
- Display a recognizable directory hierarchy and files in a navigable 3D scene.
- Select objects, show full paths and numerical metadata, and navigate to a directory or parent.
- Open a selected file with the intended host OS association through an explicit action.
- Provide a reset/recovery view and keep the UI responsive while scanning.
- Support a small deterministic synthetic dataset and at least one real directory tree.
- Handle inaccessible entries without crashing or freezing.

### Visual proof of concept

- Start with classic FSN-inspired geometry to test data mapping and navigation.
- Add one bounded cinematic treatment, such as a futuristic district with compelling materials/lighting and configurable fog/reflections where available.
- Reuse the same filesystem data and selection model.
- Prioritize demonstrating atmosphere and usability together over creating an entire production-quality city or asset marketplace.
- Clearly distinguish live metadata from sample data and mock interactions from implemented OS actions.

### Later capabilities to evaluate

Search, overview/minimap, bookmarks, multiple views, live refresh, cached scans, rich previews, copy/move/rename/delete, shell context menus, additional visualizations, downloadable themes, and content authoring tools. These are candidates for later scope, not automatic deliverables.

### Filesystem details requiring explicit policies

Junctions/symlinks/reparse points and cycles; drive and network-share boundaries; long Unicode paths; hidden/system files; inaccessible or disappearing entries; sparse/compressed/cloud-backed files; logical versus allocated size; hard-link double counting; watcher event loss; collisions and partial failures in mutations.

An initial read-only metadata scan plus deliberate file opening is a sensible proposed prototype boundary. Do not scan entire drives or upload private filesystem listings as a default test fixture. Use synthetic/open-source fixtures for shareable evidence.

### Visual usability principles — proposed

- Readable names and clear hierarchy despite rich scenery.
- Search/inspection can reduce visual obstruction from atmosphere.
- Stable positions during incremental updates where practical.
- Quality settings so cinematic ambition does not require maximum effects on every machine.
- No automatic execution merely from hovering, previewing, or navigating.

## 9. How ChatGPT should manage Claude assignments

Use small outcome-focused tasks. Every substantial assignment should specify:

1. Goal and why it matters.
2. Current known state and files to read.
3. Scope and exclusions.
4. Decisions already made versus open choices.
5. Deliverables and observable acceptance criteria.
6. Validation commands or manual checks appropriate to the change.
7. Required completion report: changed files, results, evidence, limitations, and next recommendation.

After Claude completes a task:

- Inspect the actual work rather than accepting the summary alone.
- Run/review relevant checks. For visual work, examine screenshots/video or the running application when available.
- Distinguish code written, build passed, tests passed, launched, visually observed, and benchmarked. None implies all the others.
- Give Claude a concrete correction brief for material issues.
- Summarize what DJ can try and what product decision is needed next.

Keep implementation ownership with Claude; do not make simultaneous application edits in the same files. For true parallel development, assign separate files/branches/worktrees and define integration ownership. In the initial workflow, ChatGPT reviewing while Claude implements is enough. Inspect Git state before any commit-related action, preserve unrelated work, and avoid resetting or cleaning another agent's changes.

Maintain concise durable documents when useful: README for launch/status; product brief; requirements with status; architecture decisions; task list; and session handoff. Avoid a large empty folder hierarchy or ceremonial process. Proposed names are suggestions, not mandates.

## 10. First actions for the receiving ChatGPT session

1. Read this entire handoff, then verify `/home/david/src/filescape`, existing files, local instructions, Git state, and your actual read/write/command capabilities.
2. Confirm the role split briefly. Do not restart a generic brainstorming interview or ask DJ to repeat the vision.
3. Establish whether Claude Code is available through an authorized local workflow or whether DJ will relay prompts. Do not claim to have contacted Claude without doing so.
4. Inventory readily available build/runtime tools and the Windows/GPU environment enough to design the first experiment. Do not install large engines as an incidental step.
5. Prepare a bounded Claude investigation brief using Section 11. It should produce a requirements/architecture recommendation and prototype plan, not begin a large implementation blindly.
6. Review the findings and recommend the next concrete prototype. Ask only for unresolved choices with material impact. Routine investigations and reversible documentation work should proceed within the authorized scope.

## 11. Ready-to-adapt first Claude Code assignment

The manager should adapt this to the observed repository and available tools before issuing it. This is a starter assignment, not evidence that Claude has received it.

---

You are the implementation developer for **FileScape**, working with DJ and ChatGPT as technical manager/reviewer. Work in `/home/david/src/filescape`. First read the project handoff, existing instructions, and any existing code. Preserve current work.

FileScape is a Windows filesystem explorer inspired by SGI FSN, expanded into immersive interchangeable visualizations: classic FSN, cities, forests, and potentially alien environments. It must remain useful for navigating actual files, inspecting metadata, and opening files. C# is preferred, but cinematic visual quality makes Unreal worth serious evaluation. No engine is selected.

For this assignment, perform a focused feasibility investigation and propose a prototype architecture. Do not build a complete application or install a large engine without identifying the requirement and coordinating with the manager.

Deliver:

1. An environment assessment: WSL/Windows boundary, installed relevant tools, available GPU information where accessible, and practical Windows build/run workflow. State what you could not inspect.
2. A verified assessment of the most relevant existing clones, especially `sp00nznet/fsvng`, enough to choose fork/improve, reuse selected components, or build anew. Check actual source/licenses and list unverified claims.
3. A concise stack comparison centered on Unreal C++/Blueprints versus C#-friendly alternatives. Include visual capability, filesystem integration, iteration speed, deployment, maintainability, asset/theme authoring, and integration complexity. Consider .NET desktop rendering only if it plausibly meets the expanded visual goal.
4. Proposed requirements split into initial prototype, useful MVP, and later ambitions. Separate original FSN facts from proposed FileScape behavior.
5. A minimal architecture separating filesystem data, layout, visualization, and interaction. Explain theme versus visualization and how identity/selection survives changes. Do not overbuild a plugin system.
6. The smallest runnable prototype plan that tests real filesystem navigation and a convincing visual treatment. Define measurable acceptance criteria, datasets, hardware assumptions, and an evidence plan. Any numerical performance thresholds are proposed targets until measured/agreed.
7. A recommendation with rationale, strongest alternative, unresolved risks, and the next implementation task.

Use primary documentation for factual technical claims and record source URLs. Report precisely what you read, built, ran, or measured. If a small non-invasive spike is needed to resolve a specific uncertainty, scope it explicitly and keep it isolated. Do not present a static mockup or invented benchmark as a working prototype. Do not commit/publish or contact third parties as part of this investigation.

Return a concise completion summary plus durable findings in the repository. ChatGPT will review your evidence and prepare the next implementation brief with DJ.

---

## 12. Evidence references for engine evaluation

Previously consulted official documentation; fetch current versions when making a decision:

- Unreal Lumen: https://dev.epicgames.com/documentation/unreal-engine/lumen-global-illumination-and-reflections-in-unreal-engine
- Unreal fog: https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-fog-in-unreal-engine
- Unreal instancing: https://dev.epicgames.com/documentation/unreal-engine/instanced-static-mesh-component-in-unreal-engine
- Unreal programming: https://dev.epicgames.com/documentation/unreal-engine/coding-in-unreal-engine-blueprint-vs-cplusplus
- Unreal packaging: https://dev.epicgames.com/documentation/unreal-engine/packaging-your-project
- Unreal licensing: https://www.unrealengine.com/license
- Godot C#: https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/index.html
- Godot instancing: https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html
- Stride: https://github.com/stride3d/stride
- Stride FAQ: https://www.stride3d.net/faq/
- Helix Toolkit: https://github.com/helix-toolkit/helix-toolkit
- .NET GC latency: https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/latency

No engine was installed or benchmarked in the originating session. No final license/royalty analysis was completed. These sources support evaluation; they do not select the stack for us.

## Appendix A — historical requirements research

The following is the previously saved research document, read from its current saved version when preparing this handoff. Its historical title mentions a C# clone; the broader FileScape vision and unsettled technology choice described above supersede that framing. The original inventory remains reference material, not an approved product backlog.


# SGI FSN: requirements research for a C# Windows clone

Researched November 3, 2026. This is a documented baseline, not a claim of complete behavioral equivalence. No original executable was run in this research.

## Evidence and version scope

The application is **fsn**, pronounced **fusion**, short for File System Navigator. SGI identifies it as an experimental information-landscape navigator and confirms its appearance in Jurassic Park. It was not a complete replacement for SGI's conventional file manager.

Primary implementation evidence: the `fsn.z` manual and `Fsn` X resource defaults extracted from the surviving `fsn.tar.Z` distribution. The manual identifies **FSN 1.2**, requires IRIX 5.3+, and bears a printed date of March 22, 1996. It says 1.2 added features including search. Therefore this is a later surviving implementation baseline, not proof of exact behavior in the 1993 film.

Sources:

- Original SGI overview, archived: https://archive.irixnet.org/siliconsurf/free/cool_sw_01.html
- SGI distribution notes and downloads: https://nekofiles.irixnet.org/General/fsn/
- Examined archive: https://nekofiles.irixnet.org/General/fsn/fsn.tar.Z
- Older COFF build, not examined: https://nekofiles.irixnet.org/General/fsn/fsn.COFF.tar.Z
- SGI inventors' detailed design disclosure, including diagrams: https://patents.google.com/patent/US5528735A/en
- FSV source, a separate descendant: https://github.com/mcuelenaere/fsv
- Independent FSN-inspired implementation: https://github.com/jtsiomb/fsnav
- Film-style filesystem and configuration reconstruction: https://github.com/unixmonkey/fsn_jurassic_park

The archive contains a binary, manual, defaults, icon, and installation Makefile; it does not contain ordinary application source files. The patent references a software microfiche appendix; that appendix was not retrieved or assessed.

## Implemented feature inventory

All rows below are grounded in the shipped manual unless marked otherwise. They are paraphrased requirements candidates, not copied manual text.

| ID | Area | Documented behavior |
|---|---|---|
| VIS-01 | Landscape | Present the directory hierarchy as pedestals linked by parent-child wires, with file blocks on each pedestal. |
| VIS-02 | Data encoding | Encode file size as height and file age as color; encode directory size as pedestal height. |
| VIS-03 | Scaling | Support minimum-height, linear, and exaggerated height modes. Exaggerated height uses a configurable exponent, default 0.5. |
| VIS-04 | Directory totals | Support flat directories, direct-file totals, and totals including descendant files. |
| VIS-05 | Selection | Highlight hovered objects and show their names; brighten selected directory and connector; spotlight selected files. |
| VIS-06 | Occlusion | Optionally shrink a selected file on approach while retaining a pole indicating its original height. |
| VIS-07 | Perspective | Apply extra horizontal narrowing to distant geometry beyond ordinary perspective; expose tuning parameters. |
| VIS-08 | Themes | Provide grass, ocean, desert, space, and indigo landscapes; configurable ground/sky colors and optional Gouraud shading. |
| NAV-01 | Selection travel | Left click selects and approaches a file or directory; Shift-left selects without travel. |
| NAV-02 | Connectors | Clicking a directory connector selects it; when already selected, clicking its connector approaches its parent. |
| NAV-03 | Free travel | Middle-button displacement from an initial crosshair controls travel direction and velocity; larger displacement means faster movement. |
| NAV-04 | Vertical travel | Shift-middle combines forward/downward or backward/upward travel. |
| NAV-05 | Camera presets | Reset, go back through prior moves/zooms, bird's-eye view, front view, tilt, and height controls. |
| NAV-06 | Warp mode | Double-click directory to show just that directory, child-directory buttons, parent button, and previous/next sibling buttons. Click outside to leave. |
| NAV-07 | Row travel | Selection at a file-row boundary can show an arrow to the adjoining row's first/last file. |
| NAV-08 | Overview | Optional overview shows directory topology and camera position; supports selecting or approaching locations. |
| NAV-09 | Split views | Two independently navigable views can use different modes; overview shows both camera positions in different colors. |
| NAV-10 | Marks | Named persistent bookmarks preserve position and selected objects; create, visit, and delete marks. |
| FILE-01 | Open/execute | Double-click or Open performs the appropriate OS launch action; executable files can run. |
| FILE-02 | Text viewer | Built-in text viewer has limited editing, Save, Done, and launch-editor functionality; Alt-double-click bypasses viewer for source files. |
| FILE-03 | Print | Print through the host desktop's printing mechanism. |
| FILE-04 | Transfer | Move, copy, and link a selected file to a directory selected in the other split view. |
| FILE-05 | Delete | Toggle pending deletion; show pending files as wireframes; separately process or cancel all queued deletions. |
| FILE-06 | Context menu | Right-button file menu acts on the selected file, even if a different file is hovered. |
| IDX-01 | Root | Accept one starting directory; default to working directory. |
| IDX-02 | Index | Initial recursive scan followed by persistent cached database in the user's home directory. |
| IDX-03 | Boundaries | Do not cross filesystem-type boundaries; do cross mount points of the same filesystem type. |
| IDX-04 | Refresh | Rescan all or selected subtree; application remains usable during rescanning. |
| IDX-05 | Live changes | Rescan and monitor selected directory; allow persistent directory/subtree monitoring and stopping it. |
| IDX-06 | Monitoring policy | Optional inheritance of monitoring to new child directories; temporary monitoring expires after deselection timeout. |
| IDX-07 | Prune | Remove descendants from the visualization database, without deleting filesystem contents; unpruning can take time. |
| IDX-08 | Persistence | Save database manually, periodically (15 minutes), and at exit; preserve camera position, window visibility, marks, and display choices. |
| UI-01 | Auxiliary panels | Show/hide Overview, Controls, age Legend, and Preferences. |
| UI-02 | Help | Window help, context help, and F1 help for hovered widgets. |
| UI-03 | Path/search | Version 1.2 announces search; text field supports full-path entry and first completion. Exact search matching and UI behavior need runtime inspection. |

## Confirmed configuration details

The shipped defaults contain seven age colors and six age cutoffs: 7, 14, 31, 92, 182, and 365. Legend labels are one week, two weeks, one month, three months, six months, one year, and over one year. Precise cutoff inclusivity and which filesystem timestamp defines age still need confirmation.

Documented defaults include mouse sensitivity 0.7; maximum file height 4; maximum directory height 2; exponent 0.5; temporary monitoring timeout 120 seconds; built-in viewer enabled; shrink-on-zoom enabled; transfer beaming animation enabled; warp flash disabled; monitor inheritance disabled; and database compression enabled.

The defaults also expose geometry spacing, camera positions, file-label limits, distance thresholds for simplified geometry, lighting factors, and key translations. These parameter names suggest implementation mechanisms, but exact visual behavior must be observed rather than inferred from names alone. The defaults bind Alt+R to reset, Alt+B to back, Alt+S to split, Alt+O to show overview, and Alt+C to show controls. Ctrl-middle has an additional translation whose semantics were not established.

## Explicit gaps and unimplemented ideas

The manual's wish list includes multi-selection; multiple top-level roots; more flexible meanings for height/color; configurable age thresholds and color count; local-only file-size normalization in warp mode; listing all path completions; relative paths and dot/dot-dot handling; more than two views or detachable views; and attribute queries with highlighted results and sequential visits.

It also proposes file-type separation and user-controlled warp layouts as future possibilities. Do not treat these as implemented features simply because a patent describes a broader embodiment. Conversely, the shipped defaults already contain age-threshold resources; whether those are effective, supported configuration overrides needs testing.

Version 1.2 explicitly says Spaceball support was removed. General file Properties, rename, directory creation, drag/drop, robust undo, and comprehensive search behavior were not established by the examined manual. Absence from the manual is not proof that an undocumented behavior never existed.

## Proposed Windows requirements — additions, not historical claims

1. Launch files with Windows associations; separately expose Run, Open With, and Properties as appropriate.
2. Define drive, UNC share, junction, symlink, and reparse-point behavior; detect traversal cycles and inaccessible paths.
3. Use cancellable background scanning and incremental scene updates; handle enumeration failures without losing navigation.
4. Reconcile filesystem watcher notifications with periodic/explicit rescans; do not assume events are complete.
5. Define size semantics for sparse/compressed files, hard links, cloud placeholders, and direct versus recursive totals.
6. Support long Unicode paths and Windows case behavior. Specify how stale selections and renamed/deleted bookmarks resolve.
7. Define copy/move/link collision rules, cross-volume behavior, progress, cancellation, and partial-failure reporting. Choose explicitly between hard links, symbolic links, and shortcuts.
8. Map deletion to Recycle Bin by default while retaining optional FSN-style staging; distinguish canceling a queued deletion from undoing a completed operation.
9. Add the requested metadata inspector: path, type, logical size, timestamps, attributes, and optional allocated size/security information.
10. Preserve spatial stability during updates. Set measurable performance goals using an agreed test corpus; none are established yet.
11. Provide keyboard and ordinary Windows mouse alternatives alongside faithful three-button controls.

These choices do not yet require committing to a C# UI or rendering framework.

## Remaining research before calling this a faithful clone specification

- Run original FSN under IRIX and capture every menu, panel, context-help page, preference, and viewer state.
- Inspect the older COFF build and compare it with 1.2; define whether the target is film-era appearance or later functionality.
- Measure directory layout/order, file-grid placement, scaling/normalization, artificial perspective, camera easing, picking, and label visibility.
- Verify search, path completion, age calculation, link handling, selection history, and transfer failures.
- Confirm whether metadata beyond hover names is exposed anywhere in the executable.
- Treat film-style configuration as an optional visual preset, not evidence of the original product's default state.

Suggested project split: a historically faithful navigation/rendering layer, a Windows filesystem/action layer, and optional usability enhancements. Initial milestone: real read-only directory scanning, geometry, selection, camera travel, warp mode, overview, and OS file opening. Add monitoring, persistence, split views, and mutations after spatial behavior is solid.
