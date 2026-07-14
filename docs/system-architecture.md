# System Architecture — Windows 11 Custom ISO Builder

## Overview

The Windows 11 Custom ISO Builder is a layered .NET 8 + WPF desktop application that orchestrates a complex multi-step transformation pipeline: from a user-supplied Windows 11 ISO → through DISM-based image editing → to a customized, unattended-install ISO with embedded Microsoft 365 Apps and third-party applications.

The architecture emphasizes:
- **Separation of concerns:** UI (WPF/MVVM) → ViewModels → Services (UI-agnostic) → External Tools
- **Single source of truth:** `BuildConfig` object flows through all services
- **Safe resource cleanup:** WIM mount always released; orphaned mounts cleaned at startup
- **Dual execution modes:** Interactive (GUI) and headless (CLI)
- **Observable progress:** Live process output callbacks enable real-time UI updates

---

## Layered Architecture

```
┌──────────────────────────────────────────────────────────────┐
│ Presentation Layer (WPF + MVVM)                              │
│ ┌─────────────────────────────────────────────────────────┐  │
│ │ MainWindow (XAML)                                       │  │
│ │ ├─ SelectIsoView                                        │  │
│ │ ├─ WindowsCustomizeView                                │  │
│ │ ├─ OfficeView                                          │  │
│ │ ├─ AppsView                                            │  │
│ │ ├─ ReviewBuildView                                     │  │
│ │ └─ Live Log (log viewer, progress bar)                 │  │
│ └─────────────────────────────────────────────────────────┘  │
│                      ↓ ICommand binding                       │
│ ┌─────────────────────────────────────────────────────────┐  │
│ │ ViewModel Layer (CommunityToolkit.Mvvm)                 │  │
│ │ ┌─────────────────────────────────────────────────────┐ │  │
│ │ │ MainViewModel (wizard shell, nav, tool detection)   │ │  │
│ │ ├─ SelectIsoViewModel (ISO picker, media validate)    │ │  │
│ │ ├─ WindowsCustomizeViewModel (toggles, appx list)     │ │  │
│ │ ├─ OfficeViewModel (enable, lang, bitness, modules)   │ │  │
│ │ ├─ AppsViewModel (catalog, custom installer add)      │ │  │
│ │ └─ ReviewBuildViewModel (summary, Build command)      │ │  │
│ │ └─ Live log binding, IProgress<BuildProgress>         │ │  │
│ └─────────────────────────────────────────────────────────┘  │
│                      ↓ async Build(BuildConfig, progress)   │
└──────────────────────────────────────────────────────────────┘
                            ↓
┌──────────────────────────────────────────────────────────────┐
│ Business Logic Layer (Services + Orchestrator)               │
│ ┌─────────────────────────────────────────────────────────┐  │
│ │ BuildOrchestrator (10-step pipeline orchestrator)       │  │
│ │ ├─ Detects tools, validates ISO                        │  │
│ │ ├─ Extracts & mounts WIM                               │  │
│ │ ├─ Removes appx, builds autounattend.xml               │  │
│ │ ├─ Stages Office + app payloads                        │  │
│ │ ├─ Writes first-boot scripts                           │  │
│ │ ├─ Repacks ISO (oscdimg dual-boot)                     │  │
│ │ └─ Cleans up (WIM unmount, temp dirs, file read-only) │  │
│ └─────────────────────────────────────────────────────────┘  │
│                      ↓ uses                                    │
│ ┌─────────────────────────────────────────────────────────┐  │
│ │ Domain Services (all <200 LOC, stateless)              │  │
│ │ ├─ ToolDetectionService      (DISM, oscdimg locate)    │  │
│ │ ├─ ProcessRunner             (async subprocess wrapper) │  │
│ │ ├─ LogService                (ILogSink impl)           │  │
│ │ ├─ IsoService                (mount/extract/repack)    │  │
│ │ ├─ WimService                (DISM mount/edit/appx)    │  │
│ │ ├─ UnattendBuilder            (XML autounattend)       │  │
│ │ ├─ FirstBootScriptBuilder    (SetupComplete.cmd/ps1)  │  │
│ │ ├─ OfficeOdtService          (ODT config.xml)         │  │
│ │ ├─ AppCatalogService         (catalog JSON)            │  │
│ │ ├─ DismOutputParser          (pure text parsing)       │  │
│ │ └─ HeadlessBuildRunner       (CLI entry point)         │  │
│ └─────────────────────────────────────────────────────────┘  │
│                      ↓ pass                                    │
│ ┌─────────────────────────────────────────────────────────┐  │
│ │ Data Model (POCOs, single source of truth)             │  │
│ │ ├─ BuildConfig        (user selections + derived paths)│  │
│ │ ├─ ToolPaths          (resolved DISM/oscdimg/ODT)     │  │
│ │ ├─ WindowsEdition     (index, name)                    │  │
│ │ ├─ WinCustomizationOptions  (toggles, appx list)     │  │
│ │ ├─ OfficeOptions      (enabled, lang, bitness)        │  │
│ │ ├─ AppEntry           (name, URL, silent flags)       │  │
│ │ ├─ AppInstallCommand  (resolved installer + args)     │  │
│ │ ├─ BuildProgress      (% complete, stage, message)    │  │
│ │ └─ LogEntry           (level, timestamp, message)     │  │
│ └─────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
                            ↓
┌──────────────────────────────────────────────────────────────┐
│ Infrastructure Layer (External Tools & Files)                │
│ ├─ Windows DISM (System32\dism.exe)  [DISM mount/unmount]    │
│ ├─ oscdimg.exe [bundled or ADK]      [ISO repack, dual-boot] │
│ ├─ Office Deployment Tool setup.exe  [ODT offline /download] │
│ ├─ Filesystem (WorkDir, CacheDir)    [temp mounts, extracts] │
│ └─ Windows Registry                  [ADK path detection]    │
└──────────────────────────────────────────────────────────────┘
```

