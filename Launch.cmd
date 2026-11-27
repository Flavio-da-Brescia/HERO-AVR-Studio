@echo off
setlocal
set "DOTNET_ROOT=%~dp0runtime"
set "DOTNET_ROOT_X64=%~dp0runtime"
set "DOTNET_MULTILEVEL_LOOKUP=0"
start "" "%~dp0app\HeroAVRStudio.exe"
