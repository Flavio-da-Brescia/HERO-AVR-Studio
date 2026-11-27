$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$version=[xml](Get-Content -LiteralPath "$root\source\HeroAVRStudio.csproj" -Raw)
$files=@(
 Get-ChildItem -LiteralPath $root -File | Where-Object {$_.Extension -in '.ps1','.cmd','.md','.yaml','.ico'}
 Get-ChildItem -LiteralPath "$root\source" -File | Where-Object {$_.Extension -in '.cs','.csproj'}
 Get-ChildItem -LiteralPath "$root\app" -File -Recurse
 Get-ChildItem -LiteralPath "$root\boards" -File
 Get-Item -LiteralPath "$root\examples\ArduinoISP\ArduinoISP.ino"
 Get-Item -LiteralPath "$root\toolchain\avr-gcc\bin\avr-gcc.exe","$root\toolchain\avr-gcc\bin\avr-g++.exe","$root\toolchain\avr-gcc\bin\avr-objcopy.exe","$root\toolchain\avr-gcc\bin\avr-size.exe","$root\toolchain\avrdude\bin\avrdude.exe","$root\toolchain\avrdude\etc\avrdude.conf","$root\assets\hero-logo.png","$root\runtime\dotnet.exe","$root\licenses\THIRD_PARTY.md"
)
$manifest=[ordered]@{version=[string]$version.Project.PropertyGroup.Version;architecture='win-x64';scope='Current app, source, scripts, documentation, branding and selected tool binaries; not a full toolchain or workspace manifest';files=@($files|Sort-Object FullName -Unique|ForEach-Object{[ordered]@{path=$_.FullName.Substring($root.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})}
[IO.File]::WriteAllText("$root\release.json",($manifest|ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
"Manifest updated: $($manifest.version) / $($manifest.files.Count) files"
