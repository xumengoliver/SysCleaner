# SysCleaner High-Level Design

## 1. Project Overview And Business Value

SysCleaner is a Windows local cleanup utility built with .NET 8 and WPF. Its purpose is to help users remove installed applications, identify and clean uninstall residues, safely clean targeted registry debris, clean broken uninstall-list entries left after manual deletion or failed removal, clean redundant or invalid right-click menu items, manage startup items safely, identify which process or service is occupying files targeted for cleanup, and remove empty files and empty folders within controlled scopes.

The product value is not “clean everything aggressively”. The product value is “make cleanup actions explicit, attributable, and recoverable”. This is the only credible position for a tool that can modify registry entries and delete filesystem content.

## 2. Final Recommended Solution

### 2.1 Positioning

- Platform: Windows 10/11 desktop
- Tech stack: .NET 8, WPF, SQLite
- Product direction: local safe cleanup tool with guided workflows
- Design philosophy: preview first, execute second, rollback where feasible
- UI direction: modern, restrained, high-clarity desktop experience

### 2.2 V1 Functional Modules

1. Installed software discovery
2. Invalid uninstall entry detection and cleanup
3. Context menu analysis and cleanup
4. Startup item analysis and cleanup
5. Lock detection and unlock assistance
6. Software uninstall orchestration
7. Uninstall residue analysis and cleanup
8. Registry residue analysis and cleanup
9. Empty file and empty folder analysis and cleanup
10. Cleanup execution engine
11. Rollback and operation history
12. Settings, exclusions, and safety policies

## 3. Multi-Role Discussion Summary

### 3.1 Main Trade-Offs

- Trade-off 1: coverage vs safety
  - Decision: favor safety. V1 will intentionally miss some obscure residues rather than over-delete.
- Trade-off 2: one-click automation vs explainability
  - Decision: favor explainability. V1 uses guided previews rather than blind deep clean.
- Trade-off 3: direct UI actions vs unified task engine
  - Decision: favor a unified task engine to keep logs, rollback, and consistent error handling.
- Trade-off 4: pure file-based local storage vs SQLite
  - Decision: use SQLite because cleanup history, rollback metadata, exclusions, and app signatures are structured and queryable.

### 3.2 Rejected Alternatives

- Alternative A: build a fully portable tool with only JSON storage
  - Rejected because scan history, rollback manifest lookup, and multi-table relationships become harder to manage.
- Alternative B: broad generic registry cleaner as a headline feature
  - Rejected because it is hard to justify safety and often creates low-value high-risk behavior.
- Alternative C: rely entirely on installer-provided uninstall strings and stop there
  - Rejected because the user explicitly needs residue cleanup and empty artifact cleanup.

## 4. Software Architecture

### 4.1 Layered Architecture

Recommended project split:

1. SysCleaner.Wpf

- WPF shell, pages, reusable controls, resource dictionaries, view models

1. SysCleaner.Application

- Use cases, orchestration, task pipeline, risk scoring, report generation

1. SysCleaner.Domain

- Entities, value objects, rules, cleanup item model, operation model

1. SysCleaner.Infrastructure

- Registry access, filesystem access, process launching, MSI/installer integration, SQLite repositories, logging

1. SysCleaner.Contracts

- DTOs, request/response contracts, service interfaces shared across layers

Recommended UI pattern:

- MVVM with command binding and observable state
- Shared theme resources for colors, spacing, corner radius, typography, and state styles
- Reusable card/list/detail components instead of page-specific ad hoc controls

### 4.2 Core Architectural Principle

All scan functions produce normalized cleanup candidates. All execution functions consume cleanup candidates through the same execution engine.

This avoids inconsistent behavior such as one module deleting directly while another module writes logs and supports rollback.

## 5. Core Domain Model

### 5.1 Main Entities

- InstalledApp
  - AppId
  - DisplayName
  - Publisher
  - DisplayVersion
  - InstallLocation
  - UninstallString
  - QuietUninstallString
  - InstallerType
  - RegistrySource
  - IsSystemComponent
  - IsMicrosoftSigned

- CleanupCandidate
  - CandidateId
  - Category
  - SubCategory
  - PathOrRegistryKey
  - Evidence
  - RiskLevel
  - EstimatedSize
  - IsSelected
  - RollbackSupported
  - SourceRuleId
  - RelatedAppId

