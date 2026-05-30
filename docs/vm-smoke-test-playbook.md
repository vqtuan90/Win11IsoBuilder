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
   set Computer name = **Serial**.
4. Step 3: enable Office (or disable to speed up the first pass).
5. Step 4: tick 2–3 apps with direct URLs (e.g. 7-Zip, VLC, Notepad++).
6. Step 5: name the output, click **Build ISO**. Watch progress + log.
   - **Expected:** oscdimg exits 0; a `.iso` appears in the output folder. ✅ AC-1

## Boot the VM (AC-2, AC-3, AC-4)

1. Hyper-V: create a **Gen 2** VM, **Secure Boot OFF** (to exercise the bypass), 4 GB RAM, 64 GB disk.
2. Attach the built ISO as DVD; boot.
3. **Expected:**
   - Setup does **not** block on TPM / Secure Boot / RAM. ✅ AC-3 (partial)
   - Setup shows the **drive picker** (disk not auto-wiped); pick the VM disk, continue.
   - OOBE does **not** force a Microsoft account; the **local account** is created. ✅ AC-3
   - Install completes **unattended** past OOBE. ✅ AC-2

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
