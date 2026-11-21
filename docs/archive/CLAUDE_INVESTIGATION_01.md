# Investigation 01: choose the first FileScape experiment

Status: prepared for Claude Code; not dispatched. Prepared November 3, 2026.

## Goal and working model

Work in `/home/david/src/filescape`. Read **all** of `FileScape_Project_Handoff.md`, including Appendix A, and any applicable instructions before working. DJ owns product direction; ChatGPT manages scope, architecture/design discussions, and review; you investigate and implement. Preserve existing work.

FileScape is a Windows desktop explorer of real filesystem data through immersive, interchangeable visualizations. Classic FSN is a reference and likely first geometry experiment; cities, forests, and other worlds are longer-term ambitions. Navigation, readable hierarchy, metadata inspection, and deliberate OS file opening must remain useful within the scenery.

This task produces evidence and a prototype recommendation, not an application implementation. Keep engine and language choices open. C# is a preference, not a selection. Do not install engines, SDKs, dependencies, or assets, initialize Git, commit, publish, or contact third parties. Do not change the handoff or relocate the workspace. Source inspection may use isolated temporary downloads; record what was inspected and retain no private filesystem listings in reports.

## Verified starting point

Manager checks on November 3, 2026:

- WSL2 Ubuntu environment; Windows runtime target remains distinct from this Linux development host.
- Folder initially contained only the handoff; no applicable ancestor `AGENTS.md` found. This assignment is a subsequent management document. At DJ's direction, the manager then initialized Git in this folder on branch `main`; no commits or remote were created. Preserve this repository and inspect its current status before working.
- Read/write/command access verified. Normal manager shell launch failed because `bwrap` was missing; approved direct commands worked. This is a manager tooling observation, not proof your shell has the same limitation.
- `claude --version`: Claude Code 2.1.317. Invocation/authentication for a development task has not been tested; no Claude task has been launched.
- WSL .NET SDK: 10.0.400. Git, Node, Python, Windows PowerShell and cmd are reachable.
- Godot, UnrealEditor, CMake, g++, and nvidia-smi were not found on the WSL PATH. This does not establish whether Windows engines or compilers are installed.
- Windows CIM reports Windows 11 Pro, version 10.0.26200; AMD Radeon(TM) 890M Graphics, driver 32.0.22024.19001. Dedicated/shared memory, rendering capability, and application performance remain unmeasured.

Recheck only what is needed and report differences. Establish practical Windows build/run/debug routes from the existing WSL folder, including how the Windows application accesses Windows paths. Do not use WSL scan results as evidence of Windows filesystem integration.

## Bounded investigation

1. **Environment:** inspect available Windows/.NET/compiler/engine tools without installing anything. Identify a feasible workflow and its limitations; determine enough hardware information to propose experiments, without inventing GPU performance conclusions.
2. **Existing work:** deeply inspect `https://github.com/sp00nznet/fsvng`: actual source organization, layout/rendering and filesystem model, Windows actions, build instructions/dependencies, license files and relevant file headers, activity, and how much is reusable for immersive visualizations. Inspect `https://github.com/jtsiomb/fsnav` and `https://github.com/mcuelenaere/fsv` as bounded references. Pursue other handoff leads only if they could materially alter the recommendation. Separate README claims from source findings and runtime observations. Explain fork, selective code reuse, and independent implementation as distinct options; flag licensing questions without selecting FileScape's license.
3. **Stack comparison:** evaluate Unreal C++/Blueprints, Godot .NET, and Stride using current official documentation. Include a desktop .NET viewport only if credible for the immersive goal. Compare visual treatment, instancing/picking/labels, async scanning, Windows shell integration, iteration and deployment, asset authoring, maintainability, licensing constraints, and WSL/Windows friction. Evaluate Unreal directly before proposing any optional C# bridge; extra processes/bindings require demonstrated benefit. Clearly label facts, inferences, unknowns, and preferences.
4. **Requirements and architecture:** classify historical reference, accepted direction, proposed prototype/MVP, and later ambitions. Propose minimal filesystem, layout, visualization, and interaction boundaries. Keep identity/selection independent of scene objects. Explain theme versus visualization, spatial stability, size transformations, numerical inspection, and batching/visibility/label limits. Avoid a speculative plugin framework.
5. **Next experiment:** recommend one stack and strongest alternative with evidence, uncertainties, and explicit decision gates. Specify the smallest runnable experiment that tests real Windows filesystem navigation and one bounded cinematic treatment using the same data/selection model. If two small experiments are necessary to settle a material question, explain why and give each a stopping criterion.

## Proposed prototype boundary to evaluate

Root selection; asynchronous metadata scan with progress/cancellation; visible hierarchy and files; selection with full path and true numerical metadata; directory/parent navigation; reset/recovery view; explicit opening through Windows associations; inaccessible/disappearing entries handled without freezing. Start with read-only scanning plus deliberate opening. Mutations, full-drive scans, caching/watchers, split views, marketplace/plugin systems, and comprehensive historical fidelity are excluded from the first experiment.

Specify safe initial policies for reparse points/cycles, boundaries, hidden/system entries, long Unicode paths, and logical size versus allocated size/hard links/cloud placeholders. Unsupported behavior must be visible rather than silently misrepresented.

## Deliverables and acceptance

Write one concise durable report: `INVESTIGATION_01_FINDINGS.md`. Include:

- Environment evidence and proposed Windows workflow.
- Source/license findings, a compact stack comparison, recommendation and strongest alternative.
- Requirement classifications and minimal architecture sketch.
- Next implementation brief with scope, exclusions, observable acceptance criteria, validation steps, and decision gates.
- Evidence ledger: dated primary-source URLs, inspected source revision/file locations, exact relevant commands and observed results, and explicit unverified claims/blockers. Distinguish read, built, tested, launched, visually observed, and measured.

The prototype plan must name a deterministic synthetic fixture (including wide/deep trees, zero-byte and Unicode names, size outliers and inaccessible/reparse-point cases where feasible), a user-selected bounded real Windows directory, dataset counts, proposed resolution/quality settings, and hardware assumptions. Define proposed performance targets as hypotheses. Specify measurement of scan duration/cancellation responsiveness, navigation frame times during scanning, memory, and label/picking usability; do not fabricate results. Include screenshot/video and repeatable interaction evidence for both classic geometry and the cinematic treatment. Explain how we will distinguish live filesystem data from sample data and functional OS actions from mock interactions.

No engine spike is required for this investigation. If documentation/source evidence leaves a decisive uncertainty, describe an isolated spike with its cost, prerequisites, question and stopping criterion for the next assignment; do not expand into implementation now. A failed or unavailable build is a reported limitation, not evidence that a candidate cannot work.

Finish with a concise completion report listing changed files, checks/results, evidence gaps, blockers, and the recommended next task. ChatGPT will inspect the actual report and supporting evidence before recommending a product decision to DJ.
