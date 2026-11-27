@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
if errorlevel 1 (
 echo Installation failed. Existing installations have not been overwritten.
 exit /b 1
)
echo Installation complete. Use the HERO AVR Studio shortcut or Launch.cmd.