---

## 10-Step Build Pipeline (BuildOrchestrator)

The **BuildOrchestrator** is the heart of the application. It orchestrates a 10-step, cancellable, progress-reporting pipeline with robust cleanup on failure.

```
Start
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 1: Detect Tools                                        │
│ ToolDetectionService.Detect() → ToolPaths                  │
│ Find DISM (System32), oscdimg (bundled or ADK)             │
│ Throw if DISM missing; warn if ADK missing                 │
└─────────────────────────────────────────────────────────────┘
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 2: Cleanup Orphaned Mounts                             │
│ WimService.CleanupOrphanMountsAsync()                       │
│ Handle previous crashed runs (stuck DISM mounts)           │
└─────────────────────────────────────────────────────────────┘
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 3: Validate Source ISO                                 │
│ Check file exists, prepares WorkDir/CacheDir               │
└─────────────────────────────────────────────────────────────┘
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 4: Extract ISO → MediaDir                              │
│ IsoService.ExtractIsoAsync(source → media/)                │
│ robocopy /E /R:1 /W:1 (copy with minimal retry)            │
│ ValidateMedia() checks for sources/install.wim or .esd     │
└─────────────────────────────────────────────────────────────┘
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 5: Ensure Editable WIM (ESD → WIM export if needed)   │
│ WimService.EnsureEditableWimAsync()                        │
│ If install.esd: DISM export to install.wim (index→1)       │
│ Return (wimPath, effective_index)                          │
└─────────────────────────────────────────────────────────────┘
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 6: Mount WIM, Remove Appx, Commit                      │
│ WimService.MountAsync(wimPath, index, mountDir)            │
│ WimService.GetProvisionedAppxAsync/RemoveProvisionedAppxAsync(cfg.AppxToRemove)        │
│ try { ... } finally { UnmountAsync() }                      │
│ Mount always unmounted (try/finally guarantee)              │
└─────────────────────────────────────────────────────────────┘
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 7: Build Autounattend.xml, Stage Office Payload        │
│ UnattendBuilder.Build() → XDocument (LabConfig, local acc)  │
│ OfficeOdtService.DownloadOfflineAsync() → Office source in cache   │
│ Copy Office cached → media/Payload/Office/                  │
└─────────────────────────────────────────────────────────────┘
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 8: Stage App Payload                                   │
│ AppCatalogService.StageToPayloadAsync()                       │
│ For each app: download/copy installer → media/Payload/Apps │
│ Verify SHA-256 if pinned                                    │
└─────────────────────────────────────────────────────────────┘
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 9: Write First-Boot Scripts                            │
│ FirstBootScriptBuilder.BuildSetupCompleteCmd()         │
│ FirstBootScriptBuilder.BuildComputerNameScript()    │
│ Substitute app list, serial → NetBIOS sanitization          │
│ Write → media/$OEM$/1$/Setup/Scripts/SetupComplete.cmd      │
└─────────────────────────────────────────────────────────────┘
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Step 10: Repack ISO (oscdimg dual-boot) + Cleanup           │
│ IsoService.RepackIsoAsync() → oscdimg with -bootdata:2      │
│ UEFI + legacy BIOS support; output to OutputDirectory       │
│ Finally: clear read-only, unmount if mounted, delete WorkDir│
└─────────────────────────────────────────────────────────────┘
  ↓
Success (exit 0) or
Error → Cleanup WIM mount, try-finally ensures cleanup always runs
```

