# Windows 11 Custom ISO Builder — Project Overview & Product Development Requirements

## Project Overview

**Windows 11 Custom ISO Builder** is a Windows desktop application (.NET 8 + WPF, MVVM, admin-elevated, x64) that transforms a user-supplied Windows 11 ISO into a customized, unattended-install ISO. The output ISO installs Microsoft 365 Apps + chosen third-party applications **offline** at first boot, bypasses common hardware checks (TPM, Secure Boot, RAM), creates a local administrator account, removes bloatware, and sets the computer name from the machine's BIOS serial number.

**Key Value:** Enable IT technicians to create fully-automated, customized Windows 11 installations without internet access on target machines, reducing labor and ensuring consistent deployments.

---

## Scope

### In-Scope

- **Desktop GUI** (.NET 8 + WPF, MVVM pattern, CommunityToolkit.Mvvm)
- **User-supplied ISO** — no automatic download; user provides a valid Windows 11 ISO
- **Offline app installation** — embed all app installers into the ISO (Payload/ folder), install during OOBE/first-boot via SetupComplete.cmd
- **Microsoft 365 Apps** — legal Office Deployment Tool (ODT) flow only; no KMS/MAK/crack; user activates by signing in
- **Curated app catalog** — preset selections (Chrome, Firefox, 7-Zip, VLC, Notepad++, Zalo, Unikey) with pinned versions and optional SHA-256 verification
- **User-supplied installers** — allow adding custom .exe / .msi with silent-install flags
- **Windows customization** via autounattend.xml:
  - Bypass TPM 2.0 / Secure Boot / RAM / Storage / CPU checks (LabConfig registry keys)
  - Skip Microsoft account → local admin account creation
  - Region, timezone, keyboard layout, computer name
  - Computer name = BIOS serial (sanitized, fallback: WIN-xxxxxx)
- **Bloatware removal** — provisioned-Appx removal via DISM on the mounted image
- **Dual-boot ISO** — UEFI + legacy BIOS, repacked with `oscdimg`
- **GUI wizard** — 5-step linear flow with live build log and cancellation
- **Headless CLI** — `--build` mode for automation and CI/CD

### Out-of-Scope (v1)

- Direct USB write; user burns ISO separately (Rufus, dd)
- Automatic Windows 11 ISO download from Microsoft
- License activation bypass (KMS, MAK, crack tools)
- Online winget integration (offline-only approach)
- ISO >4 GB split across multiple USB partitions
- Hybrid online/offline app installation
- GUI preset save/load

---

## Functional Requirements (FR)

| ID | Requirement | Implementation Notes |
|----|--------------|----|
| FR-1 | Select source Windows 11 ISO; validate media (must contain `sources/install.wim` or `sources/install.esd`). | IsoService validates directory structure; robocopy extract with progress. |
| FR-2 | Mount/extract ISO to working directory; auto-cleanup on completion or cancellation. | IsoService.ExtractIsoAsync; BuildOrchestrator ensures cleanup in finally. |
| FR-3 | Support both `install.wim` and `install.esd` (export ESD → WIM when editing required). | WimService.EnsureEditableWimAsync returns effective index post-export. |
| FR-4 | Select Windows edition (Home/Pro/Enterprise) from WIM image info. | WimService.GetImageInfoAsync lists editions; user picks one per UI dropdown. |
| FR-5 | Remove bloatware: list provisioned appx, allow user tick selections, apply via DISM. | WimService.GetProvisionedAppxAsync/RemoveProvisionedAppxAsync; generates removal list from config. |
| FR-6 | Generate `autounattend.xml`: LabConfig bypass keys, local admin user/pass, locale, timezone, keyboard, ComputerName="*". | UnattendBuilder via XDocument; disk partitioning skipped (Setup shows picker). |
| FR-7 | Generate first-boot script that sets computer name = BIOS serial (sanitize ≤15 NetBIOS chars, fallback WIN-xxxxxx if blank/"To be filled by O.E.M."). | FirstBootScriptBuilder via set-computername.ps1 template. |
| FR-8 | Office: ODT configuration.xml (locale, 32/64-bit, app modules), offline `/download`, cache, stage to Payload/Office. | OfficeOdtService.DownloadOfflineAsync via XDocument. |
| FR-9 | App catalog JSON (7 apps: Chrome/Firefox/7-Zip/VLC/Notepad++/Zalo/Unikey) with URLs + silent flags. | Assets/app-catalog.json; AppCatalogService loads and emits install commands. |
| FR-10 | User add custom .exe/.msi installers + custom silent flags. | OfficeViewModel + AppsViewModel UI controls; AppEntry POCO. |
| FR-11 | Generate SetupComplete.cmd: runs installers offline from Payload/ in sequence; logs output. | FirstBootScriptBuilder templates; SYSTEM context, not AutoLogon. |
| FR-12 | Repack ISO bootable (UEFI+BIOS) via `oscdimg` with `-bootdata:2#p0,e,b<etfsboot>#pEF,e,b<efisys>`. | IsoService.RepackIsoAsync; dual-boot support validated by test playbook. |
| FR-13 | GUI wizard: 5 steps (Select ISO → Windows Customize → Office → Apps → Review), step validation gates Next, live build log, cancellation. | MainViewModel + 5 step VMs; WizardStepViewModel base; async Build with CancellationToken. |
| FR-14 | Detect Windows ADK / DISM / oscdimg; if missing, raise error with setup instructions (or use bundled oscdimg). | ToolDetectionService.Detect(); IsAdkMissing flag; bundled fallback to tools/oscdimg/. |

