@echo off
rem ---------------------------------------------------------------------------
rem  Diagnostic launcher - shows Electron's own error output.
rem
rem  ASCII-only on purpose (see the launcher's header for why).
rem  Run this when the normal launcher closes without showing the pet: unlike
rem  the launcher it does not detach, so Chromium's crash message stays visible.
rem ---------------------------------------------------------------------------

setlocal
set "HERE=%~dp0"
set "ELECTRON=%HERE%node_modules\electron\dist\electron.exe"

echo.
echo == Electron runtime ==
if exist "%ELECTRON%" (echo   found: %ELECTRON%) else (echo   MISSING: %ELECTRON% & echo   run: pnpm install & pause & exit /b 1)

echo.
echo == Electron version (proves the binary runs) ==
"%ELECTRON%" --no-sandbox --version

echo.
echo == Starting the pet window (console stays open) ==
echo   If nothing appears, the text below or above is the reason.
echo.
"%ELECTRON%" "%HERE%." --no-sandbox --disable-gpu

echo.
echo == The pet process exited. ==
pause
exit /b 0
