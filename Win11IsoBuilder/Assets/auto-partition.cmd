@echo off
rem Zero-touch disk preparation, called from autounattend.xml inside WinPE before Setup
rem consumes ImageInstall. It targets the first INTERNAL disk and NEVER the boot USB:
rem this script runs from the USB itself (%~d0), so its physical disk is identified and
rem excluded. WARNING: the chosen internal disk is erased completely.
rem Firmware: PEFirmwareType 0x1 = legacy BIOS, 0x2 = UEFI. Unknown → leave disks alone.

rem --- Identify the physical disk of the boot media (the volume this script runs from) ---
set BOOTLETTER=%~d0
set BOOTLETTER=%BOOTLETTER:~0,1%
> X:\ap-detail.txt echo select volume %BOOTLETTER%
>> X:\ap-detail.txt echo detail volume
set USBDISK=
for /f "tokens=3" %%a in ('diskpart /s X:\ap-detail.txt ^| find "* Disk"') do set USBDISK=%%a

rem --- Choose target = first disk that is NOT the boot USB ---
> X:\ap-list.txt echo list disk
set TARGET=
for /f "tokens=2" %%a in ('diskpart /s X:\ap-list.txt ^| findstr /r /c:"Disk [0-9]"') do (
  if not defined TARGET if not "%%a"=="%USBDISK%" set TARGET=%%a
)

if not defined TARGET (
  echo No internal target disk found ^(boot USB = disk %USBDISK%^) - disks left untouched.> X:\auto-partition.log
  exit /b 1
)

set FW=
for /f "tokens=3" %%a in ('reg query HKLM\System\CurrentControlSet\Control /v PEFirmwareType ^| find /i "PEFirmwareType"') do set FW=%%a

if "%FW%"=="0x2" (
  rem GPT layout for UEFI: EFI 300MB + MSR 128MB + OS partition.
  (
    echo select disk %TARGET%
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
    echo select disk %TARGET%
    echo clean
    echo create partition primary size=100
    echo format quick fs=ntfs label=System
    echo active
    echo create partition primary
    echo format quick fs=ntfs label=Windows
  ) > X:\ap.txt
) else (
  echo Unknown firmware type "%FW%" - disk left untouched.> X:\auto-partition.log
  exit /b 1
)

diskpart /s X:\ap.txt > X:\auto-partition.log 2>&1
echo Target disk %TARGET% (firmware %FW%, boot USB disk %USBDISK%).>> X:\auto-partition.log
