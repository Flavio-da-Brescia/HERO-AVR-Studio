param([string]$Sdk=(Join-Path $PSScriptRoot 'dev-sdk\dotnet.exe'))
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if(!(Test-Path -LiteralPath $Sdk)){throw 'Local .NET 10 SDK not found. Supply -Sdk path to dotnet.exe.'}
if((Get-Process HeroAVRStudio -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $root 'app\HeroAVRStudio.exe')})){throw 'Close HERO AVR Studio normally before rebuilding. Do not interrupt an upload.'}
$stage=Join-Path $root ('.app-stage-'+[guid]::NewGuid().ToString('N'))
$backup=Join-Path $root ('.app-rollback-'+[guid]::NewGuid().ToString('N'))
try{
 & $Sdk publish "$root\source\HeroAVRStudio.csproj" -c Release --self-contained false -o $stage --ignore-failed-sources -p:NuGetAudit=false
 if($LASTEXITCODE -ne 0 -or !(Test-Path "$stage\HeroAVRStudio.exe")){throw 'Publish failed; current app unchanged'}
 $env:DOTNET_ROOT="$root\runtime";$env:DOTNET_ROOT_X64="$root\runtime";$env:DOTNET_MULTILEVEL_LOOKUP='0'
 $q=Start-Process "$stage\HeroAVRStudio.exe" -ArgumentList '--selftest' -PassThru
 $null=$q.Handle;$q.WaitForExit()
 if($q.ExitCode -ne 0){throw 'Integration selftest failed; current app unchanged'}
 if((Get-Process HeroAVRStudio -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $root 'app\HeroAVRStudio.exe')})){throw 'Application opened during build; close it before deployment'}
 if(Test-Path "$root\app"){Move-Item -LiteralPath "$root\app" -Destination $backup}
 try{Move-Item -LiteralPath $stage -Destination "$root\app";& "$root\Update-Manifest.ps1"}catch{
  if(Test-Path -LiteralPath "$root\app"){Move-Item -LiteralPath "$root\app" -Destination $stage}
  if(Test-Path -LiteralPath $backup){Move-Item -LiteralPath $backup -Destination "$root\app"}
  throw
 }
 if(Test-Path -LiteralPath $backup){Remove-Item -LiteralPath $backup -Recurse -Force}
 'Build, selftest and deployment successful. Launch.cmd opens the current app.'
}finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
