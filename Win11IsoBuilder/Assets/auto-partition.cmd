@echo off
rem Zero-touch disk preparation, run by autounattend.xml inside WinPE before Setup
rem consumes ImageInstall. WARNING: this erases Disk 0 completely.
rem PEFirmwareType: 0x1 = legacy BIOS, 0x2 = UEFI. Any other/unknown value → do NOT touch
rem the disk; Setup's ImageInstall then finds no partition and falls back to its UI
rem (WillShowUI=OnError) instead of wiping with the wrong layout.
set FW=
for /f "tokens=3" %%a in ('reg query HKLM\System\CurrentControlSet\Control /v PEFirmwareType ^| find /i "PEFirmwareType"') do set FW=%%a

if "%FW%"=="0x2" (
  rem GPT layout for UEFI: EFI 300MB + MSR 128MB + OS partition.
  (
    echo select disk 0
    echo clean
    echo convert gpt
    echo create partition efi size=300
    echo format quick fs=fat32 label=System
    echo create partition msr size=128
    echo create partition primary
    echo format quick fs=ntfs label=Windows
  ) > X:\ap.txt
) else if "%FW%"=="0x1" (
  rem MBR layout for legacy BIOS: active System Reserved 100MB + OS partition.
  (
    echo select disk 0
    echo clean
    echo create partition primary size=100
    echo format quick fs=ntfs label=System
    echo active
    echo create partition primary
    echo format quick fs=ntfs label=Windows
  ) > X:\ap.txt
) else (
  echo Unknown firmware type "%FW%" - disk left untouched. > X:\auto-partition.log
  exit /b 1
)

diskpart /s X:\ap.txt > X:\auto-partition.log 2>&1