**Key guarantees:**
- WIM mount always released (try/finally in step 6)
- Failure cleanup (discard mount, delete temp dirs) runs on any error
- Cancellation token respected throughout; graceful stop possible
- Progress reported at each step via `IProgress<BuildProgress>`

---

## Data Flow: BuildConfig as Single Source of Truth

```
User Input (GUI or CLI)
  ↓
  ├─ SourceIsoPath
  ├─ SelectedEdition (index, name)
  ├─ Windows (BypassTPM, BypassSecureBoot, LocalAccount, AppxToRemove)
  ├─ Office (Enabled, Language, Bitness, ExcludeModules)
  ├─ SelectedApps (app catalog + user-supplied installers)
  ├─ OutputDirectory, OutputIsoName
  └─ WorkDir, CacheDir (defaults: %TEMP%\Win11IsoBuilder\)
       ↓
       ↓ BuildConfig object created
       ↓
┌─────────────────────────────────────────────────────────┐
│ BuildOrchestrator.RunAsync(BuildConfig, progress, ct) │
├─────────────────────────────────────────────────────────┤
│ ToolDetectionService.Detect()                           │
│ → cfg.Tools (DISM, oscdimg paths)                       │
│                                                         │
│ IsoService.ExtractIsoAsync(cfg.SourceIsoPath, cfg.MediaDir) │
│ → MediaDir populated with ISO tree                      │
│                                                         │
│ WimService.EnsureEditableWimAsync(cfg.MediaDir/sources) │
│ → Updates cfg.SelectedEdition.Index if ESD exported     │
│                                                         │
│ WimService.Mount(cfg.MountDir)                          │
│ → Mounts cfg.MediaDir/sources/install.wim               │
│                                                         │
│ UnattendBuilder.Build(cfg.Windows)                      │
│ → autounattend.xml written to cfg.MediaDir/            │
│                                                         │
│ OfficeOdtService.DownloadOfflineAsync(cfg.Office)              │
│ → Office source in cfg.CacheDir, copy to cfg.PayloadDir │
│                                                         │
│ AppCatalogService.StageToPayloadAsync(cfg.SelectedApps)  │
│ → Download/copy apps to cfg.PayloadDir                 │
│                                                         │
│ FirstBootScriptBuilder (cfg.Windows.LocalAccount, apps) │
│ → SetupComplete.cmd written to cfg.MediaDir/$OEM$/1$   │
│                                                         │
│ IsoService.RepackIsoAsync(cfg.MediaDir, cfg.OutputPath) │
│ → oscdimg repack; final ISO in cfg.OutputIsoPath       │
└─────────────────────────────────────────────────────────┘
       ↓
       ↓ Output ISO
       ↓
User burns ISO to USB or boots in VM
```

**Design principle:** No global state; all shared data lives in `BuildConfig`. Services are stateless; they accept config, perform work, optionally update config, return results.

---

## File System Layout (WorkDir & MediaDir)