- CleanupTask
  - TaskId
  - TaskType
  - CreatedAt
  - ScopeDefinition
  - Status
  - Summary

- ExecutionRecord
  - RecordId
  - TaskId
  - CandidateId
  - ActionType
  - BeforeStateReference
  - AfterStateReference
  - Result
  - ErrorMessage

- ExclusionRule
  - RuleId
  - RuleType
  - Pattern
  - AppliesToCategory
  - Enabled

### 5.2 Candidate Categories

- AppFileResidue
- AppShortcutResidue
- AppServiceResidue
- AppTaskSchedulerResidue
- AppRegistryResidue
- BrokenUninstallEntry
- ContextMenuEntry
- StartupEntry
- LockedResource
- EmptyFile
- EmptyFolder
- GenericInvalidRegistryEntry

## 6. Functional Design

### 6.1 Installed Software Discovery

Discovery sources:

- HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall
- HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall
- HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall
- MSI product metadata when identifiable

Normalization logic:

- Merge duplicated entries across registry views
- Parse uninstall strings
- Infer installer type where possible
- Evaluate uninstall entry health by checking uninstall command target existence, install location existence, and registry completeness
- Flag protected/system entries

Output:

- Searchable installed-app list with publisher, version, install path, uninstall support, uninstall-entry health state, and protection state

### 6.2 Invalid Uninstall Entry Detection And Cleanup

Target scenario:

- The app still appears in Windows Apps & Features / Programs and Features
- Clicking uninstall is invalid or does nothing
- The original uninstall executable, MSI product registration, or install folder is already missing

Detection rules:

- UninstallString points to a missing executable or missing script target
- QuietUninstallString points to a missing executable or missing script target
- InstallLocation is configured but does not exist
- Registry entry has display metadata but lacks any valid uninstall path and lacks recoverable MSI metadata
- Display icon and publisher metadata remain, but all executable anchors are gone

Classification result:

- Healthy entry: normal uninstall path available
- Warning entry: partial metadata damage, but uninstall path may still be valid
- Broken entry: uninstall path and installation evidence are both invalid or missing

Cleanup action:

- Preview the exact uninstall-list registry entry that will be removed
- Remove only the uninstall-list entry and closely related display metadata for that entry
- Do not automatically remove broader app residue in the same action unless the user launches residue cleanup separately

Safety rules:

- Never mark an entry as broken solely because InstallLocation is empty; many installers do not populate it
- MSI-based entries require extra validation before being treated as broken
- Protected system components and Microsoft-signed platform components remain non-removable by default
- Export the uninstall entry registry key before deletion to support rollback

### 6.3 Context Menu Analysis And Cleanup

Scan sources:

- HKCR\*\shell
- HKCR\*\shellex\ContextMenuHandlers
- HKCR\Directory\shell
- HKCR\Directory\Background\shell
- HKCR\Folder\shell
- HKCR\AllFilesystemObjects\shell
- HKCR\Drive\shell
- HKCR\CLSID and referenced shell extension registrations when needed for resolution

Supported entry types:

- Command-based context menu items
- Shell extension handler registrations
- App-added file/folder/background menu entries

Classification logic:

- Valid entry: target executable, DLL, or COM registration resolves correctly
- Invalid entry: command target missing, referenced DLL missing, or referenced CLSID registration broken
- High-risk entry: system-level shell extension or Microsoft-owned handler

Recommended actions:

- Default action is Disable for non-invalid third-party items
- Cleanup/Delete is allowed for invalid third-party items after preview
- High-risk items are view-only in standard mode

Safety rules:

- Never clean Microsoft default shell handlers in standard mode
- Show owner app, registry path, command or CLSID target, and health state before action
- Prefer disable-before-delete for non-broken entries

### 6.4 Startup Item Analysis And Cleanup

Scan sources:

- HKCU\Software\Microsoft\Windows\CurrentVersion\Run
- HKLM\Software\Microsoft\Windows\CurrentVersion\Run
- HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce
- HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce
- Current user Startup folder
- All users Startup folder
- Scheduled tasks flagged to run at logon or startup

Classification logic:

- Enabled startup item
- Disabled startup item
- Invalid startup item with missing executable or broken shortcut target
- High-impact startup item with elevated privileges or machine-wide scope

