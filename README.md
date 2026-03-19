# SysCleaner

![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-0C54C2?logo=windows&logoColor=white)
![Windows](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D4?logo=windows11&logoColor=white)
![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)

![SysCleaner Logo](logo.png)

SysCleaner is a Windows cleanup utility built with .NET 8 and WPF. It focuses on explainable, app-centric cleanup workflows instead of aggressive one-click deletion.

The project is designed around one core principle: preview first, execute second, and keep cleanup actions attributable.

## Highlights

- Installed application discovery with normalized metadata
- Broken uninstall-entry detection and cleanup
- Residual file, registry, startup item, context menu, scheduled task, and service analysis
- File lock detection and unlock assistance
- Empty file and empty folder cleanup with controlled scope
- Operation logging and recovery-oriented execution flow
- System repair and Windows Update repair/diagnostics entry points

## Preview

Main window snapshot from the current WPF build:

![SysCleaner Main Window](assets/screenshots/main-window.png)

UI views already implemented in the WPF client include:

- Dashboard
- Installed apps
- Residue cleanup
- Registry cleanup and registry search
- Context menu cleanup
- Startup items
- Scheduled tasks
- System services
- Unlock assistant
- System repair
- Windows Update repair
- History

## Getting Started

### Requirements

- Windows 10 or Windows 11
- .NET 8 SDK
- Visual Studio 2022 or newer with WPF/Desktop development workload

### Build

```powershell
dotnet build SysCleaner.slnx
```

### Test

```powershell
dotnet test SysCleaner.slnx
```

### Run

```powershell
dotnet run --project src/SysCleaner.Wpf/SysCleaner.Wpf.csproj
```

The WPF app is configured to request administrator privileges through its manifest because several cleanup and diagnostic operations require elevation.

## Release And Publish

### Build Release binaries

```powershell
dotnet build SysCleaner.slnx -c Release
```

### Publish a framework-dependent package

```powershell
dotnet publish src/SysCleaner.Wpf/SysCleaner.Wpf.csproj -c Release -r win-x64 --self-contained false
```

Typical output directory:

```text
src/SysCleaner.Wpf/bin/Release/net8.0-windows/win-x64/publish/
```

### Publish a self-contained package

```powershell
dotnet publish src/SysCleaner.Wpf/SysCleaner.Wpf.csproj -c Release -r win-x64 --self-contained true
```

### Packaging notes

- The application currently publishes as a standard WPF executable layout.
- The app manifest requests administrator privileges at startup.
- A dedicated MSIX or installer project is not currently configured in this repository snapshot.
- For internal distribution, the publish output can be zipped and distributed directly.

### GitHub Release workflow

Recommended tag format:

```text
v<major>.<minor>.<patch>
```

Examples:

```text
v1.0.0
v1.1.0
v1.1.1
```

Recommended asset naming:

```text
SysCleaner-v<version>-win-x64-framework-dependent.zip
SysCleaner-v<version>-win-x64-self-contained.zip
```

Example asset names:

```text
SysCleaner-v1.0.0-win-x64-framework-dependent.zip
SysCleaner-v1.0.0-win-x64-self-contained.zip
```

Suggested release checklist:

1. Ensure the working tree is clean.
2. Run release validation.
3. Publish the required package type.
4. Compress the publish output into a versioned zip file.
5. Create and push the version tag.
6. Create the GitHub Release and upload the zip asset.

Suggested validation commands:

```powershell
dotnet build SysCleaner.slnx -c Release
dotnet test SysCleaner.slnx -c Release
```

Example framework-dependent release flow:

```powershell
dotnet publish src/SysCleaner.Wpf/SysCleaner.Wpf.csproj -c Release -r win-x64 --self-contained false
Compress-Archive -Path src/SysCleaner.Wpf/bin/Release/net8.0-windows/win-x64/publish/* -DestinationPath SysCleaner-v1.0.0-win-x64-framework-dependent.zip -Force
git tag v1.0.0
git push origin main
git push origin v1.0.0
```

Example self-contained release flow:

```powershell
dotnet publish src/SysCleaner.Wpf/SysCleaner.Wpf.csproj -c Release -r win-x64 --self-contained true
Compress-Archive -Path src/SysCleaner.Wpf/bin/Release/net8.0-windows/win-x64/publish/* -DestinationPath SysCleaner-v1.0.0-win-x64-self-contained.zip -Force
```

Suggested GitHub Release steps:

1. Open the repository Releases page.
2. Choose Draft a new release.
3. Select the pushed tag, such as v1.0.0.
4. Use a release title matching the version, such as SysCleaner v1.0.0.
5. Summarize major changes, fixes, and any breaking notes.
6. Upload the generated zip asset.
7. Publish the release.

Suggested release notes structure:

- Added: new modules or user-facing capabilities
- Improved: behavior, performance, or usability changes
- Fixed: bugs or incorrect cleanup behavior
- Notes: elevation requirements, compatibility notes, or known limitations

## Usage Flow

1. Open the app and choose a cleanup module from the left navigation.
2. Run a scan for the selected scope.
3. Review evidence, risk hints, and matched items before executing changes.
4. Select only the items you want to process.
5. Execute cleanup or repair actions.
6. Review history and logs to audit what changed.

## Solution Structure

```text
src/
  SysCleaner.Wpf/             WPF shell, views, themes, and view models
  SysCleaner.Application/     Application services and orchestration
  SysCleaner.Domain/          Core domain models, enums, rules, and diagnostics
  SysCleaner.Infrastructure/  Windows integration, persistence, cleanup services
  SysCleaner.Contracts/       Cross-layer interfaces and DTO-style contracts
tests/
  SysCleaner.Tests/           Unit tests for application/domain/infrastructure behavior
```

## Architecture

- WPF client for desktop workflows and module navigation
- Application layer for orchestration and cleanup use cases
- Domain layer for models, rules, and diagnostics
- Infrastructure layer for Windows integration and persistence
- Contracts layer for shared interfaces between layers

Delivery documents are intentionally not tracked in the public repository snapshot.

## Safety Notes

- SysCleaner is intentionally conservative in destructive operations.
- The product favors evidence-backed cleanup candidates over broad heuristic deletion.
- Registry and filesystem actions are surfaced as explicit user decisions.
- Protected/system scenarios should remain non-default and auditable.

## License

This repository is licensed under the MIT License. See [LICENSE](LICENSE).
