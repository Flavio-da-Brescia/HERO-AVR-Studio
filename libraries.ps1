param([ValidateSet('list','import')][string]$Action='list',[string]$Source)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$dest=Join-Path $root 'workspace\libraries'
$stage=$null
function Describe($dir,$kind){
 $props=@{}
 $p=Join-Path $dir.FullName 'library.properties'
 if(Test-Path -LiteralPath $p){foreach($line in Get-Content -LiteralPath $p){if($line -match '^([^#=]+)=(.*)$'){$props[$matches[1].Trim()]=$matches[2].Trim()}}}
 $examples=@()
 $ex=Join-Path $dir.FullName 'examples'
 if(Test-Path -LiteralPath $ex){$examples=@(Get-ChildItem -LiteralPath $ex -Recurse -File -Filter '*.ino'|ForEach-Object {$_.FullName})}
 [pscustomobject]@{name=$(if($props.name){$props.name}else{$dir.Name});folder=$dir.Name;version=$(if($props.version){$props.version}else{'unknown'});architectures=$(if($props.architectures){$props.architectures}else{'unknown'});dependencies=$props.depends;kind=$kind;path=$dir.FullName;examples=$examples}
}
function IsLibrary($p){
 (Test-Path -LiteralPath (Join-Path $p 'library.properties')) -or (@(Get-ChildItem -LiteralPath $p -File -Filter '*.h').Count -gt 0) -or ((Test-Path -LiteralPath (Join-Path $p 'src')) -and @(Get-ChildItem -LiteralPath (Join-Path $p 'src') -Recurse -File -Filter '*.h').Count -gt 0)
}
try{
 if($Action -eq 'list'){
  $items=@()
  foreach($d in Get-ChildItem -LiteralPath (Join-Path $root 'compatibility\arduino-avr\libraries') -Directory){$items+=Describe $d 'core'}
  foreach($d in Get-ChildItem -LiteralPath $dest -Directory){$items+=Describe $d 'local'}
  @{schema_version=1;success=$true;libraries=@($items)}|ConvertTo-Json -Depth 8;exit 0
 }
 if(!$Source){throw 'Specify -Source folder or ZIP'}
 $src=Get-Item -LiteralPath (Resolve-Path -LiteralPath $Source).Path
 $stage=Join-Path $root ('library-staging\'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $stage -Force|Out-Null
 if($src.PSIsContainer){
  $candidate=$src.FullName
  if($candidate.StartsWith($dest+'\',[StringComparison]::OrdinalIgnoreCase) -or $candidate -eq $dest){throw 'Source is already in library workspace'}
 }else{
  if($src.Extension -ne '.zip'){throw 'Only ZIP files and directories accepted'}
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $zip=[IO.Compression.ZipFile]::OpenRead($src.FullName)
  try{
   $total=0L
   foreach($entry in $zip.Entries){
    $target=[IO.Path]::GetFullPath((Join-Path $stage $entry.FullName))
    if(!$target.StartsWith($stage+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe ZIP entry'}
    $total+=$entry.Length
    if($total -gt 200MB){throw 'ZIP exceeds 200 MB expanded limit'}
   }
  }finally{$zip.Dispose()}
  [IO.Compression.ZipFile]::ExtractToDirectory($src.FullName,$stage)
  $candidate=$stage
  if(!(IsLibrary $candidate)){
   $dirs=@(Get-ChildItem -LiteralPath $stage -Directory|Where-Object {IsLibrary $_.FullName})
   if($dirs.Count -ne 1){throw 'ZIP must contain exactly one Arduino library'}
   $candidate=$dirs[0].FullName
  }
 }
 if(!(IsLibrary $candidate)){throw 'No Arduino library headers or metadata found'}
 $name=if($candidate -eq $stage){[IO.Path]::GetFileNameWithoutExtension($src.Name)}else{Split-Path $candidate -Leaf}
 if($name -notmatch '^[A-Za-z0-9_][A-Za-z0-9_. -]*$'){throw 'Invalid library folder name'}
 $target=Join-Path $dest $name
 if(Test-Path -LiteralPath $target){throw "Library already exists: $name (no overwrite)"}
 if(@(Get-ChildItem -LiteralPath $candidate -Recurse -Force|Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Library contains links; import rejected'}
 $copy=Join-Path $stage '__import_copy'
 if($candidate -eq $stage){
  $copy=Join-Path (Split-Path $stage -Parent) ([guid]::NewGuid().ToString('N'))
 }
 Copy-Item -LiteralPath $candidate -Destination $copy -Recurse
 Move-Item -LiteralPath $copy -Destination $target
 @{schema_version=1;success=$true;library=(Describe (Get-Item -LiteralPath $target) 'local')}|ConvertTo-Json -Depth 8
 exit 0
}catch{@{schema_version=1;success=$false;error=$_.Exception.Message}|ConvertTo-Json;exit 1}
finally{if($stage -and (Test-Path -LiteralPath $stage)){Remove-Item -LiteralPath $stage -Recurse -Force}}
