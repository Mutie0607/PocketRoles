@echo off
rem Aegis Anti-Cheat for PocketRoles hosts (separate app from the mod). Normally started by PocketRoles Launcher.
start "" /min powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0Aegis.ps1"