```
%TEMP%\Win11IsoBuilder\
├── work/                        (WorkDir, cleaned after each build)
│   ├── media/                   (MediaDir, extracted ISO + modifications)
│   │   ├── sources/
│   │   │   ├── install.wim      (mounted, edited: appx removed)
│   │   │   └── boot/
│   │   ├── bootmgr              (legacy BIOS boot files)
│   │   ├── efi/                 (UEFI boot files)
│   │   ├── autounattend.xml     (generated)
│   │   ├── $OEM$/
│   │   │   └── 1$/              (first-boot files)
│   │   │       └── Setup/Scripts/
│   │   │           ├── SetupComplete.cmd
│   │   │           └── set-computername.ps1
│   │   └── Payload/             (offline installation packages)
│   │       ├── Office/          (M365 offline source)
│   │       │   ├── Office/
│   │       │   │   ├── Data/
│   │       │   │   ├── configuration.xml
│   │       │   │   └── setup.exe
│   │       │   └── [cached ODT download tree]
│   │       └── Apps/            (app installers)
│   │           ├── Chrome-latest.exe
│   │           ├── Firefox-XX.exe
│   │           ├── 7z-XX-x64.exe
│   │           ├── VLC-XX-win64.exe
│   │           ├── notepadplusplus-XX-installer.exe
│   │           ├── Zalo-installer.exe
│   │           └── UniKey-installer.exe
│   │
│   └── mount/                   (DISM WIM mount point, temporary)
│       └── [mounted install.wim ← cleaned up after use]
│
└── cache/                       (CacheDir, survives builds)
    ├── Office/                  (ODT offline source, ~3.5 GB)
    │   ├── Office/
    │   │   ├── Data/
    │   │   └── setup.exe
    │   └── [ODT extracted tree]
    └── Apps/                    (downloaded app installers, ~500 MB)
        ├── Chrome-latest.exe
        ├── Firefox-XX.exe
        └── [cached downloads]
```

