# Windows 11 Custom ISO Builder

[![CI](https://github.com/vqtuan90/Win11IsoBuilder/actions/workflows/ci.yml/badge.svg)](https://github.com/vqtuan90/Win11IsoBuilder/actions/workflows/ci.yml)

A Windows desktop app (.NET 8 + WPF, MVVM, admin-elevated) that turns a **user-supplied Windows 11 ISO** into a customized, **unattended** install ISO — with Microsoft 365 Apps and your chosen apps installed **offline** at first boot, common requirement checks bypassed, a local account, bloatware removed, and the computer name set from the machine serial.

Output: a single bootable `.iso` (UEFI + legacy BIOS).

> Requirements docs: [`prd.md`](prd.md) · architecture: [`docs/codebase-summary.md`](docs/codebase-summary.md)

## Features

- **Bring your own ISO** — mounts + customizes a Win11 ISO you provide (never downloads Windows).
- **Intel VMD/RST storage driver injection** — the pinned Intel RST driver (SHA-256 verified, extracted
  from `SetupRST.exe -extractdrivers`) plus any custom driver folders are injected into `boot.wim`
  and `install.wim`, so Setup sees NVMe drives on Intel Core 11th-gen+ machines with VMD enabled.
- **Win11 24H2/25H2 fix (automatic)** — the new "ConX" Setup on 24H2+ media silently ignores the
  `specialize`/`oobeSystem` unattend passes (interactive OOBE, no first-boot app install). The builder
  detects such media and patches `boot.wim` with a `winpeshl.ini` that launches the legacy
  `setup.exe /legacy`, restoring fully unattended installs.
- **Unattended setup** via generated `autounattend.xml`:
  - Bypass **TPM 2.0 / Secure Boot / RAM / Storage / CPU** checks (LabConfig keys).
  - Skip Microsoft account → create a **local administrator**.
  - Region / time zone / keyboard; computer name placeholder.
  - **MDT-style zero-touch by default** — ⚠️ **ERASES DISK 0**: a WinPE script wipes and partitions
    Disk 0 (GPT on UEFI, MBR on legacy BIOS), the edition is applied and the EULA accepted with no
    prompts. Untick *Fully automated install* (or pass `--no-auto-partition`) to get the interactive
    drive picker instead.
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
                    [--drivers "C:\drv1;C:\drv2"] [--no-vmd] [--no-auto-partition]
```

- `--edition` omitted → auto-selects a **Pro** SKU (else index 1).
- `--office` enables Microsoft 365 (off by default).
- `--apps` are ids from `Win11IsoBuilder/Assets/app-catalog.json`.
- `--drivers` adds custom driver folders (semicolon-separated; every `.inf` inside is injected).
- `--no-vmd` skips the pinned Intel VMD driver; `--no-auto-partition` restores the drive picker
  (zero-touch **wipes Disk 0** and is ON by default).
- Exits `0` on success; the finished `.iso` is written to `--out`.

Example (Office + apps):

```powershell
Win11IsoBuilder.exe --build --iso C:\ISO\Win11.iso --out C:\Out `
  --debloat "Microsoft.BingNews,Microsoft.BingWeather" `
  --office --apps "chrome,firefox,7zip,vlc,notepadpp"
```

## How it works (pipeline)

`Detect tools → extract ISO → (ESD→WIM) → resolve drivers → inject drivers into boot.wim → mount install.wim → remove appx + inject drivers → commit → autounattend.xml (+ zero-touch partition script) → stage Office payload → stage app payload → write first-boot scripts → oscdimg repack → cleanup`

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
- ⚠️ **Zero-touch installs erase Disk 0 of whatever machine boots the ISO** — label your media clearly and disable *Fully automated install* when building for mixed hardware.
- ⚠️ **Multi-disk machines:** only Disk 0 is wiped, but Setup installs to the *first available* partition — on a machine with a second disk holding a large empty partition, Windows may land on that disk instead. Disconnect extra disks or use `--no-auto-partition` on such machines.
- The Intel RST catalog driver covers Core 11th–14th gen VMD controllers. For Core Ultra, download `SetupRST.exe` (RST 20.x) from Intel, run `SetupRST.exe -extractdrivers <folder>`, and add that folder under **Storage drivers** (or `--drivers`).
- `oscdimg` / ODT are Microsoft tools — review their licenses before redistributing.

## Status

Implemented end-to-end and validated by real builds on a Windows 11 25H2 ISO (extract → debloat → Office + app staging → dual-boot repack). 35/35 unit tests pass.

## License

[MIT](LICENSE) © 2026 Tuan Vu. Covers this project's source only — not the bundled Microsoft tools (`oscdimg`, ODT) or Windows itself; review their terms separately.