Recommended actions:

- Disable as the default safe operation
- Cleanup/Delete for clearly invalid or explicitly selected entries
- Batch disable supported for selected third-party items

Safety rules:

- Do not remove Windows security, input, graphics, or device-critical startup entries by default
- Show publisher, command line, startup source, current status, and file existence before action
- Scheduled task startup items require separate confirmation because they may be used by update agents and device software

### 6.5 Lock Detection And Unlock Assistance

Target scenario:

- A file or folder cannot be deleted because it is currently in use
- The user cannot determine which process, service, shell extension, or Explorer instance is holding the lock
- Cleanup, uninstall residue deletion, or empty-folder cleanup is blocked by file occupancy

Detection sources and strategies:

- Use Restart Manager APIs where applicable to detect processes using a file or related resources
- Enumerate matching processes by executable path correlation for known uninstall targets and residue paths
- Inspect Windows services whose binary path or working directory references the locked resource
- Identify Explorer and shell-related occupancy for file preview, thumbnail, or folder window scenarios
- When precise handle resolution is not available through safe native APIs, fall back to guided heuristics and explicit user messaging

Classification logic:

- User-process lock: ordinary desktop app or background process holds the file
- Service lock: Windows service or updater service holds the file
- Shell lock: Explorer, preview handler, or shell extension likely holds the file
- Unknown lock: deletion failed with in-use semantics, but exact holder cannot be resolved with confidence
- High-risk lock: security software, system service, or protected Windows component holds the file

Recommended actions:

- Show exact locking process name, PID, executable path, and publisher when available
- Offer close-process action for non-critical user processes
- Offer stop-service action for matched third-party services with elevated confirmation
- Offer restart Explorer action only when the holder is clearly shell-related and user confirms it
- Offer schedule-delete-on-reboot when immediate unlock is unsafe or unavailable
- Offer retry deletion after unlock action completes

Safety rules:

- Never force-terminate protected system processes in standard mode
- Never stop Windows core services automatically
- Any terminate-process or stop-service action must show impact summary before execution
- If holder resolution confidence is low, present the action as advisory rather than definitive

### 6.6 Software Uninstall Orchestration

Execution order:

1. Validate app entry and required privilege level
2. Offer standard uninstall
3. Monitor uninstall process and result
4. If uninstall fails or completes, offer residue analysis

Supported paths:

- MSI uninstall via product code when reliably known
- UninstallString execution for installer-managed applications
- Quiet uninstall only as an advanced option in V1

Safety rules:

- Do not expose uninstall for protected system components by default
- Show exact command path before execution when in advanced mode

### 6.7 Uninstall Residue Analysis And Cleanup

Residue scan sources:

- InstallLocation
- Program Files / Program Files (x86)
- ProgramData
- AppData Local / Roaming
- Desktop and Start Menu shortcuts
- Services referencing app paths
- Scheduled tasks referencing app executables

Correlation strategy:

- Primary correlation by normalized install location and executable names
- Secondary correlation by publisher + product display name aliases
- Rule signatures maintained per installer family and app evidence patterns

Output behavior:

- Each residue item shows location, reason, size, confidence, and deletion action

### 6.8 Registry Cleanup

Registry cleanup is split into two modes.

Mode A: app-linked residue cleanup

- Remove uninstall leftovers linked to a known removed application
- Remove app-specific Run entries, shell extensions, context menu items, file associations, and service references when there is strong evidence and rule support
- Target only keys that correlate to the removed app identity

Mode B: conservative generic invalid-entry cleanup

- Disabled by default in V1
- Limited to clearly invalid references, such as non-existent file paths in a narrow vetted rule set
- Requires higher confirmation level

Registry safety strategy:

- Backup targeted keys before modification
- Record pre-change data into rollback manifest
- Never scan or modify the entire registry indiscriminately

### 6.9 Empty File And Empty Folder Cleanup

Supported scan roots:

- User-selected directories
- Optional predefined safe presets such as temp folders and app cache folders

Protection rules:

- Exclude Windows directory, Program Files roots, user profile root, OneDrive root, source code workspace roots, and any custom exclusions by default policy or user configuration
- Exclude junctions/reparse points unless explicitly enabled
- Exclude folders with hidden/system attributes in standard mode

Definition rules:

