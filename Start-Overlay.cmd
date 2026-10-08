@echo off
cd /d "%~dp0"
if not exist "artifacts\portable\PhoneLyrics.exe" (
  echo Build required. See README.md.
  pause
  exit /b 1
)
start "" "%~dp0artifacts\portable\PhoneLyrics.exe"
