@echo off
setlocal EnableExtensions EnableDelayedExpansion
set "LOG=%WINDIR%\Setup\Scripts\win11builder-firstboot.log"
echo [%DATE% %TIME%] SetupComplete starting >> "%LOG%"

rem --- Computer name (runs regardless of payload presence) ---
echo [%DATE% %TIME%] Setting computer name... >> "%LOG%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0set-computername.ps1" >> "%LOG%" 2>&1

rem --- Locate the offline payload drive by its marker file (drive letter is not fixed) ---
set "PD="
for %%D in (C D E F G H I J K L M N O P Q R S T U V W X Y Z A B) do (
  if exist "%%D:\Payload\manifest.txt" set "PD=%%D:\Payload"
)
if not defined PD (
  echo [%DATE% %TIME%] ERROR: payload not found on any drive - skipping app install >> "%LOG%"
  goto :end
)
echo [%DATE% %TIME%] Payload drive: %PD% >> "%LOG%"

rem --- Installers run at top level (no enclosing parens) so app names/args with ) are safe ---
{{OFFICE_BLOCK}}
{{APPS_BLOCK}}

:end
echo [%DATE% %TIME%] SetupComplete finished >> "%LOG%"
endlocal
exit /b 0