- Empty file: size 0 bytes and not protected by exclusion rules
- Empty folder: contains no eligible files or subfolders after applying exclusion policy

Cascade rules:

- After deleting an empty file or empty subfolder, re-evaluate the immediate parent folder within the same task
- If the parent folder becomes empty and is inside the allowed cleanup scope, add it as a new cleanup candidate or execute it in the same cascade chain depending on execution mode
- Continue upward re-evaluation until reaching a non-empty folder, a protected folder, the selected scan root, or a configured stop boundary
- Never cascade-delete above the user-selected root or across excluded/protected path boundaries

Execution strategy:

- Default to Recycle Bin for files and folders when possible
- Permanent delete only in advanced mode
- Execution engine must support dynamic candidate expansion so parent folders that become empty mid-task can be safely processed

## 7. User Interaction And Anti-Mistake Design

### 7.0 Visual And Interaction Design Baseline

Design target:

- Modern, but not decorative
- Minimal, but not sparse or ambiguous
- Clear and direct for system-level operations

Visual language:

- Base palette: light neutral surfaces with low-saturation blue/teal as the action accent and amber/red reserved for warning and destructive states
- Background structure: layered neutral surfaces instead of flat white everywhere, using subtle contrast between shell, navigation, content panel, and detail panel
- Shape system: medium corner radius, soft separators, low-noise shadows only for elevated panels and dialogs
- Iconography: simple line icons with consistent stroke weight
- Typography: Segoe UI Variable preferred, with clear weight contrast between page titles, section headers, and body text

Layout principles:

- Left navigation for stable module switching
- Main content split into summary header, filter/action bar, result list, and detail/preview panel
- Critical decisions should happen in the detail panel or confirmation sheet, not inside crowded table cells
- Keep the default page width spacious, but maintain dense information presentation through card grouping and progressive disclosure rather than tiny text

Interaction principles:

- One primary action per screen, with secondary actions visually demoted
- Risk state must be visible before selection through tags such as Safe, Review, High Risk, Broken, Protected
- Prefer toggle-based Disable for reversible operations and reserve Delete/Cleanup for explicit destructive intent
- Use step-based execution summaries for scans and cleanup tasks instead of modal spam

Component guidance:

- Navigation item with icon, label, and current-task badge
- Summary cards for counts like Broken Entries, Startup Items, Context Menu Items, and Recoverable Actions
- Summary cards for counts like Broken Entries, Startup Items, Context Menu Items, Locked Resources, and Recoverable Actions
- Result list with sticky filter bar, multi-select, sortable columns, and inline health tags
- Detail panel showing evidence, source path, owner app, command target, lock holder details, rollback support, and recommended action
- Confirmation dialog with concise impact summary and explicit destructive wording

Motion and feedback:

- Use restrained transitions such as fade/slide for panel changes and loading placeholders
- No ornamental animation loops
- Long-running scans show staged progress with current phase, processed count, and cancellable state

Accessibility and clarity:

- Keyboard reachable primary workflows
- Text labels remain visible and are not replaced by icon-only actions in critical screens
- Warning and destructive colors must always be paired with text/icon labels, not color alone

### 7.1 Main Navigation

Recommended left navigation:

1. Dashboard
2. Software Uninstall
3. Uninstall Entry Cleanup
4. Context Menu Cleanup
5. Startup Manager
6. Unlock Assistant
7. Residue Cleanup
8. Registry Cleanup
9. Empty Files/Folders
10. History And Rollback
11. Settings

### 7.2 Main Interaction Flow

For app cleanup:

1. Select app
2. View app details
3. Start uninstall or residue scan
4. Review categorized candidates
5. Select items
6. Execute cleanup
7. Review report

For broken uninstall-entry cleanup:

1. Scan installed-app registry entries
2. Filter entries marked as broken
3. Review evidence such as missing uninstall target, missing install folder, and registry source path
4. Preview the exact uninstall-list key to be removed
5. Execute cleanup of the stale entry only
6. Review rollback/export record

For context menu cleanup:

1. Scan right-click menu entries by source type
2. Group by file, folder, background, and shell extension categories
3. Review owner app, command target, CLSID or DLL target, and health state
4. Disable or clean selected entries
5. Review execution report and rollback/export record

For startup cleanup:

