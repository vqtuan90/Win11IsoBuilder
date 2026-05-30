# Codebase Summary — Windows 11 Custom ISO Builder

.NET 8 + WPF desktop app (admin-elevated, x64, Windows-only) that turns a user-supplied
Windows 11 ISO into a customized, unattended install ISO with offline Microsoft 365 + apps.
See `../prd.md` for full requirements.

## Solution layout

```
Win11IsoBuilder.sln
├── Win11IsoBuilder/            (WPF app, net8.0-windows, requireAdministrator)
│   ├── Models/                 POCO contracts (BuildConfig is the single source of truth)
│   ├── Services/               business logic (no UI deps)
│   │   └── Dism/               DISM text-output parsing
│   ├── ViewModels/             CommunityToolkit.Mvvm wizard VMs (5 steps + shell)
│   ├── Views/                  XAML UserControls per step
│   ├── Assets/                 app-catalog.json, first-boot .cmd/.ps1 templates
│   └── tools/                  (gitignored) bundled oscdimg.exe + ODT setup.exe — add locally
└── Win11IsoBuilder.Tests/      xUnit (35 tests; builders/parsers/services)
```

## Key components

| Layer | Type | Responsibility |
|-------|------|----------------|
| Pipeline | `BuildOrchestrator` | 10-step build: detect tools → extract → ESD→WIM → debloat → unattend → Office → apps → first-boot scripts → repack → cleanup. Cancellable; discards WIM mount on failure. |
| Process | `ProcessRunner` | DRY async wrapper for dism/oscdimg/setup.exe/robocopy: capture stdout/stderr/exit, timeout, cancellation, live-line callback. |
| ISO | `IsoService` | Mount (poll for drive letter) + robocopy extract; validate media; oscdimg dual-boot (UEFI+BIOS) repack; >4 GB warning. |
| WIM | `WimService` | DISM Get-ImageInfo, ESD→WIM export (returns effective index), mount/unmount (try/finally), provisioned-appx list/remove, orphan-mount cleanup. |
| Unattend | `UnattendBuilder` | autounattend.xml via XDocument: LabConfig bypass keys, local admin, locale/timezone, ComputerName="*". No disk partitioning (Setup shows drive picker). |
| First boot | `FirstBootScriptBuilder` | SetupComplete.cmd + set-computername.ps1 (serial sanitize → NetBIOS, fallback WIN-xxxxxx), offline app loop (msi→msiexec, exe direct). |
| Office | `OfficeOdtService` | ODT configuration.xml (XDocument), offline `/download` (cached), stage to payload. Legal ODT only — no activation bypass. |
| Apps | `AppCatalogService` | Load catalog JSON, add user installers, acquire (download/copy + optional SHA-256), stage to payload, emit install commands. |
| Tools | `ToolDetectionService` | Resolve DISM (System32) + oscdimg (bundled-first, ADK fallback); flag `IsAdkMissing`. |
| UI | `MainViewModel` + 5 step VMs | Wizard nav gated by per-step `CanProceed`; live log marshaled to UI thread; async Build with Cancel. |

## Conventions

- One `BuildConfig` flows through every service; derived workspace paths computed from it.
- Each file < ~200 lines; services are UI-agnostic and log through `ILogSink`.
- XML artifacts built with XDocument (well-formed/escaped); shell artifacts via text templates.

## Status

Build clean (0/0); 35/35 unit tests pass. Runtime acceptance (boot a VM, AC-1..AC-6) is manual —
see `vm-smoke-test-playbook.md`. Before a real build, drop `oscdimg.exe` and ODT `setup.exe`
under `Win11IsoBuilder/tools/`.
