param(
 [string]$Destination=(Join-Path $env:LOCALAPPDATA 'HERO_AVR_Studio'),
 [switch]$IncludeDevelopmentSdk,
 [switch]$NoShortcut
)
$ErrorActionPreference='Stop'
$src=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$dest=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
if(![Environment]::Is64BitOperatingSystem){throw 'HERO AVR Studio requires 64-bit Windows'}
if(Test-Path -LiteralPath $dest){throw "Destination already exists: $dest. Choose a new folder; no overwrite."}
if($dest.StartsWith($src+'\',[StringComparison]::OrdinalIgnoreCase) -or $src.StartsWith($dest+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Choose a destination separate from the source folder'}
$parent=Split-Path -Parent $dest
if(!$parent){throw 'Choose an ordinary installation folder'}
New-Item -ItemType Directory -Force $parent|Out-Null
$stage=Join-Path $parent ('.hero-install-'+[guid]::NewGuid().ToString('N'))
function HashTree([string]$folder){
 $hasher=[Security.Cryptography.SHA256]::Create()
 try{
  foreach($f in Get-ChildItem -LiteralPath $folder -File -Recurse -Force|Sort-Object FullName){
   if($f.Name -eq 'package-manifest.json' -and $f.DirectoryName -eq $folder){continue}
   if($f.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Unexpected link: $($f.FullName)"}
   $stream=[IO.File]::OpenRead($f.FullName)
   try{$hash=([BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-','')}finally{$stream.Dispose()}
   [ordered]@{path=$f.FullName.Substring($folder.Length+1).Replace('\','/');sha256=$hash;bytes=$f.Length}
  }
 }finally{$hasher.Dispose()}
}
function AssertManifest([string]$folder,$manifest){
 $actual=@(HashTree $folder);$expected=@($manifest.files)
 if($actual.Count -ne $expected.Count){throw "Package file count mismatch: $folder"}
 for($i=0;$i -lt $actual.Count;$i++){
  if($actual[$i].path -cne $expected[$i].path -or $actual[$i].sha256 -ne $expected[$i].sha256 -or $actual[$i].bytes -ne $expected[$i].bytes){throw "Package integrity mismatch: $($actual[$i].path)"}
 }
}
function AssertNoLinks([string]$folder){
 if((Get-Item -LiteralPath $folder).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Unexpected link: $folder"}
 if(Get-ChildItem -LiteralPath $folder -Recurse -Force|Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}|Select-Object -First 1){throw "Links are not supported: $folder"}
}
$packageFile=Join-Path $src 'package-manifest.json'
$packaged=Test-Path -LiteralPath $packageFile
try{
 New-Item -ItemType Directory $stage|Out-Null
 if($packaged){
  AssertNoLinks $src
  $manifest=Get-Content -LiteralPath $packageFile -Raw|ConvertFrom-Json
  if($manifest.schema_version -ne 1 -or $manifest.architecture -ne 'win-x64'){throw 'Invalid package manifest'}
  AssertManifest $src $manifest
  if($IncludeDevelopmentSdk -and !(Test-Path "$src\dev-sdk")){throw 'This package does not contain the development SDK'}
  foreach($item in Get-ChildItem -LiteralPath $src -Force){Copy-Item -LiteralPath $item.FullName -Destination $stage -Recurse -Force}
  AssertManifest $stage $manifest
 }else{
  $dirs=@('app','runtime','toolchain','compatibility','boards','examples','licenses','tests')
  if($IncludeDevelopmentSdk){$dirs+= 'dev-sdk'}
  foreach($n in $dirs){
   $p=Join-Path $src $n;AssertNoLinks $p
   Copy-Item -LiteralPath $p -Destination (Join-Path $stage $n) -Recurse
  }
  New-Item -ItemType Directory -Force "$stage\source","$stage\assets","$stage\workspace","$stage\diagnostics"|Out-Null
  Copy-Item -LiteralPath "$src\assets\hero-logo.png" -Destination "$stage\assets"
  Get-ChildItem -LiteralPath "$src\source" -File|Where-Object {$_.Extension -in '.cs','.csproj'}|Copy-Item -Destination "$stage\source"
  Get-ChildItem -LiteralPath $src -File|Where-Object {$_.Extension -in '.ps1','.cmd','.md','.ico' -or $_.Name -eq 'release.json'}|Copy-Item -Destination $stage
  foreach($n in 'workspace\libraries','diagnostics\profiles'){
   AssertNoLinks (Join-Path $src $n)
   Copy-Item -LiteralPath (Join-Path $src $n) -Destination (Join-Path $stage $n) -Recurse
  }
  $release=Get-Content -LiteralPath "$src\release.json" -Raw|ConvertFrom-Json
  $manifest=[ordered]@{schema_version=1;version=$release.version;architecture='win-x64';development_sdk=[bool]$IncludeDevelopmentSdk;files=@(HashTree $stage)}
  [IO.File]::WriteAllText("$stage\package-manifest.json",($manifest|ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
  AssertManifest $stage $manifest
 }
 foreach($n in 'build','uploads','projects','plots','production','serial-logs','isp-logs','diagnostics\runs','workspace\sketches','workspace\libraries','diagnostics\profiles'){New-Item -ItemType Directory -Force (Join-Path $stage $n)|Out-Null}
 foreach($required in 'app\HeroAVRStudio.exe','runtime\dotnet.exe','toolchain\avr-gcc\bin\avr-gcc.exe','toolchain\avrdude\bin\avrdude.exe','hero.ps1','hero-build.ps1','hero-preprocess.ps1','Launch.cmd'){
  if(!(Test-Path -LiteralPath (Join-Path $stage $required) -PathType Leaf)){throw "Missing component: $required"}
 }
 [IO.Directory]::Move($stage,$dest)
 Write-Output "Installed: $dest"
 Write-Output "Verified package files: $(@($manifest.files).Count)"
 if(!$NoShortcut){
  try{
   $w=New-Object -ComObject WScript.Shell
   $shortcut=Join-Path ([Environment]::GetFolderPath('Desktop')) 'HERO AVR Studio.lnk'
   if(Test-Path -LiteralPath $shortcut){Write-Warning "Existing shortcut preserved: $shortcut"}
   else{
    $link=$w.CreateShortcut($shortcut)
    $link.TargetPath=Join-Path $dest 'Launch.cmd'
    $link.WorkingDirectory=$dest
    $link.IconLocation=(Join-Path $dest 'HERO.ico')+',0'
    $link.Save()
   }
  }catch{Write-Warning "Installed successfully; shortcut not created: $_"}
 }
 Write-Output "Launch using $dest\Launch.cmd. USB serial/ISP drivers are not installed."
}finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