1. Scan startup sources from registry, startup folders, and scheduled tasks
2. Filter invalid, high-impact, machine-wide, or third-party entries
3. Review command target, publisher, startup source, and status
4. Disable or clean selected entries
5. Review execution report and rollback/export record

For lock detection and unlock assistance:

1. Select or auto-capture the file/folder that failed deletion
2. Detect lock holders and classify them as process, service, shell, unknown, or protected
3. Review process name, PID, path, publisher, and recommended unlock action
4. Apply close-process, stop-service, restart-Explorer, or schedule-on-reboot action as appropriate
5. Retry deletion and review execution result

For empty cleanup:

1. Select scan roots
2. Configure exclusions
3. Run scan
4. Preview candidates with size and path
5. Execute selected cleanup
6. Review cascade-deleted parent folders generated during execution

### 7.3 Anti-Mistake Rules

- High-risk items are not preselected
- Registry items show exact key path and matched rule
- Broken uninstall-entry cleanup shows the exact uninstall-list registry path and why the entry is classified as broken
- Context menu cleanup shows whether the action is Disable or Delete and highlights shell-extension level risk
- Startup cleanup defaults to Disable instead of Delete for valid entries
- Unlock assistance must show exactly why a file is blocked and must never default to force-kill protected processes
- Empty-folder cleanup must show when parent folders are cascade-deleted after child cleanup and must stop at protected or user-defined boundaries
- Protected paths cannot be removed unless user explicitly unlocks advanced mode
- Every execution page shows item count, affected locations, and rollback availability summary

## 8. Execution Engine Design

### 8.1 Unified Pipeline

1. Pre-check
2. Privilege evaluation
3. Backup/manifest generation
4. Execute item actions sequentially or by safe batch groups
5. Capture result per item
6. Generate summary report

### 8.2 Action Types

- LaunchUninstallCommand
- DeleteUninstallRegistryEntry
- DisableContextMenuEntry
- DeleteContextMenuEntry
- DisableStartupEntry
- DeleteStartupEntry
- CloseLockingProcess
- StopLockingService
- RestartExplorerShell
- ScheduleDeleteOnReboot
- DeleteFile
- DeleteFolder
- DeleteRegistryKey
- DeleteRegistryValue
- RemoveShortcut
- RemoveScheduledTask
- RemoveServiceReferenceMetadata

### 8.3 Error Handling

- One item failure does not automatically abort the entire task unless the failure is in a required precondition
- Dynamic candidates created by cascade empty-folder cleanup must be logged with their parent-child trigger relationship
- Failed items are retriable individually
- Summary report distinguishes success, skipped, failed, and rolled back

## 9. Rollback And Auditability

### 9.1 Rollback Strategy

- Files/folders: prefer Recycle Bin path in standard mode
- Registry: export targeted values/keys into rollback manifest
- Operation report: persist every item result with timestamp

### 9.2 Limits Of Rollback

- External uninstaller behavior cannot always be reversed by SysCleaner
- Deletion outside Recycle Bin or modifications by third-party uninstallers may only support partial rollback
- These limits must be shown in the UI before execution

## 10. Data Storage Design

### 10.1 SQLite Tables

- installed_app_cache
- cleanup_task
- cleanup_candidate
- execution_record
- exclusion_rule
- app_signature_rule
- setting_item

### 10.2 Why SQLite

- Structured local queries for history and rollback lookups
- Easier management of scan cache and rule metadata than scattered JSON files
- No external dependency for deployment

## 11. Key Interfaces Inside The Application

### 11.1 Application Services

- IInstalledAppService
- IContextMenuService
- IStartupItemService
- ILockDetectionService
- IUnlockAssistanceService
- IUninstallOrchestrator
- IResidueAnalysisService
- IRegistryCleanupService
- IEmptyItemScanService
- ICleanupExecutionService
- IRollbackService
- IExclusionPolicyService

### 11.2 Infrastructure Adapters

- IRegistryAccessor
- IFileSystemAccessor
- IProcessLauncher
- IRestartManagerAdapter
- IMsiMetadataReader
- IRecycleBinService
- IShortcutResolver
- IScheduledTaskAccessor
- IServiceManagerAccessor
- ISqliteDbContextFactory

## 12. Test Plan And Quality Strategy

### 12.1 Test Layers