**Key points:**
- **work/** deleted after successful build (or partial cleanup on failure)
- **mount/** always unmounted (via try/finally); orphaned mounts cleaned at startup
- **cache/** preserved; reused by subsequent builds (fast Office re-downloads, apps already downloaded)
- **Payload/** is the offline installation tree embedded in the final ISO

---

## First-Boot Execution Sequence (On Target Machine)

When the built ISO boots on a target machine and reaches Setup Complete phase:

```
1. Setup loads the autounattend.xml from media root
   ├─ LabConfig keys bypass TPM/SecureBoot/RAM checks
   ├─ WinPE runs auto-partition.cmd from the media (zero-touch default):
   │  detects firmware (PEFirmwareType) → diskpart wipes Disk 0 → GPT (UEFI) or MBR (BIOS) layout
   ├─ LocalAccount (admin) auto-created
   ├─ Region, timezone, keyboard applied
   ├─ ComputerName="*" (placeholder; will be set at first-boot)
   └─ ImageInstall applies the chosen edition + AcceptEula (no prompts)

2. Zero-touch OFF (--no-auto-partition): Setup shows the drive picker instead
   ├─ User selects a disk to install to
   └─ Setup then proceeds without further prompts

3. OOBE (Out-of-Box Experience)
   ├─ Network setup (auto)
   ├─ Windows Update begins (auto)
   └─ No Microsoft account prompt (local admin used instead)

4. First Boot → SetupComplete.cmd runs (SYSTEM context)
   ├─ Located: %WINDIR%\Setup\Scripts\SetupComplete.cmd
   ├─ **Triggered two ways** (script is idempotent — guarded by a
   │  %WINDIR%\Setup\Scripts\.setupcomplete.done marker so only one runs):
   │  ├─ Setup's native auto-run — but Windows Setup SILENTLY SKIPS this on
   │  │  any edition with a firmware-embedded OEM product key (the case on
   │  │  almost every retail/prebuilt PC), except Enterprise/Server
   │  │  (see Microsoft Learn "Add a Custom Script to Windows Setup")
   │  └─ specialize-pass RunSynchronousCommand added to autounattend.xml
   │     (UnattendBuilder.RunSetupCompleteCommand) — explicitly invokes the
   │     same script; this trigger is unaffected by the OEM-key restriction,
   │     so it is the reliable path on real hardware
   ├─ **set-computername.ps1 runs first**
   │  ├─ Queries BIOS serial: Get-WmiObject Win32_SystemEnclosure
   │  ├─ Sanitizes: alphanumeric + hyphen, ≤15 chars
   │  ├─ Fallback: WIN-xxxxxx if serial blank or invalid
   │  ├─ Applies: Rename-Computer -NewName $sanitized
   │  └─ Logs to: %WINDIR%\Setup\Scripts\win11builder-firstboot.log
   │
   └─ **App installation loop**
      ├─ Discovers Payload drive letter (offline media)
      ├─ For each .msi: msiexec /i /qn /norestart
      ├─ For each .exe: .\installer.exe [silent flags]
      ├─ Logs each installer's exit code
      │
      └─ If Office enabled:
         ├─ Payload/Office/setup.exe /configure configuration.xml
         ├─ M365 Apps installed offline (no network required)
         ├─ First-run prompts to sign in for activation
         └─ No KMS/crack; user responsibility

5. System restarts (if setup.exe requires it)

6. User logs in
   ├─ Desktop ready with Computer Name = BIOS Serial
   ├─ Office + apps pre-installed offline
   └─ No internet required during installation
```

---

## Service Dependency Graph

```
BuildOrchestrator (orchestrator, main)
├─ depends on: ToolDetectionService
├─ depends on: IsoService
│              ├─ depends on: ProcessRunner
│              └─ depends on: LogService
├─ depends on: WimService
│              ├─ depends on: ProcessRunner
│              ├─ depends on: DismOutputParser
│              └─ depends on: LogService
├─ depends on: UnattendBuilder
├─ depends on: FirstBootScriptBuilder
├─ depends on: OfficeOdtService
│              ├─ depends on: ProcessRunner
│              └─ depends on: LogService
├─ depends on: AppCatalogService
│              └─ depends on: LogService
└─ depends on: LogService

HeadlessBuildRunner (CLI entry point)
├─ creates: BuildConfig (from CLI args)
├─ creates: LogService
├─ calls: ToolDetectionService
├─ calls: BuildOrchestrator
└─ returns: exit code (0 on success, non-zero on error)

MainViewModel (GUI orchestrator)
├─ creates: LogService (shared for live log)
├─ creates: BuildConfig (from user selections)
├─ calls: BuildOrchestrator (async, on UI thread context)
├─ reports: IProgress<BuildProgress> (updates UI in real-time)
└─ handles: CancellationToken (Cancel button)
```

---

## Concurrency & Thread Safety

### Single-Threaded by Design

The application is **single-threaded within a build**:
- GUI thread calls `BuildOrchestrator.RunAsync()`
- All services execute on the task threadpool (no special synchronization)
- WIM mount is a **resource owned by WimService**; only one service instance touches it at a time
- No race conditions because BuildOrchestrator is sequential

### UI Thread Marshaling

**LogService publishes events:**
```csharp
public event EventHandler<LogEntry>? EntryLogged;
```

**MainViewModel subscribes:**
```csharp
_log.EntryLogged += (_, e) => 
    UIThread.Invoke(() => LogItems.Add(e));  // Marshal to UI thread
```

This allows real-time log updates without blocking the orchestrator.

---

## Error Handling & Recovery

### Failure Modes & Recovery

| Scenario | Handling |
|----------|----------|
| **Source ISO missing** | FileNotFoundException in step 3; fail fast. |
| **DISM not found** | Throw in step 1; GUI shows error dialog. |
| **oscdimg missing** | ToolDetectionService flags `IsAdkMissing`; suggest install ADK or bundle oscdimg. |
| **WIM mount fails** | Exception in step 6; try/finally ensures no stuck mount. |
| **Appx removal fails** | Log warning; continue (non-blocking). |
| **Office download timeout** | ProcessRunner timeout exception; fail build with clear error. |
| **App installer 404** | ProcessRunner captures exit code; log failure; continue to next app. |
| **Cleanup fails** (read-only file) | Retry with backoff; finally-block ensures retry happens even on error. |
| **User cancels** | CancellationToken triggers; finally-block cleans up in-progress work. |

### Recovery Strategies

1. **Orphaned Mount Cleanup (Startup):**
   ```csharp
   // Step 2 of pipeline
   await wim.CleanupOrphanMountsAsync(cfg.MountDir, ct);
   ```
   Handles previous crashed runs where DISM mount wasn't released.

2. **Read-Only Attribute Clearing (Before Cleanup):**
   ```csharp
   // Finally block in BuildOrchestrator
   ForceDeleteDirectory(cfg.WorkDir);  // Retries with /A-:R
   ```

3. **Retry with Backoff (Transient Locks):**
   ```csharp
   // In cleanup phase
   await Retry.WithExponentialBackoff(
       () => File.Delete(bootFile),
       maxAttempts: 5,
       delayMs: 100
   );
   ```

---

## Execution Paths: GUI vs. CLI

### GUI Path

```
User launches Win11IsoBuilder.exe
  ↓
App.xaml.cs → MainWindow (DataContext=MainViewModel)
  ↓
MainViewModel initializes
  ├─ ToolDetectionService.Detect() (async at startup)
  └─ LogService created (listens to events)
  ↓
User clicks through 5 steps (each step validates inputs)
  ├─ Step 1: SelectIsoView (file picker)
  ├─ Step 2: WindowsCustomizeView (toggles, appx list, admin creds)
  ├─ Step 3: OfficeView (enable/disable, language, bitness)
  ├─ Step 4: AppsView (catalog checkboxes, add custom)
  └─ Step 5: ReviewBuildView (summary, Build button)
  ↓
User clicks "Build ISO"
  ├─ ReviewBuildViewModel.Build() command executes
  ├─ BuildOrchestrator.RunAsync(cfg, progress, ct) starts
  ├─ Progress updates flow to UI (log viewer updates in real-time)
  ├─ User can click Cancel at any time
  └─ On completion: success message or error shown
```

### CLI (Headless) Path

```
User runs: Win11IsoBuilder.exe --build --iso <p> --out <dir> [--office] [--apps ...]
  ↓
Program.Main() calls HeadlessBuildRunner.RunAsync(args)
  ↓
HeadlessBuildRunner.Parse(args)
  ├─ Extract --iso, --out, --name, --edition, --debloat, --office, --apps
  └─ Build BuildConfig from CLI options
  ↓
HeadlessBuildRunner.BuildConfigFrom(opts)
  ├─ Create new BuildConfig with defaults (WorkDir, CacheDir)
  ├─ Set SourceIsoPath, OutputDirectory, SelectedEdition
  ├─ Parse debloat list → Windows.AppxToRemove
  ├─ Parse office flag → Office.Enabled
  └─ Parse apps list → load from app-catalog.json + user-supplied
  ↓
BuildOrchestrator.RunAsync(cfg, progress, ct)
  ├─ All 10 steps same as GUI
  ├─ Progress printed to Console (only Warn/Error levels)
  └─ On error, exit with non-zero code
  ↓
Return exit code (0 = success, non-zero = failure)
```

---

## Deployment Architecture

The built `.iso` contains:

```
.iso (repackaged with oscdimg)
├── [UEFI boot partition]
│   ├── efi/Boot/bootx64.efi
│   └── efi/Microsoft/Boot/...
├── [Legacy BIOS boot partition]
│   ├── bootmgr
│   └── Boot/...
├── sources/
│   ├── install.wim (debloated, appx removed, edited)
│   ├── boot.wim
│   └── ...
├── autounattend.xml (LabConfig bypass, local account, ComputerName=*)
├── $OEM$/1$/
│   └── Setup/Scripts/
│       ├── SetupComplete.cmd (runs at first-boot)
│       └── set-computername.ps1 (sets name = BIOS serial)
└── Payload/
    ├── Office/
    │   ├── Office/ (M365 offline source tree)
    │   ├── configuration.xml
    │   └── setup.exe
    └── Apps/
        ├── Chrome-latest.exe
        ├── Firefox-XX.exe
        ├── 7z-XX-x64.exe
        ├── VLC-XX-win64.exe
        ├── notepadplusplus-XX-installer.exe
        ├── Zalo-installer.exe
        └── UniKey-installer.exe
```

**Usage:**
1. User burns ISO to USB with Rufus (NTFS, UEFI + BIOS mode).
2. Boot on target machine; Setup proceeds unattended.
3. First-boot scripts install Office + apps offline.
4. Computer name set from BIOS serial.

---

## Security & Trust Model

### No Activation Bypass

- **Office:** Legal ODT `/download` only; user signs in to activate (no KMS/MAK/crack).
- **Windows:** LabConfig keys are standard community practice for testing/legacy hardware; they don't bypass activation.

### Resource Safety

- WIM mount always released (try/finally).
- Temp files cleaned up after build.
- No credentials stored in ISO or code.

### Public Repository

- No secrets in source code.
- oscdimg and ODT are bundled locally (gitignored); license terms documented.
- CI running on GitHub (build + test on windows-latest).

---

## Performance Characteristics

| Operation | Typical Duration | Notes |
|-----------|------------------|-------|
| **Tool detection** | <1 sec | Registry lookup, file existence checks. |
| **ISO extraction** | 5–15 min | Depends on ISO size (7–12 GB); robocopy /E. |
| **ESD→WIM export** | 3–5 min | If needed; DISM export-image. |
| **WIM mount** | 1–2 sec | Immediate; DISM mount time. |
| **Appx removal** | 2–5 min | Depends on count; DISM remove-appx per item. |
| **autounattend.xml** | <1 sec | XDocument construction + write. |
| **Office ODT /download** | 20–40 min | **First build only**; cached after. |
| **App downloads** | 5–15 min | Network dependent; cached after. |
| **ISO repack (oscdimg)** | 5–10 min | oscdimg with dual-boot; depends on final ISO size. |
| **Cleanup** | 1–3 min | Delete WorkDir, unmount WIM. |
| **Total (full Office + 5 apps)** | 60–90 min | First build. Subsequent: 40–50 min (cached Office/apps). |

---

## Extensibility Points

### Adding a New Service

1. Create `Services/NewDomainService.cs` (<200 LOC).
2. Accept `ILogSink` for logging.
3. Make it stateless; accept `BuildConfig` or relevant input POCO.
4. Implement async methods; use `ConfigureAwait(false)`.
5. Return results as new POCO or update `BuildConfig`.

**Example: Custom registry injection service**
```csharp
public class RegistryPatchService
{
    private readonly ILogSink _log;

    public RegistryPatchService(ILogSink log) => _log = log;

    public async Task InjectAsync(string wimMountPath, string regFilePath, CancellationToken ct)
    {
        _log.Log(LogLevel.Info, $"Injecting registry patch: {regFilePath}");
        // Use `reg load HKLM\Offline C:\mount\Windows\System32\config\SOFTWARE` + `reg import`
        // ...
    }
}
```

### Adding a New App to Catalog

1. Edit `Assets/app-catalog.json`:
   ```json
   {
     "id": "myapp",
     "name": "My App",
     "url": "https://example.com/myapp-latest.exe",
     "silentFlags": "/S /D=C:\\Program Files\\MyApp",
     "sha256": "abc123..."  // optional
   }
   ```
2. AppCatalogService loads JSON; no code change needed.

### CLI Flags Extension

1. Edit `HeadlessBuildRunner.Parse(args)` to recognize new flags.
2. Map to `BuildConfig` or new POCO.
3. Update usage string.

---

## Testing Strategy

### Unit Tests (35 tests, all passing)

- **Builders:** XML generation without I/O
- **Parsers:** DISM text parsing with fixtures
- **Services:** Mocked dependencies (no real subprocess/file access)
- **Test doubles:** MockProcessRunner, MockLogSink

### Acceptance Tests (Manual Playbook)

- AC-1..AC-6 validated via [`vm-smoke-test-playbook.md`](vm-smoke-test-playbook.md)
- Boot built ISO in Hyper-V/VirtualBox
- Verify unattended install, local account, serial→name, offline apps, Office sign-in

---

## Summary

The Windows 11 Custom ISO Builder achieves a complex transformation with:

✅ **Layered separation of concerns** (UI → ViewModel → Services → Tools)  
✅ **Single source of truth** (`BuildConfig` flows through all services)  
✅ **Safe resource cleanup** (WIM mount in try/finally; orphaned-mount cleanup at startup)  
✅ **Dual execution modes** (GUI + CLI from same codebase)  
✅ **Real-time progress feedback** (LogService events, IProgress binding)  
✅ **Cancellable long-running operations** (CancellationToken throughout)  
✅ **Modular design** (each service <200 LOC; reusable)  
✅ **Well-tested** (35 unit tests + manual acceptance playbook)  

The 10-step pipeline is deterministic, cancellable, and fault-tolerant, leaving no stuck mounts or temp files behind on error.

---

**Last Updated:** 2026-05-31 | **Status:** Production | **Architecture Version:** 1.0