---

## Non-Functional Requirements (NFR)

| Attribute | Requirement |
|-----------|-----------|
| **Permissions** | Application requires Administrator privileges (DISM mount/unmount elevation). |
| **Performance** | First build with Office slow (~30–40 min, including ~3.5 GB ODT download). Subsequent builds fast (ODT cached). |
| **Scalability** | Single-user desktop app; no server/distributed features. |
| **ISO Size** | Accept 6–10 GB output (offline content). Warn user; >4 GB cannot fit FAT32 UEFI USB (use Rufus NTFS or split-WIM later). |
| **Reliability** | All steps logged; WIM mount always unmounted in finally block. No orphaned mounts left on error. |
| **Maintainability** | Modular services <200 LOC each; MVVM separation; UI-agnostic business logic; ILogSink logging. |
| **Security** | No activation bypass, no KMS/MAK/crack. Legal ODT only. LabConfig keys are standard community practice for testing/legacy hardware. |
| **Compatibility** | Windows 10/11, x64; .NET 8 Desktop Runtime. WPF XAML + MVVM via CommunityToolkit.Mvvm. |

---

## Acceptance Criteria (AC)

| ID | Criterion | Validation Method |
|----|-----------|-----------|
| AC-1 | From a real Windows 11 ISO + 2–3 app selections → produces valid `.iso` without errors. | End-to-end build on real 25H2 ISO; oscdimg exit code 0; ISO exists. |
| AC-2 | ISO boots UEFI on VM (Hyper-V/VirtualBox) and installs **without manual intervention** (unattended). | Boot VM, Setup proceeds past OOBE, reaches desktop. |
| AC-3 | Post-install: **local account** (not Microsoft account); **not blocked** by TPM/Secure Boot checks. | Check local user exists; no Setup errors related to HW checks. |
| AC-4 | Computer name = **BIOS serial** (sanitized); **bloatware** (Candy Crush, Xbox, etc.) **removed**. | `hostname` equals VM serial; appx absent from Start menu. |
| AC-5 | After first boot: **Office + selected apps installed offline** (no internet used on target). | Apps present in Programs; Office installed; no online fallback triggered. |
| AC-6 | **Office opens, prompts to sign in** (no embedded license key). Activation is user's responsibility. | Launch Word; sign-in dialog appears; no KMS/crack present. |

All AC validated via manual playbook: [`docs/vm-smoke-test-playbook.md`](vm-smoke-test-playbook.md).

---

## Key Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Windows ADK / oscdimg unavailable | Cannot repack ISO | Bundle oscdimg.exe in tools/; fallback to ADK registry detection; clear setup instructions. |
| install.esd not directly editable | Cannot apply DISM debloat/appx removal | WimService exports ESD → WIM before mount/edit; transparent to caller. |
| BIOS serial invalid (blank, too long, non-alphanumeric) | Computer name fails or invalid | Sanitize: allow alphanumeric + hyphen, limit ≤15 chars, fallback WIN-<random>. Tested in playbook. |
| WIM mount stuck on error (no cleanup) | Resource leak, future mounts fail | try/finally unmount /Discard; startup CleanupOrphanMountsAsync; monitored in playbook. |
| ISO >4 GB exceeds FAT32 UEFI USB | Media unbootable on USB | Out of scope v1; warn user; doc notes Rufus NTFS + split-WIM future. |
| LabConfig keys change per Win11 build | Bypass ineffective on newer Windows | Use standard community keys + allow updates; user-supplied registry modifications possible. |