- Unit tests for rule matching, path normalization, risk scoring, and exclusion policy
- Integration tests for registry and filesystem adapters using isolated fixtures
- VM-based end-to-end tests for uninstall and cleanup flows
- Manual exploratory tests for UI clarity and rollback understanding

### 12.2 Critical Test Scenarios

1. Uninstall app successfully, then cleanup linked residues
2. App folder is manually deleted, uninstall-list entry remains, and the system correctly classifies it as a broken uninstall entry
3. Entry has missing InstallLocation but valid MSI uninstall path, and the system does not misclassify it as broken
4. Context menu item points to a missing executable and is correctly classified as invalid
5. Microsoft shell handler is surfaced as protected and cannot be deleted in standard mode
6. Startup registry item points to a missing executable and defaults to safe cleanup after preview
7. Scheduled-task startup item is shown separately and requires stronger confirmation
8. Residue file is locked by a visible user process and the system shows process details before offering close-and-retry
9. Locked file is held by Explorer shell and the system offers restart Explorer rather than generic force-kill
10. Locked file is held by a protected system process and the system blocks dangerous unlock actions in standard mode
11. Uninstall command fails, but residue scan still works
12. Registry candidate points to shared vendor path and is correctly blocked
13. Empty folder scan encounters junction and skips it
14. A parent folder that was initially non-empty becomes empty after child cleanup and is correctly deleted within the same task
15. Cascade cleanup stops at the selected root or protected boundary and does not over-delete ancestor folders
16. Cleanup task partially fails and produces accurate report
17. Rollback manifest is generated for supported actions

### 12.3 Release Gates

- No deletion of protected roots in baseline test suites
- No registry deletion without stored evidence and rule ID
- All destructive actions must be previewable in UI

## 13. Security, Privilege, And Release Safeguards

- Standard scans can run without elevation where possible
- Elevation is requested only for actions that require it
- Logs must include privilege context and exact action target
- Microsoft and Windows protected components are hidden or locked by default
- Advanced mode is visually separated from standard mode
- WPF theme resources must enforce consistent visual states for safe, warning, destructive, disabled, and protected items

## 14. Milestones And Implementation Suggestions

### 14.1 Milestone 1: Foundation

- Solution structure
- Base UI shell
- WPF theme and shared control library
- SQLite setup
- Logging and settings
- Installed software discovery

### 14.2 Milestone 2: App Uninstall And Residue Flow

- Uninstall orchestration
- Residue scan engine
- Candidate preview UI
- Execution engine v1

### 14.3 Milestone 3: Registry Cleanup And Empty Item Cleanup

- App-linked registry cleanup
- Empty file/folder scan
- Exclusion policies
- History and rollback UI

### 14.4 Milestone 4: Hardening

- Regression fixtures
- Performance optimization
- UX refinement
- Packaging and installer

## 15. Key Risks, Open Issues, And Assumption Boundaries

### 15.1 Key Risks

- False deletion risk from weak app correlation
- Registry cleanup overreach if generic rules are too broad
- Shell extension cleanup risk if COM resolution is incomplete
- Startup cleanup risk if device or security software entries are misclassified
- Unlock assistance risk if process attribution is wrong or dangerous kill actions are too easy
- Installer diversity causing inconsistent uninstall behavior
- Permission elevation and file-lock edge cases

### 15.2 Open Issues

- Whether V1 must support batch silent uninstall
- Whether V1 should support temporary disable/restore for shell extension handlers without full deletion for all handler types
- Whether startup impact scoring should be simple rule-based or include performance sampling in a later version
- Whether V1 lock detection should stay on safe native APIs only or optionally integrate deeper handle inspection in advanced mode later
- Whether driver and service binary cleanup should be deeper than metadata cleanup
- Whether portable application cleanup is a first-class feature
- Whether online signature rule updates are needed in V1.1

### 15.3 Assumption Boundary

- This design assumes a standalone local utility for single-machine use.
- This design does not assume enterprise fleet management or remote orchestration.

## 16. Compromises And Backup Plan

- If app identity correlation is weak, downgrade item confidence and require manual confirmation instead of blocking the whole scan.
- If registry rollback cannot be guaranteed for a category, keep that category preview-only in the first release.
- If WPF visual complexity grows, constrain the design system to a small reusable component set rather than allowing page-by-page custom styling.
