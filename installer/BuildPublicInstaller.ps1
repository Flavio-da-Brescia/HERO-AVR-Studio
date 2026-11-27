param(
 [string]$Root=(Split-Path $PSScriptRoot -Parent),
 [string]$AppDirectory,
 [string]$DotnetNoticesDirectory,
 [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Root)
if(!$AppDirectory){$AppDirectory=Join-Path $root 'app'}
if(!$DotnetNoticesDirectory){$DotnetNoticesDirectory=Join-Path $root 'runtime'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $root 'distribution'}
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $out|Out-Null
$exe=Join-Path $out 'HERO_AVR_Studio_Setup.exe'
if(Test-Path -LiteralPath $exe){throw 'Output already exists; choose a new output directory'}
$csc=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path $csc)){throw 'Windows .NET Framework C# compiler unavailable'}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('hero-public-build-'+[guid]::NewGuid().ToString('N'))
$stage=Join-Path $temp 'payload'
function CopyFile($source,$relative){
 $dest=Join-Path $stage $relative
 New-Item -ItemType Directory -Force (Split-Path $dest -Parent)|Out-Null
 Copy-Item -LiteralPath $source -Destination $dest
}
try{
 New-Item -ItemType Directory -Force $stage|Out-Null
 foreach($f in Get-ChildItem -LiteralPath $root -File -Force|Where-Object {$_.Extension -in '.ps1','.cmd','.md','.ico' -or $_.Name -in 'LICENSE','.gitignore','.gitattributes'}){
  CopyFile $f.FullName $f.Name
 }
 foreach($f in Get-ChildItem -LiteralPath "$root\source" -File | Where-Object {$_.Extension -in '.cs','.csproj'}){CopyFile $f.FullName ('source\'+$f.Name)}
 foreach($dir in 'examples','tests','boards','assets','diagnostics\profiles'){
  foreach($f in Get-ChildItem -LiteralPath (Join-Path $root $dir) -File -Recurse){
   if($f.Extension -notin '.ino','.c','.cpp','.h','.json','.properties','.png'){throw "Unexpected fixture file: $($f.FullName)"}
   CopyFile $f.FullName $f.FullName.Substring($root.Length+1)
  }
 }
 CopyFile "$root\licenses\THIRD_PARTY.md" 'licenses\THIRD_PARTY.md'
 foreach($n in 'LICENSE.txt','ThirdPartyNotices.txt'){CopyFile (Join-Path $DotnetNoticesDirectory $n) ('licenses\dotnet-apphost\'+$n)}
 foreach($n in 'HeroAVRStudio.exe','HeroAVRStudio.dll','HeroAVRStudio.deps.json','HeroAVRStudio.runtimeconfig.json'){CopyFile (Join-Path $AppDirectory $n) ('app\'+$n)}
 foreach($n in 'Bootstrap.cs','InstallOnline.ps1','dependencies.json','BuildPublicInstaller.ps1','README.md'){CopyFile (Join-Path "$root\installer" $n) ('installer\'+$n)}
 $zip=Join-Path $temp 'hero.zip'
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
 $hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
 $identity=Join-Path $temp 'PayloadIdentity.cs'
 [IO.File]::WriteAllText($identity,('internal static class PayloadIdentity { internal const string Sha256 = "'+$hash+'"; }'))
 $result=Join-Path $temp 'HERO_AVR_Studio_Setup.exe'
 & $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$result" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/win32icon:$root\HERO.ico" "/resource:$zip,hero.payload" "/resource:$root\installer\InstallOnline.ps1,hero.install" "$root\installer\Bootstrap.cs" $identity
 if($LASTEXITCODE -ne 0 -or !(Test-Path $result)){throw 'Installer compile failed'}
 [IO.File]::Copy($result,$exe)
 $sha=(Get-FileHash -LiteralPath $exe).Hash
 [IO.File]::WriteAllText($exe+'.sha256',($sha+'  '+[IO.Path]::GetFileName($exe)+[Environment]::NewLine),(New-Object Text.UTF8Encoding($false)))
 $inventory=[ordered]@{version='2.0.1';setup_sha256=$sha;payload_sha256=$hash;size=(Get-Item $exe).Length;payload_files=@(Get-ChildItem -LiteralPath $stage -Recurse -File -Force|ForEach-Object{[ordered]@{path=$_.FullName.Substring($stage.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash;bytes=$_.Length}})}
 $inventory|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $out 'setup-manifest.json')
 Write-Output "SETUP: $exe"
 Write-Output "SHA256: $sha"
 Write-Output "BYTES: $((Get-Item $exe).Length)"
 Write-Output "PAYLOAD: $($inventory.payload_files.Count) files"
}finally{if(Test-Path $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
