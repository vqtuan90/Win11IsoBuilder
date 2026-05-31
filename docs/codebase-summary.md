# Codebase Summary — Windows 11 Custom ISO Builder

.NET 8 + WPF desktop app (admin-elevated, x64, Windows-only) that turns a user-supplied
Windows 11 ISO into a customized, unattended install ISO with offline Microsoft 365 + apps.
See `../prd.md` (Vietnamese original) or `project-overview-pdr.md` (English translation) for full requirements.

## Solution Layout

```
Win11IsoBuilder.sln
├── Win11IsoBuilder/
│   ├── Models/                 8 POCO contracts (BuildConfig is the single source of truth)
│   │   ├── BuildConfig.cs      Input selections + derived workspace paths
│   │   ├── BuildProgress.cs    UI progress reporting (percent, stage, message)
│   │   ├── ToolPaths.cs        Resolved DISM/oscdimg/ODT paths
│   │   ├── WindowsEdition.cs   WIM edition (index, name)
│   │   ├── WinCustomizationOptions.cs  Bypass toggles, admin user, appx-remove list
│   │   ├── OfficeOptions.cs    Enable flag + language/bitness/exclude modules
│   │   ├── AppEntry.cs         Name, URL, silent flags, SHA-256 hash
│   │   └── AppInstallCommand.cs  Resolved installer path + command-line
│   ├── Services/               11 service classes (UI-agnostic, logged via ILogSink)
│   │   ├── ProcessRunner.cs    Async wrapper: dism/oscdimg/setup.exe/robocopy; stdout/stderr capture; timeout; live callback
│   │   ├── LogService.cs       ILogSink impl: file + event (EntryLogged)
│   │   ├── ToolDetectionService.cs  Detect DISM (System32) + oscdimg (bundled or ADK); IsAdkMissing flag
│   │   ├── IsoService.cs       Mount ISO, robocopy extract, validate media, oscdimg dual-boot repack
│   │   ├── WimService.cs       DISM mount/unmount, ESD→WIM export, appx list/remove, orphan cleanup
│   │   ├── UnattendBuilder.cs  autounattend.xml via XDocument (LabConfig bypass, local account, locale, ComputerName="*")
│   │   ├── FirstBootScriptBuilder.cs  SetupComplete.cmd + set-computername.ps1 (serial sanitize, offline app loop)
│   │   ├── OfficeOdtService.cs  ODT configuration.xml (XDocument), offline download (cached), stage to Payload/Office
│   │   ├── AppCatalogService.cs  Load Assets/app-catalog.json, add user installers, acquire + SHA-256 verify, stage to Payload/Apps
│   │   ├── HeadlessBuildRunner.cs  Parse CLI args (--build, --iso, --out, --edition, --debloat, --office, --apps)
│   │   ├── BuildOrchestrator.cs  10-step pipeline: detect tools → extract → ESD→WIM → debloat → unattend → Office → apps → scripts → repack → cleanup
│   │   └── Dism/
│   │       └── DismOutputParser.cs  Pure-text parsing (no subprocess); unit-tested with fixtures
│   ├── ViewModels/             7 view-model classes (CommunityToolkit.Mvvm)
│   │   ├── WizardStepViewModel.cs  Base: Title, CanProceed, async OnNext (if override)
│   │   ├── MainViewModel.cs    Wizard shell: nav, tool detection, live log marshaling, Build command
│   │   ├── SelectIsoViewModel.cs  File picker, media validation
│   │   ├── WindowsCustomizeViewModel.cs  Edition dropdown, bypass toggles, admin user/pass, appx checklist
│   │   ├── OfficeViewModel.cs  Enable toggle, language/bitness/modules
│   │   ├── AppsViewModel.cs    Catalog checkboxes + add-custom-installer (name/path/flags)
│   │   └── ReviewBuildViewModel.cs  Summary display, Build button, progress binding
│   ├── Views/                  6 XAML UserControls + MainWindow
│   │   ├── MainWindow.xaml     Window chrome, DataTemplate→ViewModel mapping
│   │   ├── SelectIsoView.xaml  File picker UI
│   │   ├── WindowsCustomizeView.xaml  Bypass toggles, admin creds, appx list
│   │   ├── OfficeView.xaml     Enable, language, bitness, module excludes
│   │   ├── AppsView.xaml       Catalog grid, Add app modal
│   │   ├── ReviewBuildView.xaml  Summary, log viewer, Build/Cancel buttons
│   │   └── App.xaml            Global DataTemplate definitions
│   ├── Assets/                 JSON catalog, first-boot templates
│   │   ├── app-catalog.json    7 preset apps (Chrome, Firefox, 7-Zip, VLC, Notepad++, Zalo, Unikey)
│   │   ├── SetupComplete.cmd   Template: SYSTEM context, payload discovery, app loop
│   │   └── set-computername.ps1  Template: serial→sanitize→NetBIOS or WIN-xxxxxx fallback
│   ├── tools/                  (gitignored) Large Microsoft redistributables
│   │   ├── oscdimg/            oscdimg.exe (from Windows ADK, amd64)
│   │   └── odt/                Office Deployment Tool setup.exe + source cache
│   └── Win11IsoBuilder.csproj  net8.0-windows, requireAdministrator, x64 platform
└── Win11IsoBuilder.Tests/
    ├── Win11IsoBuilder.Tests.csproj  net8.0-windows, references main project
    ├── DismOutputParserTests.cs  6 tests: parse provision list, get-imageinfo, error output
    ├── UnattendBuilderTests.cs   5 tests: LabConfig keys, local account, locale/timezone
    ├── FirstBootScriptBuilderTests.cs  4 tests: serial sanitize, script templates, offline app loop
    ├── OfficeOdtServiceTests.cs  4 tests: configuration.xml structure, lang/bitness
    ├── AppCatalogServiceTests.cs  8 tests: JSON load, user install, SHA-256 verify
    ├── ToolDetectionServiceTests.cs  3 tests: DISM detect, oscdimg bundled/ADK fallback
    └── TestDoubles.cs           Mock LogService, ProcessRunner
```

