# VM Smoke-Test Playbook — Win11 Custom ISO Builder

Reproducible manual test of a built `.iso` against acceptance criteria AC-1..AC-6 (`prd.md` §7).
Unit tests cover the builders/parsers; this validates a real install end-to-end.

## Prerequisites

- Host: Windows 10/11, Hyper-V enabled (or VirtualBox), ≥ 8 GB free RAM, ≥ 60 GB free disk.
- A source Windows 11 ISO.
- The app run **as Administrator** with `oscdimg` available (bundled `tools\oscdimg\oscdimg.exe` or ADK).
- For an Office build: internet for the one-time ~3.5 GB ODT download (then cached).

## Build the ISO (AC-1)

1. Launch `Win11IsoBuilder` (accept the UAC prompt).
2. Step 1: pick the source ISO, choose an edition (e.g. Windows 11 Pro).
3. Step 2: keep bypass toggles on; set a local admin user/pass; pick a few bloatware items;
   set Computer name = **Serial**. Keep **Intel VMD driver** and **Fully automated install** ticked
   (defaults) — the build log must show driver download/cache + Add-Driver into boot.wim and install.wim.
   On a 24H2/25H2 source ISO the log must also show "ConX Setup media detected — forcing legacy Setup"
   and "Wrote winpeshl.ini (legacy Setup)" — without this, OOBE asks region/keyboard and apps never install.
4. Step 3: enable Office (or disable to speed up the first pass).
5. Step 4: tick 2–3 apps with direct URLs (e.g. 7-Zip, VLC, Notepad++).
6. Step 5: name the output, click **Build ISO**. Watch progress + log.
   - **Expected:** oscdimg exits 0; a `.iso` appears in the output folder. ✅ AC-1

## Boot the VM (AC-2, AC-3, AC-4)

1. Hyper-V: create a **Gen 2** VM, **Secure Boot OFF** (to exercise the bypass), 4 GB RAM, 64 GB disk.
2. Attach the built ISO as DVD; boot.
3. **Expected (zero-touch default):**
   - Setup does **not** block on TPM / Secure Boot / RAM. ✅ AC-3 (partial)
   - **No drive picker, no EULA, no edition prompt** — Disk 0 is wiped + partitioned automatically
     (GPT on Gen 2/UEFI) and install proceeds with **zero clicks**. ✅ AC-Z1
   - OOBE does **not** force a Microsoft account; the **local account** is created. ✅ AC-3
   - Install completes **unattended** past OOBE. ✅ AC-2
4. Repeat on a **Gen 1** VM (legacy BIOS): same zero-touch behavior with an MBR layout. ✅ AC-Z2
5. Rebuild with `--no-auto-partition` (or untick *Fully automated install*): Setup stops at the
   **drive picker** as before. ✅ AC-Z3
   - Optional multi-disk check: add a second virtual disk with a large empty partition and confirm
     where Setup installs — `InstallToAvailablePartition` picks the first qualifying partition on
     ANY disk (documented limitation; README warns to detach extra disks).
6. **Intel VMD driver (AC-D3):** VMs cannot emulate the VMD controller — final verification needs a
   real Intel Core 11th-gen+ machine with VMD enabled in BIOS: boot the ISO and confirm Setup lists
   the NVMe drive and the installed OS boots (no INACCESSIBLE_BOOT_DEVICE). To verify injection
   offline instead: `dism /Mount-Wim` the built ISO's boot.wim and run
   `dism /Image:<mount> /Get-Drivers` → expect `iastorvd.inf`. ✅ AC-D1

## Real hardware (USB) test

1. **Write the ISO to USB with Rufus** (install.wim > 4 GB does not fit FAT32; Rufus handles the
   NTFS + UEFI:NTFS split automatically):
   - Partition scheme **GPT**, target **UEFI (non CSM)** for modern machines.
   - When Rufus shows its **"Windows User Experience"** customization dialog, **UNCHECK every
     option** (TPM bypass, local account, ...) — Rufus would otherwise write its own
     `autounattend.xml` and override the builder's automation.
2. ⚠️ **The machine that boots this USB gets its internal disk ERASED with no prompt.** The boot
   USB is detected (via the disk it runs from) and excluded, so it is never wiped; but unplug
   external/extra disks and double-check nothing valuable is on the machine's internal drive.
3. BIOS: keep **VMD/RST enabled** (that is what the driver injection is for); Secure Boot can stay
   ON (media uses signed Microsoft boot files; the LabConfig keys only bypass the requirement check).
4. Expected: Setup lists the NVMe drive (VMD driver loaded), wipes/partitions the internal disk
   (never the boot USB), installs and reaches the desktop with zero interaction; computer name =
   BIOS serial (sanitized); apps + Office install offline at first boot
   (`%WINDIR%\Setup\Scripts\win11builder-firstboot.log`).
5. **Debug from WinPE:** press **Shift+F10** during Setup → `type X:\auto-partition.log` shows the
   chosen target disk and the excluded boot-USB disk number.

> **Status (2026-07-16):** Real-hardware zero-touch NOT yet fully validated — auto-partition still
> reported wiping an unintended disk on a real machine. Disk-selection under investigation.

## Post-install verification (AC-4, AC-5, AC-6)

After first boot reaches the desktop, check:

1. **Computer name = serial:** open `cmd` → `hostname`. Compare with the VM BIOS serial
   (`wmic bios get serialnumber`). For a blank VM serial, expect `WIN-xxxxxx`. ✅ AC-4
2. **Bloatware removed:** the appx you selected are absent from Start. ✅ AC-4
3. **First-boot log:** open `%WINDIR%\Setup\Scripts\win11builder-firstboot.log`.
   - Confirms payload drive found, computer name set, each installer's exit code.
4. **Apps installed offline:** the selected apps are present (no internet was used). ✅ AC-5
5. **Office:** if enabled, M365 Apps installed; launching Word prompts to **sign in** to
   activate (no key embedded). ✅ AC-5, AC-6

## Cleanup / stability

- Run two or three builds in a row. Confirm no stuck WIM mounts:
  `dism /Get-MountedWimInfo` should list none after each build.
- If a build is cancelled mid-mount, the workspace mount is discarded automatically;
  `dism /Cleanup-Wim` also runs at app start.

## Result log

| AC | Description | Pass/Fail | Notes |
|----|-------------|-----------|-------|
| AC-1 | ISO builds without error | | |
| AC-2 | Boots UEFI, installs unattended | | |
| AC-3 | Local account, no TPM/SecureBoot block | | |
| AC-4 | Computer name = serial, bloatware removed | | |
| AC-5 | Office + apps installed offline | | |
| AC-6 | Office opens, awaits sign-in | | |

## Known limitations

- ISO > 4 GB will not fit a FAT32 UEFI USB (out of scope; use Rufus NTFS or split-WIM later).
- Bundled `oscdimg.exe` / ODT `setup.exe` are **not** committed — add them under `tools\` before a real build.
- App catalog URLs are best-effort; if one 404s, supply a local installer via **Add app**.
