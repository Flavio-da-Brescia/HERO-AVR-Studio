param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'distribution'),[switch]$IncludeDevelopmentSdk)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$release=Get-Content -LiteralPath "$root\release.json" -Raw|ConvertFrom-Json
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $out|Out-Null
$name='HERO_AVR_Studio_'+$release.version+'_win-x64'+$(if($IncludeDevelopmentSdk){'_SDK'}else{''})
$zip=Join-Path $out ($name+'.zip')
if(Test-Path -LiteralPath $zip){throw "Package already exists: $zip"}
$stage=Join-Path ([IO.Path]::GetTempPath()) ('hero-package-'+[guid]::NewGuid().ToString('N'))
$tmp=$zip+'.partial'
try{
 # Installer stages a curated payload, including full manifest, but no user projects/logs.
 & "$root\Install.ps1" -Destination $stage -NoShortcut -IncludeDevelopmentSdk:$IncludeDevelopmentSdk
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::CreateFromDirectory($stage,$tmp,[IO.Compression.CompressionLevel]::Optimal,$false)
 [IO.File]::Move($tmp,$zip)
 $h=(Get-FileHash -LiteralPath $zip).Hash
 [IO.File]::WriteAllText($zip+'.sha256',($h+'  '+[IO.Path]::GetFileName($zip)+[Environment]::NewLine),(New-Object Text.UTF8Encoding($false)))
 Write-Output "PACKAGE: $zip"
 Write-Output "SHA256: $h"
 Write-Output "BYTES: $((Get-Item -LiteralPath $zip).Length)"
}finally{
 if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}
 if(Test-Path -LiteralPath $tmp){Remove-Item -LiteralPath $tmp -Force}
}
