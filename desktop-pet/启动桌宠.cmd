@echo off
rem ---------------------------------------------------------------------------
rem  Whale-girl desktop pet - launcher
rem
rem  Double-click this file. It starts the native Windows build (WhalePet.exe),
rem  which needs no runtime download and does not depend on DeepSeek Harness.
rem
rem  ASCII-ONLY on purpose (no Chinese here, not even in comments):
rem    * cmd.exe reads .cmd files with the OEM code page, so UTF-8 Chinese text
rem      is parsed as garbage commands, and
rem    * the executable name must stay ASCII because csc.exe decodes its /out:
rem      argument with that same OEM code page.
rem
rem  A startup log is always written to %TEMP%\whalepet-startup.log, so a silent
rem  failure (security software terminating the process) still leaves evidence.
rem
rem  NOTE: 360 Total Security is installed on this machine and challenges new
rem  unsigned executables. If the pet does not appear, allow WhalePet.exe in
rem  360 (Trusted Zone / add to whitelist) - the prompt is 360, not Windows.
rem ---------------------------------------------------------------------------

setlocal
set "HERE=%~dp0"
set "PET=%HERE%WhalePet.exe"
set "LOG=%TEMP%\whalepet-startup.log"

if not exist "%PET%" (
  echo [ERROR] Pet executable not found:
  echo         %PET%
  echo.
  echo Build it first:
  echo         powershell -ExecutionPolicy Bypass -File "%HERE%tools\build-desktop-pet.ps1"
  echo.
  pause
  exit /b 1
)

if not exist "%HERE%art\front.png" (
  echo [ERROR] Artwork not found: %HERE%art\front.png
  pause
  exit /b 1
)

if exist "%LOG%" del /q "%LOG%" >nul 2>&1

echo Starting the whale-girl desktop pet...
echo   exe : %PET%
echo   log : %LOG%
echo.
echo If nothing appears, check that log file; if a security dialog asks about
echo WhalePet.exe, allow it once (360 blocks new unsigned programs by default).
echo.

start "" "%PET%" "--log=%LOG%" %*
exit /b 0
