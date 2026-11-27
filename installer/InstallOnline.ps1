param(
 [Parameter(Mandatory=$true)][string]$Payload,
 [Parameter(Mandatory=$true)][string]$PayloadSha256,
 [Parameter(Mandatory=$true)][string]$Destination,
 [switch]$NoShortcut,
 [string]$VerifiedCache
)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$dest=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
$parent=Split-Path -Parent $dest
if(!$parent -or (Test-Path -LiteralPath $dest)){throw 'Choose a new installation folder. Existing installations are never overwritten.'}
if([Environment]::OSVersion.Version.Major -lt 10 -or ![Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64'){throw 'Windows 10/11 x64 is required.'}
$tar=Join-Path $env:SystemRoot 'System32\tar.exe'
if(!(Test-Path -LiteralPath $tar)){throw 'Windows tar.exe is required. Update Windows.'}
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$stage=Join-Path $parent ('.hero-install-'+[guid]::NewGuid().ToString('N'))
$temp=Join-Path ([IO.Path]::GetTempPath()) ('hero-deps-'+[guid]::NewGuid().ToString('N'))
$ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function CheckHash($file,$algorithm,$expected){
 if((Get-FileHash -LiteralPath $file -Algorithm $algorithm).Hash -ne $expected){throw "Checksum mismatch: $([IO.Path]::GetFileName($file))"}
}
function ExtractZip($file,$target){
 $z=[IO.Compression.ZipFile]::OpenRead($file)
 try{
  $base=[IO.Path]::GetFullPath($target).TrimEnd('\')+'\'
  foreach($entry in $z.Entries){
   $full=[IO.Path]::GetFullPath((Join-Path $target $entry.FullName))
   if(!$full.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe ZIP path'}
  }
 }finally{$z.Dispose()}
 # Each archive extracts to an empty directory.
 [IO.Compression.ZipFile]::ExtractToDirectory($file,$target)
}
function RejectLinks($folder){
 if(Get-ChildItem -LiteralPath $folder -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}|Select-Object -First 1){throw 'Archive contains an unexpected link'}
}
try{
 if(!(Test-Path -LiteralPath $parent)){New-Item -ItemType Directory -Path $parent -Force|Out-Null}
 New-Item -ItemType Directory -Path $temp,$stage|Out-Null
 Write-Output '[1/5] Verifying and extracting HERO application...'
 CheckHash $Payload SHA256 $PayloadSha256
 ExtractZip $Payload $stage
 RejectLinks $stage
 $lock=Get-Content -LiteralPath "$stage\installer\dependencies.json" -Raw|ConvertFrom-Json
 if($lock.schema_version -ne 1 -or @($lock.packages).Count -ne 5){throw 'Unsupported dependency manifest'}
 Write-Output '[2/5] Downloading and verifying official dependencies...'
 foreach($p in $lock.packages){
  $archive=Join-Path $temp $p.archive
  $cached=if($VerifiedCache){Join-Path $VerifiedCache $p.archive}else{''}
  if($cached -and (Test-Path -LiteralPath $cached)){Write-Output ("  Verified cache: "+$p.name);Copy-Item -LiteralPath $cached -Destination $archive}
  else{
   Write-Output ("  Downloading "+$p.name+" "+$p.version+" ...")
   $client=New-Object Net.WebClient
   try{$client.DownloadFile($p.url,$archive)}finally{$client.Dispose()}
  }
  CheckHash $archive $p.algorithm $p.hash
  Write-Output ("  Checksum OK: "+$p.name)
  $ex=Join-Path $temp ([IO.Path]::GetFileNameWithoutExtension($p.archive)+'-extracted')
  New-Item -ItemType Directory -Force $ex|Out-Null
  if($archive.EndsWith('.zip')){ExtractZip $archive $ex}
  else{
   $entries=@(& $tar -tf $archive)
   if($LASTEXITCODE -ne 0){throw 'Cannot read official tar archive'}
   foreach($name in $entries){
    if($name -match '^[/\\]|^[A-Za-z]:|(^|[/\\])\.\.([/\\]|$)'){throw 'Unsafe tar path'}
   }
   & $tar -xf $archive -C $ex
   if($LASTEXITCODE -ne 0){throw 'Dependency extraction failed'}
  }
  RejectLinks $ex
  $source=$ex
  if(!(Test-Path -LiteralPath (Join-Path $source $p.marker))){
   $candidates=@(Get-ChildItem -LiteralPath $ex -Directory|Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName $p.marker)})
   if($candidates.Count -ne 1){throw "Unexpected archive layout: $($p.name)"}
   $source=$candidates[0].FullName
  }
  $target=Join-Path $stage $p.target
  New-Item -ItemType Directory -Force $target|Out-Null
  Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $target -Recurse -Force
  if(!(Test-Path -LiteralPath (Join-Path $target $p.marker))){throw "Missing extracted component: $($p.name)"}
  Remove-Item -LiteralPath $archive -Force
  Remove-Item -LiteralPath $ex -Recurse -Force
 }
 Write-Output '[3/5] Creating a clean workspace...'
 foreach($n in 'build','uploads','projects','plots','production','serial-logs','isp-logs','diagnostics\runs','workspace\sketches','workspace\libraries','compatibility\extra-libraries'){
  New-Item -ItemType Directory -Force (Join-Path $stage $n)|Out-Null
 }
 $env:DOTNET_ROOT=Join-Path $stage 'runtime';$env:DOTNET_ROOT_X64=$env:DOTNET_ROOT;$env:DOTNET_MULTILEVEL_LOOKUP='0'
 $env:PATH="$env:SystemRoot\System32;$env:SystemRoot"
 $env:HERO_NO_CACHE='1'
 Write-Output '[4/5] Running local compiler and desktop integration tests (no board programming)...'
 $native=& $ps -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$stage\hero.ps1" -Action compile -Sketch "$stage\examples\NativeBlink"|ConvertFrom-Json
 if($LASTEXITCODE -ne 0 -or !$native.success -or $native.sha256 -ne '98ED4092AE719071884B6600F88CC2FE962CD20B70F7A6FC8FBC8D3E77A78364'){throw 'Native AVR compiler self-test failed'}
 Write-Output '  Native C: PASS'
 # Run GUI probes as though launched from a default restrictive user environment.
 $savedPolicy=$env:PSExecutionPolicyPreference
 try{
  $env:PSExecutionPolicyPreference='Restricted'
  $gui=Start-Process "$stage\app\HeroAVRStudio.exe" -ArgumentList '--policy-selftest' -PassThru
 }finally{$env:PSExecutionPolicyPreference=$savedPolicy}
 $null=$gui.Handle
 if(!$gui.WaitForExit(180000)){throw 'Desktop self-test timed out; close the test process before retrying'}
 if($gui.ExitCode -ne 0){throw 'Desktop self-test failed'}
 $g=Get-Content -LiteralPath "$stage\gui-test.json" -Raw|ConvertFrom-Json
 if(!$g.success -or $g.compile.sha256 -ne 'F14B4FAB8DB2172786F7E8371DF5A0D6A6B0877C49439514CF41E69C94272000'){throw 'Arduino-compatible firmware self-test hash mismatch'}
 Write-Output '  Arduino-compatible compiler, GUI and restricted-policy backend probes: PASS'
 # Selftest output paths refer to staging; remove generated data before atomic relocation.
 foreach($n in 'build','gui-test.json','gui-test-error.txt','policy-test.json','policy-test-error.txt','isp-logs'){
  $p=Join-Path $stage $n;if(Test-Path -LiteralPath $p){Remove-Item -LiteralPath $p -Recurse -Force}
 }
 New-Item -ItemType Directory -Force "$stage\build","$stage\isp-logs"|Out-Null
 & "$stage\Update-Manifest.ps1"
 Write-Output '[5/5] Finalizing installation...'
 [IO.Directory]::Move($stage,$dest)
 if(!$NoShortcut){
  try{
   $w=New-Object -ComObject WScript.Shell
   $shortcut=Join-Path ([Environment]::GetFolderPath('Desktop')) 'HERO AVR Studio.lnk'
   if(Test-Path -LiteralPath $shortcut){Write-Output 'Existing desktop shortcut preserved.'}
   else{
    $link=$w.CreateShortcut($shortcut);$link.TargetPath=Join-Path $dest 'Launch.cmd';$link.WorkingDirectory=$dest;$link.IconLocation=(Join-Path $dest 'HERO.ico')+',0';$link.Save()
    Write-Output 'Desktop shortcut created.'
   }
  }catch{Write-Output ('Shortcut warning: '+$_.Exception.Message)}
 }
 Write-Output "SUCCESS: HERO AVR Studio installed at $dest"
 Write-Output 'Internet is needed for installation only. USB serial/ISP drivers are separate Windows requirements. Optional third-party libraries can be imported locally.'
}catch{
 Write-Output ('ERROR: '+$_.Exception.Message); Write-Output $_.ScriptStackTrace
 exit 1
}finally{
 if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue}
 if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
}
exit 0
