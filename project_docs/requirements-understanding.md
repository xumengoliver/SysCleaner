# SysCleaner Requirements Understanding

## 1. Input Validation Result

### 1.1 Known Information

- Product type: Windows desktop cleaning software.
- Core capabilities explicitly required:
  - Software uninstall
  - Uninstall residue cleanup
  - Invalid uninstall-list entry cleanup for apps already deleted manually or incompletely removed
  - Registry cleanup
  - Context menu cleanup
  - Startup item cleanup
  - File and folder lock detection with unlock assistance
  - Empty file cleanup
  - Empty folder cleanup
- Technology stack fixed by user:
  - .NET 8
  - WPF
- Current phase: design first, implementation later.

### 1.2 Missing Information

- Target users are not explicitly defined: ordinary home users, power users, or enterprise IT staff.
- Whether batch silent uninstall is required is not confirmed.
- Whether rollback must cover registry and filesystem together is not confirmed.
- Whether third-party installer ecosystems must be deeply supported is not confirmed, such as MSI, Inno Setup, NSIS, InstallShield, winget/choco managed apps.
- Whether there is a requirement for portable software cleanup is not confirmed.
- Whether software update, telemetry, cloud sync, or online signature updates are in scope is not confirmed.

### 1.3 Key Assumptions

- This product is a pure local Windows utility and does not depend on a backend service in V1.
- Primary target users are advanced consumers and local machine maintainers rather than large-scale enterprise administrators.
- V1 focuses on safe and explainable cleanup, not aggressive cleanup coverage.
- Cleanup operations default to preview first, execute later, with rollback metadata retained.
- Registry cleanup in V1 is conservative and rule-based. It will not attempt broad “deep magic” optimization of the entire registry.
- Empty file and empty folder cleanup targets user-selected scopes and known safe scopes first, not the whole disk by default.

## 2. Activated Roles And Reasons

### 2.1 Software Architect

- Needed to define desktop architecture, module boundaries, execution pipeline, and extensibility.

### 2.2 Test Engineer

- Needed because this product touches uninstall, registry, and file deletion, where regression and false deletion risk are high.

### 2.3 Senior User

- Needed to challenge whether operations are understandable, recoverable, and controllable from a real-user perspective.

### 2.4 Domain Expert

- Needed because Windows uninstall, residual artifacts, registry structure, and privilege handling all depend on platform-specific knowledge.

### 2.5 UX/UI

- Added because desktop cleaning tools often become visually cluttered and intimidating; the interaction model and visual hierarchy are critical to build trust and reduce accidental operations.

### 2.6 Security

- Added because the software can modify uninstall entries, delete files, and edit registry keys. Even if this is a local utility, safety boundaries matter.

## 3. Technology Stack Applicability

- User-specified stack is .NET 8 + WPF.
- This stack is suitable for the task because:
  - Windows-only local system utility is the primary scenario.
  - WPF is better suited for a modern desktop UI with stronger layout composition, styling, templating, and state visualization.
  - .NET 8 provides long-term support, modern runtime behavior, async support, diagnostics, and packaging options.
- Recommendation:
  - UI framework: WPF
  - Architecture pattern: layered architecture with MVVM separation, minimal code-behind, and reusable views/styles
  - Persistence: SQLite for history, task records, whitelist/blacklist, and rollback manifests
  - Windows integration: P/Invoke plus officially available APIs where possible

## 4. Need For Front-Loaded Understanding Document

- Required.
- Reason:
  - User has requested a design that will likely be used as the base for implementation.
  - Several safety assumptions must be recorded before code work starts.
  - This project has non-trivial scope boundaries and high-risk destructive operations.

## 5. Product Goal Summary

- Build a Windows cleaner that is safer and more explainable than “one-click deep clean” tools.
- Prioritize precise application cleanup and traceable operations over exaggerated scan counts.
- Provide a guided flow from discovery to preview to execution to rollback.

## 6. Scope Definition

### 6.1 In Scope For V1

- Installed software discovery
- Standard uninstall launch and orchestration
- Residual artifact discovery after uninstall
- Invalid uninstall-list entry detection and cleanup when uninstall targets no longer exist
- Rule-based registry residue cleanup related to removed software
- Context menu item discovery, classification, disablement, and cleanup
- Startup item discovery, classification, disablement, and cleanup
- File/folder occupancy detection, lock source identification, and guided unlock actions
- Empty file cleanup in configurable scopes
- Empty folder cleanup in configurable scopes
- Cascading empty-folder cleanup after child items are deleted within the same task
- Cleanup preview and selective execution
- Operation logging and rollback metadata retention
- Risk prompts, exclusions, and protected paths

### 6.2 Out Of Scope For V1

- Driver cleanup as a standalone capability
- Browser privacy cleanup
- Duplicate file cleanup
- Memory optimization or process boosting
- Real-time background resident cleaning
- Enterprise remote management console
- Cloud account system

## 7. Multi-Role Initial Roundtable Summary

### 7.1 Software Architect View

- Core judgment: the product should be built around a task pipeline and evidence model, not around isolated buttons.
- Main risk: if every cleanup action is implemented as direct filesystem or registry deletion without a unified execution engine, rollback and auditing will collapse.
- Recommended direction: use a scan-result model, rule engine, execution journal, and policy-based deletion strategy.

### 7.2 Test Engineer View

