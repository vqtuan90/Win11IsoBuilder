# Deployment Guide — Windows 11 Custom ISO Builder

## Overview

This guide covers:
1. **Prerequisites & environment setup** — getting the bundled tools in place
2. **Building the application** — dotnet build for Release
3. **Running the application** — GUI (interactive) and CLI (headless)
4. **Running tests** — unit tests + CI validation
5. **Deploying the output ISO** — burning to USB and booting on target machines

---

## Prerequisites

### System Requirements

| Requirement | Details |
|-------------|---------|
| **OS** | Windows 10 / Windows 11 (x64) |
| **RAM** | ≥8 GB (for VM testing); ≥16 GB recommended for Office builds |
| **Disk** | ≥100 GB free (extraction + payload staging + ISO output) |
| **Elevation** | Must run Win11IsoBuilder.exe **as Administrator** (DISM needs elevation) |
| **Network** | Internet for first Office/app download; optional for subsequent builds (cached) |

### .NET Runtime

| Context | Required |
|---------|----------|
| **Build (compile)** | .NET 8 SDK (latest stable) |
| **Run (GUI)** | .NET 8 Desktop Runtime |
| **Run (CLI)** | .NET 8 Desktop Runtime |

**Installation:**
- Download from [dot.net](https://dot.net)
- Or: `winget install Microsoft.DotNet.SDK.8` + `winget install Microsoft.DotNet.DesktopRuntime.8`

### Bundled Tools (Critical)

These **must** be placed locally before a real build; they are **not** committed to git (large redistributables).

| Tool | Location | Size | Source |
|------|----------|------|--------|
| `oscdimg.exe` (amd64) | `Win11IsoBuilder/tools/oscdimg/oscdimg.exe` | ~1 MB | Windows ADK → Deployment Tools (`Oscdimg` folder) |
| ODT `setup.exe` | `Win11IsoBuilder/tools/odt/setup.exe` | ~20 MB | [Microsoft Office Deployment Tool](https://www.microsoft.com/en-us/download/details.aspx?id=49117) |

#### Obtaining oscdimg

**Option A: From Windows ADK (Recommended)**

1. Download Windows ADK for Windows 11 from [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/get-started/adk-install).
2. Run the installer; select **Deployment Tools** only (uncheck other components to save space).
3. Install to default location (e.g., `C:\Program Files (x86)\Windows Kits\11\Assessment and Deployment Kit\`).
4. Find `oscdimg.exe` in: `...\Deployment Tools\amd64\Oscdimg\oscdimg.exe`
5. **Copy** to `Win11IsoBuilder\tools\oscdimg\oscdimg.exe`

**Option B: Fallback (Bundled in Repo)**

If ADK is installed locally, `ToolDetectionService` will auto-detect and use it. If neither bundled nor ADK available, the build will fail with a clear error message.

#### Obtaining Office Deployment Tool (ODT)

1. Download from [Microsoft Office Deployment Tool](https://www.microsoft.com/en-us/download/details.aspx?id=49117).
2. Run `officedeploymenttool.exe` (self-extracting).
3. Extract to a temporary folder; you get `setup.exe` + `configuration.xml`.
4. **Copy** `setup.exe` to `Win11IsoBuilder\tools\odt\setup.exe`

**Directory Structure After Setup:**

```
Win11IsoBuilder/
├── tools/
│   ├── oscdimg/
│   │   └── oscdimg.exe           ← copied here
│   └── odt/
│       └── setup.exe             ← copied here
├── Models/
├── Services/
├── ViewModels/
├── Views/
└── Assets/
    ├── app-catalog.json
    ├── SetupComplete.cmd
    └── set-computername.ps1
```

### Source Windows 11 ISO

- Obtain a valid Windows 11 ISO (22H2 or later; 25H2 tested).
- No need to extract; the application mounts it via ISO interface.
- Keep a backup; ISO is not modified (only copied to WorkDir).

---

## Building the Application

### Restore & Build (Release)

```powershell
cd C:\path\to\Win11IsoBuilder

# Restore NuGet packages
dotnet restore Win11IsoBuilder.sln

# Build in Release configuration
dotnet build Win11IsoBuilder.sln -c Release

# Or, build and publish (creates self-contained exe)
dotnet publish Win11IsoBuilder/Win11IsoBuilder.csproj -c Release -o C:\Build\Output
```

### Build Output

The compiled application is located at:
```
Win11IsoBuilder/bin/Release/net8.0-windows/Win11IsoBuilder.exe
```

**Or published (standalone):**
```
C:\Build\Output\Win11IsoBuilder.exe
```

### Verification

After build, ensure bundled tools are included:
```powershell
ls Win11IsoBuilder/bin/Release/net8.0-windows/tools/
# Expected output:
# oscdimg/oscdimg.exe
# odt/setup.exe
```

If tools are missing, the build succeeded but real ISO builds will fail. Copy the tools after building (the `.csproj` should have `<ItemGroup Include="tools/**" />` to auto-copy).

---

## Running the Application

### Mode 1: GUI (Interactive 5-Step Wizard)

```powershell
# From repo root or Build/Output folder
.\Win11IsoBuilder\bin\Release\net8.0-windows\Win11IsoBuilder.exe

# Or from published output
C:\Build\Output\Win11IsoBuilder.exe
```

**Expected behavior:**
1. UAC prompt appears → click **Yes** to allow Administrator elevation.
2. MainWindow opens with wizard shell.
3. **Step 1: Select ISO** — Click "Browse", pick a Windows 11 ISO file.
4. **Step 2: Windows Customize** — Toggle TPM/SecureBoot bypass, set admin user/password, check appx items to remove.
5. **Step 3: Office** — Enable/disable Microsoft 365 Apps.
6. **Step 4: Apps** — Select apps from catalog or add custom installers.
7. **Step 5: Review & Build** — Check summary, click "Build ISO".
8. **Progress window** shows live build log; watch for completion or errors.
9. **Output:** ISO file written to `OutputDirectory` (default: `%USERPROFILE%\Desktop\Win11-Custom.iso`).

### Mode 2: CLI (Headless, Automation-Friendly)

```powershell
Win11IsoBuilder.exe --build --iso "C:\ISO\Win11.iso" --out "C:\Output" `
  [--name "CustomWin11.iso"] `
  [--edition 2] `
  [--debloat "Microsoft.BingNews,Microsoft.BingWeather"] `
  [--office] `
  [--apps "chrome,firefox,7zip,vlc,notepadpp"]
```

#### CLI Arguments Reference

| Argument | Type | Required | Description |
|----------|------|----------|-------------|
| `--build` | flag | ✅ Yes | Enables headless mode (no GUI). |
| `--iso <path>` | string | ✅ Yes | Path to source Windows 11 ISO. |
| `--out <dir>` | string | ✅ Yes | Output directory for the built ISO. |
| `--name <file>` | string | ❌ No | Output filename (default: `Win11-Custom.iso`). |
| `--edition <N>` | int | ❌ No | WIM edition index (default: 1 = Pro). Omit for auto-select. |
| `--debloat <list>` | CSV string | ❌ No | Comma-separated provisioned appx IDs to remove. Example: `"Microsoft.BingNews,Microsoft.BingWeather,Microsoft.GamingApp"`. |
| `--office` | flag | ❌ No | Enable Microsoft 365 Apps offline staging (off by default for speed). |
| `--apps <list>` | CSV string | ❌ No | Comma-separated app IDs from `Assets/app-catalog.json`. Example: `"chrome,firefox,7zip,vlc,notepadpp"`. |

#### Example CLI Commands

**Fast debloat-only build (no Office, no apps):**
```powershell
Win11IsoBuilder.exe --build --iso "C:\ISO\Win11.iso" --out "C:\Output" `
  --debloat "Microsoft.BingNews,Microsoft.BingWeather,Microsoft.GamingApp"
```

**Full build with Office + 5 apps (Pro edition):**
```powershell
Win11IsoBuilder.exe --build --iso "C:\ISO\Win11.iso" --out "C:\Output" `
  --name "Win11-Pro-Office-Apps.iso" `
  --edition 2 `
  --office `
  --apps "chrome,firefox,7zip,vlc,notepadpp"
```

**Custom app + debloat (Home edition):**
```powershell
Win11IsoBuilder.exe --build --iso "C:\ISO\Win11.iso" --out "C:\Output" `
  --edition 1 `
  --debloat "Microsoft.BingNews" `
  --apps "chrome"
```

#### CLI Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success — ISO built without errors. |
| `1` | General error — check console output for details. |
| `2` | Invalid arguments — missing required flag or malformed value. |

#### CLI Output Example

```
Source ISO : C:\ISO\Win11.iso
Output     : C:\Output\Win11-Custom.iso
Edition    : Windows 11 Pro (index 1)
Office     : True | Debloat: 2 | Apps: 5
------------------------------------------------------------
[  2%] Detecting tools: Locating DISM and oscdimg...
[  5%] Validating: Checking source ISO...
[ 10%] Extracting ISO: Copying ISO contents...
[ 30%] Preparing image: Ensuring editable install.wim...
[ 40%] Removing appx: Debloating Windows image...
[ 50%] Building unattend: Creating autounattend.xml...
[ 65%] Office: Staging Microsoft 365 Apps...
[ 75%] Apps: Staging third-party installers...
[ 85%] Scripts: Writing first-boot scripts...
[ 95%] Repacking: oscdimg dual-boot ISO...
[100%] Build complete: C:\Output\Win11-Custom.iso (11.6 GB)
```

---

## Running Tests

### Unit Tests

```powershell
dotnet test Win11IsoBuilder.Tests/Win11IsoBuilder.Tests.csproj -c Release
```

**Expected output:**
```
Test Run Successful.
Total tests: 35
     Passed: 35
     Failed: 0
Test execution time: ~2.5 sec
```

### Specific Test Class

```powershell
dotnet test Win11IsoBuilder.Tests/Win11IsoBuilder.Tests.csproj -c Release -k DismOutputParserTests
```

### Verbose Output

```powershell
dotnet test Win11IsoBuilder.Tests/Win11IsoBuilder.Tests.csproj -c Release --verbosity detailed
```

### Test Coverage (Optional)

If you have coverlet installed:
```powershell
dotnet test Win11IsoBuilder.Tests/Win11IsoBuilder.Tests.csproj -c Release /p:CollectCoverage=true
```

---

## CI/CD Integration (GitHub Actions)

The repository includes a GitHub Actions workflow (`.github/workflows/ci.yml`) that automatically:

1. **On push to main or pull request:**
   - Checks out code
   - Sets up .NET 8
   - Restores dependencies
   - Builds Release configuration
   - Runs all unit tests

2. **Workflow file:** `.github/workflows/ci.yml`
3. **Status:** Public badge in README.md

### Local CI Simulation

To verify your changes will pass CI before pushing:

```powershell
dotnet restore Win11IsoBuilder.sln
dotnet build Win11IsoBuilder.sln -c Release
dotnet test Win11IsoBuilder.Tests/Win11IsoBuilder.Tests.csproj -c Release
```

If all three commands succeed without warnings, your changes should pass CI.

---

## Deploying the Output ISO

Once the ISO is built, users deploy it to target machines.

### Option 1: USB Boot (Most Common)

**Tools needed:** Rufus (or `dd` on Linux host)

**Steps:**

1. **Prepare USB drive:**
   - Insert USB drive (≥8 GB for typical builds; ≥16 GB for Office+apps).
   - Open **Rufus** (free, portable).
   - Select USB device; select the built `.iso` file.
   - **Partition scheme:** GPT (UEFI) or MBR (for legacy BIOS).
   - **File system:** NTFS (for >4 GB ISOs) or FAT32 (if ISO <4 GB).
   - Click **Start** → confirm data wipe → wait for write.

2. **Boot target machine from USB:**
   - Insert USB on target machine.
   - Power on; press F12 (or Del/F2/Esc depending on BIOS).
   - Select USB drive from boot menu.
   - Setup begins automatically (unattended).

3. **First boot (post-install):**
   - SetupComplete.cmd runs automatically.
   - Computer name set from BIOS serial.
   - Office + apps installed offline.
   - User logs in; desktop ready.

### Option 2: Virtual Machine (Testing)

**Prerequisites:** Hyper-V or VirtualBox

**Steps (Hyper-V):**

1. Open Hyper-V Manager.
2. Create new VM:
   - **Name:** "TestWin11"
   - **Generation:** Gen 2 (UEFI)
   - **Memory:** 4 GB
   - **Disk:** 64 GB
3. Add DVD drive; attach the built `.iso`.
4. Power on; boot from DVD.
5. Setup proceeds; monitor via Hyper-V console.
6. After first boot, verify:
   - `hostname` equals BIOS serial.
   - Appx items removed (checked Start menu).
   - Office + apps present.

**Steps (VirtualBox):**

1. Create new VM:
   - OS: Windows 11 (64-bit)
   - RAM: 4 GB
   - Disk: 64 GB (dynamic)
   - **Firmware:** UEFI (if Secure Boot testing needed; otherwise BIOS is fine).
2. Settings → Storage → Attach `.iso` to optical drive.
3. Start VM; boot from optical.
4. Setup proceeds unattended.
5. After first boot, verify same as Hyper-V.

### Option 3: Physical Machine

**Prerequisites:** Administrator access, target hardware

**Steps:**

1. Build ISO using the UI or CLI (as documented above).
2. Burn ISO to USB using Rufus (Option 1).
3. Boot target machine from USB (F12 / Del / Esc at startup).
4. Setup proceeds unattended; no manual intervention needed.
5. First boot: computer name set, Office + apps installed offline.
6. User logs in with pre-configured local admin account.

---

## Troubleshooting

### Build Fails: "oscdimg not found"

**Symptom:** Error message during build: `oscdimg not found. Bundle it under tools\oscdimg or install the Windows ADK.`

**Cause:** `oscdimg.exe` not in `Win11IsoBuilder\tools\oscdimg\` and ADK not installed.

**Fix:**
1. Obtain `oscdimg.exe` from Windows ADK (see "Bundled Tools" section above).
2. Copy to `Win11IsoBuilder\tools\oscdimg\oscdimg.exe`.
3. Retry build.

### Build Fails: "Invalid Windows media"

**Symptom:** Error during extraction: `Invalid Windows media: sources/install.wim or sources/install.esd not found.`

**Cause:** Source ISO is not a valid Windows 11 ISO (possibly corrupted or wrong OS).

**Fix:**
1. Re-download Windows 11 ISO from Microsoft (22H2 or 25H2).
2. Verify SHA-256 hash matches Microsoft's published hash.
3. Retry build with valid ISO.

### Build Fails: "DISM mount error 0xc1510111"

**Symptom:** Error during WIM mount: `DISM mount failed with error 0xc1510111 (read-only media).`

**Cause:** The extracted `install.wim` has read-only attribute; DISM cannot modify it.

**Fix:** This is a known production bug (fixed in v1.0). If you encounter it:
1. Ensure you're running latest version (as of 2026-05-31).
2. If persistent, manually clear read-only:
   ```powershell
   attrib -R C:\path\to\install.wim
   ```
3. Retry build.

### Build Slow: "Office download taking forever"

**Symptom:** Build progress stalls at "Office: Staging Microsoft 365 Apps..." for 20+ minutes.

**Cause:** First Office build downloads ~3.5 GB; network speed dependent.

**Fix:**
1. This is expected for the first build.
2. Subsequent builds use cached Office source (much faster).
3. If network is very slow, consider:
   - Disable `--office` for first few builds (test appx removal, apps first).
   - Pre-download Office offline to `%TEMP%\Win11IsoBuilder\cache\Office` manually.
   - Use wired network (faster than WiFi).

### Build Cleanup Fails: "Access denied deleting temp folder"

**Symptom:** After successful build, error during cleanup: `Access denied: C:\temp\Win11IsoBuilder\work`.

**Cause:** WIM not fully unmounted; file still locked.

**Fix:** This is a known production bug (fixed in v1.0 with retry logic). If you encounter it:
1. Wait 10 seconds; Windows releases lock.
2. Manually delete: `rmdir /s /q "C:\temp\Win11IsoBuilder\work"` (with admin).
3. Or restart application; startup cleanup will handle it.

### VM Install Fails: "Setup blocked by TPM check"

**Symptom:** Setup halts with error about TPM 2.0 requirement.

**Cause:** LabConfig bypass keys not in `autounattend.xml`.

**Fix:**
1. Verify build completed successfully (no errors during "Building unattend..." step).
2. Double-check "Bypass TPM" toggle was **enabled** in Step 2.
3. If toggle was enabled, there's a code bug; report via GitHub Issues with build log.

### VM Install Unattended Fails: "Waits for user input"

**Symptom:** Setup shows dialogs (region, time, privacy) during install.

**Cause:** `autounattend.xml` incomplete or not in media root.

**Fix:**
1. Verify `autounattend.xml` is in ISO root (not in a subfolder).
2. Verify `<ComputerName>*</ComputerName>` is present (placeholder for first-boot).
3. If issue persists, share ISO contents + autounattend.xml with developer team (GitHub Issues).

---

## Performance Tuning

### Reduce Build Time

| Action | Impact | How |
|--------|--------|-----|
| Disable Office | 25–30 min saved | Uncheck Office in Step 3, or omit `--office` in CLI. |
| Reduce appx removal | 2–3 min saved | Uncheck non-critical appx in Step 2. |
| Reduce apps | 5–10 min saved | Select fewer apps or omit `--apps` in CLI. |
| Use NVMe SSD | 10–20% faster | Extract/mount/repack on fast disk; avoid USB/network storage. |
| Upgrade RAM to 16+ GB | Marginal (10%) | Allows Windows to buffer I/O; helps on slower disks. |

### Cache Reuse

After the first build with Office + apps:
- Office offline source cached in `%TEMP%\Win11IsoBuilder\cache\Office\`.
- App installers cached in `%TEMP%\Win11IsoBuilder\cache\Apps\`.
- Subsequent builds reuse cache (saves 20–30 min for Office re-downloads + app re-downloads).

**Note:** Cache survives WorkDir cleanup; manually delete if you want to force re-download:
```powershell
rmdir /s /q "$env:TEMP\Win11IsoBuilder\cache"
```

---

## Compliance & Licensing

### Bundled Tools Licensing

| Tool | License | Terms |
|------|---------|-------|
| **oscdimg.exe** | Microsoft (Windows ADK) | Redistribution allowed; review ADK EULA. |
| **ODT setup.exe** | Microsoft (Office Deployment Tool) | Free for business use; review ODT terms. |
| **Win11IsoBuilder source** | Per repository LICENSE (if any) | Check repo for details. |

### Content in Built ISO

The built ISO contains:
- Unmodified Windows 11 source (user-provided).
- Autounattend.xml (generated by tool; no license).
- Offline Office source (per ODT; user must have license for end-users).
- App installers (licensing per app; e.g., Chrome, Firefox are free; others may require commercial license).

**User Responsibility:** Ensure all software in the ISO complies with your organization's licensing policy.

---

## Next Steps & Support

1. **First build:** Follow the [Getting Started](#running-the-application) section above (GUI or CLI).
2. **Acceptance testing:** Run the manual playbook at [`docs/vm-smoke-test-playbook.md`](vm-smoke-test-playbook.md).
3. **Issues or questions:** File GitHub Issues at [github.com/vqtuan90/Win11IsoBuilder/issues](https://github.com/vqtuan90/Win11IsoBuilder/issues).
4. **Feature requests:** Use GitHub Discussions or Issues with the `enhancement` label.

---

## Quick Reference

### Folder Structure

```
Win11IsoBuilder/
├── bin/Release/net8.0-windows/
│   ├── Win11IsoBuilder.exe         (executable)
│   ├── Win11IsoBuilder.dll         (main assembly)
│   ├── tools/
│   │   ├── oscdimg/oscdimg.exe
│   │   └── odt/setup.exe
│   └── Assets/
│       ├── app-catalog.json
│       ├── SetupComplete.cmd
│       └── set-computername.ps1
└── [source files]
```

### Key Paths (at Runtime)

| Path | Purpose |
|------|---------|
| `%TEMP%\Win11IsoBuilder\work\` | Extraction + mount + repack (cleaned after build) |
| `%TEMP%\Win11IsoBuilder\cache\` | Office + app downloads (cached, survives builds) |
| `%USERPROFILE%\Desktop\` | Default output ISO location |

### Build Commands (Cheat Sheet)

```powershell
# Restore and build Release
dotnet restore
dotnet build -c Release

# Run tests
dotnet test

# Run app (GUI)
.\Win11IsoBuilder\bin\Release\net8.0-windows\Win11IsoBuilder.exe

# Run app (CLI, example)
.\Win11IsoBuilder\bin\Release\net8.0-windows\Win11IsoBuilder.exe `
  --build --iso "C:\ISO\Win11.iso" --out "C:\Out" --office --apps "chrome,firefox"

# Publish (standalone)
dotnet publish Win11IsoBuilder -c Release -o C:\Publish
```

---

**Last Updated:** 2026-05-31 | **Status:** Production | **Version:** 1.0
