# Windows 11 Custom ISO Builder

[![CI](https://github.com/vqtuan90/Win11IsoBuilder/actions/workflows/ci.yml/badge.svg)](https://github.com/vqtuan90/Win11IsoBuilder/actions/workflows/ci.yml)

A Windows desktop app (.NET 8 + WPF, MVVM, admin-elevated) that turns a **user-supplied Windows 11 ISO** into a customized, **unattended** install ISO — with Microsoft 365 Apps and your chosen apps installed **offline** at first boot, common requirement checks bypassed, a local account, bloatware removed, and the computer name set from the machine serial.

Output: a single bootable `.iso` (UEFI + legacy BIOS).

> Requirements docs: [`prd.md`](prd.md) · architecture: [`docs/codebase-summary.md`](docs/codebase-summary.md)

## Features

- **Bring your own ISO** — mounts + customizes a Win11 ISO you provide (never downloads Windows).
- **Unattended setup** via generated `autounattend.xml`:
  - Bypass **TPM 2.0 / Secure Boot / RAM / Storage / CPU** checks (LabConfig keys).
  - Skip Microsoft account → create a **local administrator**.
  - Region / time zone / keyboard; computer name placeholder.
  - **Disk is not auto-partitioned** — Setup shows the drive picker (avoids wiping the wrong disk).
- **Bloatware removal** — curated provisioned-Appx checklist, applied via DISM on the mounted image.
- **Microsoft 365 Apps (offline)** via the Office Deployment Tool — downloads the source once (cached) and stages it onto the media. Legal ODT only; **no KMS/crack** — you sign in to activate.
- **App catalog** — Chrome, Firefox, 7-Zip, VLC, Notepad++ (pinned versions + **SHA-256 verified**), plus add-your-own `.exe`/`.msi` with silent flags. All staged for **offline** install.
- **First boot** — `SetupComplete.cmd` (SYSTEM context) sets the computer name from the BIOS serial (sanitized to NetBIOS, with `WIN-xxxxxx` fallback) and runs every installer offline.
- **Dual-boot repack** with `oscdimg` (UEFI + BIOS).
- **GUI wizard** with live build log + cancellation, **and** a headless `--build` CLI for automation.

## Requirements

- Windows 10/11, **x64**, run **as Administrator** (DISM mount/unmount needs elevation).
- **.NET 8 SDK** (build) / .NET 8 Desktop Runtime (run).
- Bundled native tools (not committed — see below): `oscdimg.exe` and the Office Deployment Tool `setup.exe`.

### Bundled tools (`tools/`, git-ignored)

These are large Microsoft redistributables kept out of source control. Place them before a real build:

| Tool | Path | Source |
|------|------|--------|
| `oscdimg.exe` | `Win11IsoBuilder/tools/oscdimg/oscdimg.exe` | Windows ADK → *Deployment Tools* (`amd64\Oscdimg`) |
| ODT `setup.exe` | `Win11IsoBuilder/tools/odt/setup.exe` | [Office Deployment Tool](https://www.microsoft.com/en-us/download/details.aspx?id=49117), run `officedeploymenttool.exe /extract:.` |

If `oscdimg` is absent the app also auto-detects an installed Windows ADK as a fallback.

## Build

```powershell
dotnet build Win11IsoBuilder/Win11IsoBuilder.csproj -c Release
```

## Usage

### GUI

Run `Win11IsoBuilder.exe` (accept the UAC prompt) and follow the 5-step wizard:
**Select ISO → Windows tweaks → Office → Apps → Review & Build**.

### Headless CLI

```
Win11IsoBuilder.exe --build --iso <source.iso> --out <folder>
                    [--name out.iso] [--edition N]
                    [--debloat "Microsoft.BingNews,Microsoft.BingWeather"]
                    [--office] [--apps "chrome,firefox,7zip,vlc,notepadpp"]
```

- `--edition` omitted → auto-selects a **Pro** SKU (else index 1).
- `--office` enables Microsoft 365 (off by default).
- `--apps` are ids from `Win11IsoBuilder/Assets/app-catalog.json`.
- Exits `0` on success; the finished `.iso` is written to `--out`.

Example (Office + apps):

```powershell
Win11IsoBuilder.exe --build --iso C:\ISO\Win11.iso --out C:\Out `
  --debloat "Microsoft.BingNews,Microsoft.BingWeather" `
  --office --apps "chrome,firefox,7zip,vlc,notepadpp"
```

## How it works (pipeline)

`Detect tools → extract ISO → (ESD→WIM) → mount → remove appx → commit → autounattend.xml → stage Office payload → stage app payload → write first-boot scripts → oscdimg repack → cleanup`

A single `BuildConfig` flows through every service; the WIM mount is always unmounted in a `finally` so a failure never leaves a stuck mount.

## Project structure

```
Win11IsoBuilder/
├── Models/        BuildConfig (single source of truth) + option/result POCOs
├── Services/      tooling, ISO/WIM (DISM+oscdimg), unattend, first-boot, Office ODT, app catalog, orchestrator
├── ViewModels/    MVVM wizard (CommunityToolkit.Mvvm)
├── Views/         XAML step pages
└── Assets/        autounattend templates, first-boot .cmd/.ps1, app-catalog.json
Win11IsoBuilder.Tests/   xUnit (builders, parsers, services)
```

## Testing

```powershell
dotnet test Win11IsoBuilder.Tests/Win11IsoBuilder.Tests.csproj -c Release
```

Unit tests cover the generators/parsers (XML, scripts, catalog, DISM output). Full installation acceptance (AC‑2…AC‑6 — booting the ISO in a VM) is manual: see [`docs/vm-smoke-test-playbook.md`](docs/vm-smoke-test-playbook.md).

## Legal / safety

- Only the **legal** ODT flow is used for Office; **no activation bypass** (no KMS/MAK/crack). Licensing is the user's responsibility — sign in to activate.
- TPM/Secure Boot bypass uses the standard community LabConfig registry keys and only affects setup of the ISO you build.
- `oscdimg` / ODT are Microsoft tools — review their licenses before redistributing.

## Status

Implemented end-to-end and validated by real builds on a Windows 11 25H2 ISO (extract → debloat → Office + app staging → dual-boot repack). 35/35 unit tests pass.

## License

[MIT](LICENSE) © 2026 Tuan Vu. Covers this project's source only — not the bundled Microsoft tools (`oscdimg`, ODT) or Windows itself; review their terms separately.
