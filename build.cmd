@echo off

powershell -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
if errorlevel 1 exit /b %errorlevel%

powershell -ExecutionPolicy Bypass -File "%~dp0build.ps1" -installer %*
if errorlevel 1 exit /b %errorlevel%