---

## Resolved Decisions (Validation Session, 2026-05-30)

1. ✅ **oscdimg bundling** — `oscdimg.exe` bundled in `tools/oscdimg/` (redistributed per ADK license terms). User saves step.
2. ✅ **App catalog** — 7 preset apps (Chrome, Firefox, 7-Zip, VLC, Notepad++ with SHA-256 pinning; Zalo, Unikey user-supplied).
3. ✅ **Office** — M365 Apps for Business (`O365BusinessRetail`), English (en-US), 64-bit. Legal ODT only.
4. ✅ **Preset save/load** — YAGNI; deferred. BuildConfig serializable if needed later.
5. ✅ **Disk partitioning** — Setup shows user the drive picker; no auto-wipe (safety).
6. ✅ **First-boot** — SetupComplete.cmd (SYSTEM context), not AutoLogon (simplicity + safety).
7. ✅ **CLI headless mode** — `--build --iso <p> --out <dir>` for CI/automation/smoke tests.

---

## Project Status

### Completed Phases (All 8)

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | **Setup & Architecture** — Solution structure, Models/Services/ViewModels/Views, MVVM framework. | ✅ Done |
| 2 | **Tool Detection & Logging** — ToolDetectionService, ProcessRunner, LogService. | ✅ Done |
| 3 | **ISO Extraction & Validation** — IsoService mount/extract/repack, media validation. | ✅ Done |
| 4 | **WIM & DISM Integration** — WimService mount/unmount, ESD export, appx removal, DismOutputParser. | ✅ Done |
| 5 | **Unattended Setup & Computer Name** — UnattendBuilder (autounattend.xml), FirstBootScriptBuilder (serial→name). | ✅ Done |
| 6 | **Office Integration** — OfficeOdtService (configuration.xml, offline /download, payload staging). | ✅ Done |
| 7 | **App Catalog & Installers** — AppCatalogService (JSON catalog + user-supplied installers, SHA-256 verify, staging). | ✅ Done |
| 8 | **Build Orchestrator & UI** — BuildOrchestrator (10-step pipeline, cancellation, cleanup), 5-step wizard, live log, headless CLI. | ✅ Done |

### Validation Status

- **AC-1** (builds without error): **Proven** — end-to-end builds on real Windows 11 25H2 ISO (debloat-only 7.9 GB, full Office+5 apps 11.63 GB).
- **AC-2..AC-6** (runtime): **Manual via playbook** — [`docs/vm-smoke-test-playbook.md`](vm-smoke-test-playbook.md).
- **3 production bugs found & fixed:**
  1. Read-only `install.wim` from ISO copy → DISM mount error 0xc1510111. **Fixed:** robocopy /A-:R + pre-mount read-only clear.
  2. Read-only media blocked cleanup (access denied). **Fixed:** ClearReadOnly before cleanup.
  3. Transient boot-file lock during cleanup. **Fixed:** Retry with exponential backoff.
- **Unit tests:** 35/35 passing (builders, parsers, services).
- **CI:** GitHub Actions (`ci.yml`) passes (build + test on windows-latest).

---

## Forward Roadmap (Post-v1)

| Item | Priority | Effort | Rationale |
|------|----------|--------|-----------|
| **VM Smoke-Test Automation** | High | Medium | Eliminate manual playbook; auto-spin Hyper-V VM, build, verify AC-1..AC-6. |
| **>4 GB Split-WIM / USB Support** | High | Medium | Split install.wim + append to USB in segments; FAT32-compatible. |
| **Preset Save/Load** | Medium | Low | Serialize BuildConfig; allow "Save as template"; quick redeploy. |
| **App Dependency Handling** | Medium | High | Auto-stage VC++ runtimes, .NET Framework; detect + warn about missing deps. |
| **Signed Binaries** | Medium | Low | Codesign the .exe; improve user trust. |
| **Localization (i18n)** | Low | High | Translate UI + autounattend to de/ja/zh; match Office locale. |
| **Online Winget Option** | Low | High | Hybrid mode: offline core + winget fallback if internet available post-install. |
| **Advanced Registry Customization** | Low | Medium | User-supplied .reg files; inject into WIM at build time. |

---

## Architecture Snapshot