- Core judgment: false positives are the primary release blocker, not scan speed.
- Main risk: deleting shared folders, deleting valid registry references, and failing halfway through cleanup.
- Recommended direction: create a deterministic preview, dry-run mode, synthetic fixtures, VM-based regression tests, and category-specific safety gates.

### 7.3 Senior User View

- Core judgment: users do not trust cleaners that only show a huge item count without explanation.
- Main risk: users cannot understand why an item is considered safe to remove, and cannot easily undo mistakes.
- Recommended direction: show evidence, location, reason, risk level, and default action for every cleanup item.

### 7.4 Domain Expert View

- Core judgment: Windows uninstall and residue cleanup requires installer-aware heuristics; generic filename matching is not enough.
- Main risk: uninstall strings vary by installer family, registry locations are split across machine/user and 32/64-bit views, and AppData/ProgramData artifacts are inconsistent.
- Recommended direction: normalize installed-app identity first, then correlate file paths, shortcuts, services, scheduled tasks, and registry artifacts through a signature model, and explicitly detect stale uninstall entries whose uninstall targets have disappeared.

### 7.5 UX/UI View

- Core judgment: V1 must avoid dense all-in-one clutter and instead use guided modes.
- Main risk: advanced options mixed into the main path will create user errors.
- Recommended direction: use WPF to build a modern but restrained UI with left navigation, focused workspaces, preview panes, strong visual hierarchy, and explicit execution summaries.

### 7.6 Security View

- Core judgment: safe defaults are mandatory because the product performs destructive local operations.
- Main risk: operations executed with elevated rights can damage the system if rules are too broad.
- Recommended direction: protect system paths, protect Microsoft-signed components by default, require confirmation on high-risk actions, and persist detailed logs.

## 8. V1 Baseline Direction

- Product positioning: safe local Windows cleanup tool.
- Product principle: explainable, previewable, recoverable.
- Recommended V1 core loop:
  1. Discover installed apps and cleanup candidates.
  2. Analyze evidence and classify risk.
  3. Preview and allow user selection.
  4. Execute through a unified cleanup engine.
  5. Record logs, manifests, and rollback material.

## 9. Senior User Challenges

### 9.1 Pain Point 1

- “I only want to remove leftovers from one app. I do not want the tool to scan and suggest unrelated registry entries.”

### 9.2 Pain Point 2

- “I cannot tell whether a folder under AppData is truly safe to remove or used by another app from the same vendor.”

### 9.3 Pain Point 3

- “If uninstall fails halfway, I still need a controlled residue scan rather than a broken state.”

### 9.4 Pain Point 4

- “I do not want to accidentally clean network folders, synced directories, or developer workspaces when finding empty folders.”

### 9.5 Pain Point 5

- “If something goes wrong, I need to know exactly what the tool changed.”

### 9.6 Pain Point 6

- “I deleted a software folder manually, but it still shows in Windows uninstall list and uninstall does nothing. I need to remove only that broken uninstall entry safely.”

### 9.7 Pain Point 7

- “The right-click menu has too many invalid or slow items, but I do not want to break normal shell functions or remove Microsoft system entries by mistake.”

### 9.8 Pain Point 8

- “Too many programs start with Windows, and some of them point to files that no longer exist. I need a safe way to disable or remove them.”

### 9.9 Pain Point 9

- “When deleting software or residues, Windows says the file is in use, but I cannot tell which process, service, or Explorer window is locking it. I need the tool to show exactly what is occupying it and help me release it.”

### 9.10 Pain Point 10

- “A folder is not empty at first, but after deleting the empty child file or child folder inside it, the parent folder becomes empty too. The tool should continue cleaning that parent folder instead of leaving it behind.”

## 10. Consensus After Iteration

- App-centric cleanup becomes the primary entry, not global cleaning.
- Broken uninstall entries become a dedicated cleanup lane, not an implicit side effect of generic registry cleanup.
- Registry cleanup is split into two lanes:
  - Precise app residue registry cleanup linked to a removed app
  - Conservative generic invalid-entry cleanup, disabled by default in V1
- Context menu cleanup and startup cleanup become dedicated capabilities with independent preview and safety policies.
- Lock detection becomes a supporting capability for deletion-related workflows, with process visibility and guided unlock actions.
- Empty file and empty folder cleaning require explicit scope selection, protected-path exclusions, and post-delete cascading re-evaluation of parent folders.
- Every cleanup item needs evidence text, risk level, and source rule.
- Execution must generate an operation report and rollback manifest.

## 11. Success Criteria For V1

- User can uninstall a selected app and see related residual items before deletion.
- User can detect uninstall-list entries whose uninstall command target or install path no longer exists, and remove those stale entries after preview.
- User can independently run residue scan for an already removed app.
- User can discover startup items from registry and startup folders, disable or clean invalid entries, and distinguish system entries from third-party entries.
- User can discover right-click menu entries, see their source location and owning app, and disable or clean broken items safely.
- User can identify which process, service, or shell component is locking a file or folder, and apply guided unlock actions before retrying deletion.
- User can scan selected directories for empty files and empty folders with exclusions, and the system can continue deleting parent folders that become empty during the same cleanup task.
- The system produces an execution log and rollback artifacts for supported cleanup actions.
- False positive rate remains low enough that no system-critical path is deleted in test baselines.

## 12. Recommended Next Documents

- High-level design
- Detailed module design
- Rule and signature design
- Test strategy and fixture design
