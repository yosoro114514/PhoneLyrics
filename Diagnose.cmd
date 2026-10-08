@echo off
cd /d "%~dp0"
if not exist "artifacts\diagnostics\PhoneLyrics.exe" (
  echo Build required. See README.md.
  pause
  exit /b 1
)
"artifacts\diagnostics\PhoneLyrics.exe" ams-watch --seconds 20
pause
