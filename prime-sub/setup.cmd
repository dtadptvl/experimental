@echo off
setlocal EnableExtensions
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
set "RC=%ERRORLEVEL%"
if not defined PRIME_SUB_NO_PAUSE pause
exit /b %RC%