```
┌─────────────────────────────────────────┐
│  WPF Desktop (MVVM)                     │
│  ┌─ MainWindow                          │
│  ├─ MainViewModel (wizard shell)         │
│  └─ 5 Step UserControls                 │
└────────────┬────────────────────────────┘
             │ ICommand, navigation
        ┌────▼────────────────────────────┐
        │  ViewModels (5 steps + shell)   │
        │  WizardStepViewModel base       │
        │  SelectIsoVM, WindowsCustomizeVM│
        │  OfficeVM, AppsVM, ReviewBuildVM│
        └────┬─────────────────────────────┘
             │ ICommand (Build)
        ┌────▼─────────────────────────────┐
        │  Services (UI-agnostic)         │
        │  ┌─ ToolDetectionService        │
        │  ├─ ProcessRunner               │
        │  ├─ LogService                  │
        │  ├─ IsoService                  │
        │  ├─ WimService                  │
        │  ├─ UnattendBuilder             │
        │  ├─ FirstBootScriptBuilder      │
        │  ├─ OfficeOdtService           │
        │  ├─ AppCatalogService          │
        │  └─ BuildOrchestrator          │
        └────┬──────────────────────────────┘
             │ ProcessRunner, file ops
        ┌────▼───────────────────────────┐
        │  External Tools & Files        │
        │  DISM (System32)               │
        │  oscdimg (bundled/ADK)         │
        │  ODT setup.exe (bundled)       │
        └────────────────────────────────┘
```

**Pipeline (10 steps in BuildOrchestrator):**
1. Detect tools (DISM, oscdimg)
2. Cleanup orphaned mounts
3. Validate source ISO
4. Prepare workspace
5. Extract ISO → media
6. Ensure editable WIM (export ESD if needed)
7. Mount WIM; remove appx; commit
8. Build autounattend.xml + stage Office payload
9. Stage app payload + first-boot scripts
10. Repack ISO (oscdimg dual-boot) + cleanup

---

## Technical Highlights

- **Single source of truth:** `BuildConfig` flows through all services; derived paths computed from it.
- **Safe resource cleanup:** WIM mount always unmounted in finally block; orphaned-mount cleanup at startup.
- **Progressive feedback:** Live process-runner callbacks enable real-time log in GUI.
- **Dual-mode execution:** GUI (interactive) and CLI (headless) from same codebase.
- **Modular design:** Each service <200 LOC; no UI dependencies; logged via ILogSink interface.
- **Well-tested:** 35 unit tests (parsers, builders, services); AC acceptance via manual playbook.

---

## How to Get Started

1. **Build:** `dotnet build Win11IsoBuilder.sln -c Release`
2. **Test:** `dotnet test Win11IsoBuilder.Tests/Win11IsoBuilder.Tests.csproj -c Release`
3. **Run (GUI):** `Win11IsoBuilder.exe` (accept UAC); follow 5-step wizard
4. **Run (CLI):** `Win11IsoBuilder.exe --build --iso C:\path\to\Win11.iso --out C:\out [--office] [--apps "chrome,firefox"]`
5. **Validate:** See [`docs/vm-smoke-test-playbook.md`](vm-smoke-test-playbook.md) for manual acceptance testing

Before a real build, ensure bundled tools are present: `Win11IsoBuilder/tools/oscdimg/oscdimg.exe` and `Win11IsoBuilder/tools/odt/setup.exe`.

---

## Key Documents

- [`prd.md`](../prd.md) — Vietnamese original requirements (full detail)
- [`docs/codebase-summary.md`](codebase-summary.md) — Code architecture + file layout
- [`docs/code-standards.md`](code-standards.md) — Naming, file size, conventions
- [`docs/system-architecture.md`](system-architecture.md) — Detailed architecture + Mermaid diagrams
- [`docs/deployment-guide.md`](deployment-guide.md) — Build, run, deploy the output ISO
- [`docs/project-roadmap.md`](project-roadmap.md) — Milestone tracking + forward items
- [`docs/vm-smoke-test-playbook.md`](vm-smoke-test-playbook.md) — Manual acceptance testing (AC-1..AC-6)

---

## Contact & Support

- **Repository:** [github.com/vqtuan90/Win11IsoBuilder](https://github.com/vqtuan90/Win11IsoBuilder) (public)
- **Issues:** Use GitHub Issues for bugs, feature requests, doc clarifications
- **License:** See repository LICENSE file (if applicable)

---

**Last Updated:** 2026-05-31 | **Author:** Documentation Team | **Status:** In Production