## Code Statistics

- **Total lines:** ~2783 LOC (source only, excluding tests, assets, obj/)
- **Largest file:** 163 LOC (BuildOrchestrator)
- **Services:** All <200 LOC per file (modular design)
- **Unit tests:** 35 tests across 7 test classes, all passing
- **CI:** GitHub Actions (windows-latest, .NET 8, build + test on push/PR)

## Key Components & Responsibilities

| Layer | Component | Key Methods |
|-------|-----------|-----------|
| **Pipeline** | `BuildOrchestrator` | `RunAsync(BuildConfig, IProgress, CancellationToken)` — orchestrates 10 steps with robust cleanup. |
| **Process** | `ProcessRunner` | `RunAsync(filename, args, timeout, cancel, lineCallback)` — DRY subprocess wrapper for dism, oscdimg, setup.exe, robocopy. |
| **ISO** | `IsoService` | `ExtractIsoAsync`, `ValidateMedia`, `RepackIsoAsync` (oscdimg dual-boot). Warns if >4 GB. |
| **WIM** | `WimService` | `GetImageInfoAsync`, `EnsureEditableWimAsync` (ESD→WIM), `MountWimAsync`, `UnmountWimAsync`, `GetProvisionedAppxAsync/RemoveProvisionedAppxAsync`, `CleanupOrphanMountsAsync`. |
| **Unattend** | `UnattendBuilder` | `Build()` → XDocument (LabConfig, local account, locale, timezone, keyboard, ComputerName="*", no partitioning). |
| **First Boot** | `FirstBootScriptBuilder` | `BuildSetupCompleteCmd`, `BuildComputerNameScript` (serial sanitize to ≤15 chars, fallback WIN-xxxxxx). |
| **Office** | `OfficeOdtService` | `DownloadOfflineAsync()` → XDocument configuration.xml, ODT offline /download, stage to Payload/Office. |
| **Apps** | `AppCatalogService` | `LoadCatalog()` (JSON), `AcquireInstallerAsync` (download + SHA-256 verify), `StageToPayloadAsync`. |
| **Tools** | `ToolDetectionService` | `Detect()` → ToolPaths (DISM, oscdimg bundled-first then ADK, IsAdkMissing flag). |
| **CLI** | `HeadlessBuildRunner` | `RunAsync(args)` — parses --build --iso --out [--name] [--edition] [--debloat] [--office] [--apps]. |
| **Log** | `LogService` | `ILogSink` impl; event + file output. |
| **UI** | `MainViewModel` + 5 step VMs | MVVM wizard, step validation, live log binding, async Build + Cancel. |

