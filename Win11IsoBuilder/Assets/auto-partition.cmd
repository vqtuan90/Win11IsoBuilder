@echo off
rem Zero-touch disk preparation, called from autounattend.xml inside WinPE before Setup
rem consumes ImageInstall. SAFETY FIRST: it classifies every disk as USB or internal
rem (diskpart "detail disk" — a USB stick reports Type USB and a "USB Device" model) and
rem only wipes when there is EXACTLY ONE internal, non-USB disk. Any ambiguity (no internal
rem disk, or more than one) aborts WITHOUT touching any disk, so Setup shows its drive
rem picker instead of ever erasing the wrong disk (e.g. the boot USB).
rem Firmware: PEFirmwareType 0x1 = legacy BIOS, 0x2 = UEFI.
setlocal enabledelayedexpansion
set LOG=X:\auto-partition.log
echo [auto-partition] start > %LOG%

rem --- Classify every disk; count internal (non-USB) disks and remember the first ---
set TARGET=
set INTERNAL=0
> X:\ap-list.txt echo list disk
for /f "tokens=2" %%d in ('diskpart /s X:\ap-list.txt ^| findstr /r /c:"Disk [0-9]"') do (
  > X:\ap-detail.txt echo select disk %%d
  >> X:\ap-detail.txt echo detail disk
  set ISUSB=0
  diskpart /s X:\ap-detail.txt | find /i "USB" > nul && set ISUSB=1
  echo Disk %%d USB=!ISUSB! >> %LOG%
  if "!ISUSB!"=="0" (
    set /a INTERNAL+=1
    if not defined TARGET set TARGET=%%d
  )
)
echo Internal disks=!INTERNAL!, target=!TARGET! >> %LOG%

rem --- Only proceed when exactly one internal disk is present ---
if not "!INTERNAL!"=="1" (
  echo Not exactly one internal disk - leaving all disks untouched, Setup will show the picker. >> %LOG%
  copy /y %LOG% "%~dp0auto-partition.log" > nul 2>&1
  exit /b 1
)

set FW=
for /f "tokens=3" %%a in ('reg query HKLM\System\CurrentControlSet\Control /v PEFirmwareType ^| find /i "PEFirmwareType"') do set FW=%%a
echo Firmware=!FW! >> %LOG%

if "!FW!"=="0x2" (
  rem GPT layout for UEFI: EFI 300MB + MSR 128MB + OS partition.
  (
    echo select disk !TARGET!
    echo clean
    echo convert gpt
    echo create partition efi size=300
    echo format quick fs=fat32 label=System
    echo create partition msr size=128
    echo create partition primary
    echo format quick fs=ntfs label=Windows
  ) > X:\ap.txt
) else if "!FW!"=="0x1" (
  rem MBR layout for legacy BIOS: active System Reserved 100MB + OS partition.
  (
    echo select disk !TARGET!
    echo clean
    echo create partition primary size=100
    echo format quick fs=ntfs label=System
    echo active
    echo create partition primary
    echo format quick fs=ntfs label=Windows
  ) > X:\ap.txt
) else (
  echo Unknown firmware type "!FW!" - disk left untouched. >> %LOG%
  copy /y %LOG% "%~dp0auto-partition.log" > nul 2>&1
  exit /b 1
)

diskpart /s X:\ap.txt >> %LOG% 2>&1
echo Partitioned internal disk !TARGET! (firmware !FW!). >> %LOG%
copy /y %LOG% "%~dp0auto-partition.log" > nul 2>&1