## Design Patterns & Conventions

- **Single source of truth:** `BuildConfig` shared across all services; derived paths computed from it.
- **Safe cleanup:** WIM mount always unmounted in finally block; orphaned-mount cleanup at startup.
- **Async/await:** All I/O operations use async (except sync file ops like XDocument.Save).
- **Logging:** Services log via `ILogSink` (no direct Console); UI binds to LogService events.
- **UI-agnostic:** Services know nothing about WPF; can be reused by CLI headless runner.
- **XML construction:** XDocument (well-formed, escaped) for autounattend.xml + ODT configuration.xml.
- **Shell artifacts:** Text templates for first-boot .cmd/.ps1 (Assets/), substituted at runtime.
- **File size limit:** Each .cs < ~200 LOC for cognitive load and context window.

## Known Issues & Fixes Applied

**3 production bugs found via real ISO builds (2026-05-30) and fixed:**

1. **Read-only WIM mount failure** — ISO copy set install.wim read-only → DISM mount error 0xc1510111.
   - **Fix:** `robocopy /A-:R` clears read-only before mount; verified in WimService.

2. **Read-only media blocks cleanup** — After mount, media attributes prevent file deletion.
   - **Fix:** ClearReadOnly utility called before cleanup phase.

3. **Transient lock on boot files during cleanup** — etfsboot/efisys occasionally locked.
   - **Fix:** Retry with exponential backoff; retry loop in cleanup.

All three verified by repeated real builds on Windows 11 25H2 ISO.

## Testing & Validation

- **Unit tests:** 35 tests (builders, parsers, services); all passing.
  - Fixtures include sample DISM output (provision list, get-imageinfo, errors).
  - Builders tested without filesystem (pure XML/string generation).
  - Parsers unit-tested (text parsing only).
- **AC-1 (build without error):** Proven via real builds (7.9 GB debloat-only, 11.63 GB Office+apps).
- **AC-2..AC-6 (runtime):** Manual playbook in `vm-smoke-test-playbook.md` (boot VM, verify unattended, local account, serial name, offline apps, Office sign-in).

## Before a Real Build

Ensure bundled tools are present (gitignored, not committed):

| Tool | Location | Source |
|------|----------|--------|
| `oscdimg.exe` (amd64) | `Win11IsoBuilder/tools/oscdimg/oscdimg.exe` | Windows ADK → Deployment Tools |
| ODT `setup.exe` | `Win11IsoBuilder/tools/odt/setup.exe` | [Microsoft Office Deployment Tool](https://www.microsoft.com/en-us/download/details.aspx?id=49117) |

If `oscdimg` is missing, ToolDetectionService will look in the ADK registry location and fail if not found.

## CI/CD Status

- **Build:** `dotnet build Win11IsoBuilder.sln -c Release` ✅ passes (zero warnings)
- **Test:** `dotnet test Win11IsoBuilder.Tests/Win11IsoBuilder.Tests.csproj -c Release` ✅ 35/35 pass
- **GitHub Actions:** `.github/workflows/ci.yml` runs on windows-latest (push/PR to main); build + test in ~4 min

## Quick Links

- **Full requirements:** [`prd.md`](../prd.md) (Vietnamese) or [`project-overview-pdr.md`](project-overview-pdr.md) (English)
- **System architecture:** [`system-architecture.md`](system-architecture.md)
- **Code standards:** [`code-standards.md`](code-standards.md)
- **Deployment:** [`deployment-guide.md`](deployment-guide.md)
- **Acceptance testing:** [`vm-smoke-test-playbook.md`](vm-smoke-test-playbook.md)
- **Project roadmap:** [`project-roadmap.md`](project-roadmap.md)

---

**Status:** Production-ready (all 8 phases complete, AC-1 validated, 35/35 tests pass, public GitHub repo)
**Last Updated:** 2026-05-31
