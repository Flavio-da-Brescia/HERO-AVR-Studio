param([ValidateSet('list','create','history','build','release','restore','rebuild','backup')][string]$Action='list',[string]$Name,[string]$Source,[string]$BuildId,[string]$Version,[string]$Destination,[string]$Diagnostic,[ValidateSet('Arduino','NativeC','NativeCpp')][string]$Mode)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
. (Join-Path $root 'hero-build.ps1')
$projects=Join-Path $root 'projects'
function Reply($obj){ConvertTo-Json -InputObject $obj -Depth 16}
function WriteJson($path,$obj){[IO.File]::WriteAllText($path,(ConvertTo-Json -InputObject $obj -Depth 16),(New-Object Text.UTF8Encoding($false)))}
function ReadJson($path){Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}
function ValidName($n){if($n -notmatch '^[A-Za-z][A-Za-z0-9_]{0,47}$' -or $n -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$'){throw 'Invalid project name: use letters, digits, underscore'}}
function ValidVersion($v){if($v -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*)?$' -or $v.Length -gt 64){throw 'Use semantic version, e.g. 1.0.0'}}
function CopyTree($from,$to){
 if(Test-Path -LiteralPath $to){throw "Destination exists: $to"}
 $item=Get-Item -LiteralPath $from
 if(!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Expected ordinary directory'}
 if(@(Get-ChildItem -LiteralPath $from -Recurse -Force|Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Links are not supported'}
 Copy-Item -LiteralPath $from -Destination $to -Recurse -Force
}
function TreeHashes($p){
 foreach($f in Get-ChildItem -LiteralPath $p -File -Recurse -Force|Sort-Object FullName){[pscustomobject]@{path=$f.FullName.Substring($p.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash}}
}
function VerifyTree($p,$expected){
 $current=@(TreeHashes $p);$expected=@($expected)
 if($current.Count -ne $expected.Count){throw "Snapshot file count changed: $p"}
 for($i=0;$i -lt $current.Count;$i++){if($current[$i].path -cne $expected[$i].path -or $current[$i].sha256 -ne $expected[$i].sha256){throw "Snapshot integrity mismatch: $($current[$i].path)"}}
}
function Toolchain(){
 $paths=@('hero-build.ps1','hero-preprocess.ps1','boards\ATmega328P_8MHz.json','compatibility\arduino-avr\platform.txt','compatibility\arduino-avr\boards.txt','toolchain\avr-gcc\bin\avr-gcc.exe','toolchain\avr-gcc\bin\avr-g++.exe')
 @($paths|ForEach-Object{[pscustomobject]@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash}})
}
function BuildSnapshot($sketch,$libs,$output){
 Invoke-HeroBuild $sketch $libs $output
}
try{
 if($Action -eq 'list'){
  $items=@(Get-ChildItem -LiteralPath $projects -Directory|Where-Object{Test-Path (Join-Path $_.FullName 'project.json')}|ForEach-Object{ReadJson (Join-Path $_.FullName 'project.json')})
  Reply @{success=$true;projects=@($items|Sort-Object updated -Descending)};exit 0
 }
 ValidName $Name
 $p=Join-Path $projects $Name
 $manifest=Join-Path $p 'project.json'
 $src=Join-Path $p "src\$Name"
 if($Diagnostic -and $Action -in 'create','list','backup'){throw 'Diagnostic is supported for history, build, release, rebuild and restore'}
 if($Action -eq 'create'){
  if(Test-Path -LiteralPath $p){throw 'Project already exists'}
  if(!$Mode){
   if($Source -and !(Test-Path (Join-Path $Source ((Split-Path $Source -Leaf)+'.ino')))){
    if(Test-Path (Join-Path $Source 'main.c')){$Mode='NativeC'}elseif(Test-Path (Join-Path $Source 'main.cpp')){$Mode='NativeCpp'}else{throw 'Native import requires main.c or main.cpp'}
   }else{$Mode='Arduino'}
  }
  $entry=switch($Mode){'Arduino'{"$Name.ino"};'NativeC'{'main.c'};'NativeCpp'{'main.cpp'}}
  if($Source){
   $original=(Resolve-Path -LiteralPath $Source).Path
   if(!(Test-Path -LiteralPath $original -PathType Container)){throw 'Source must be sketch directory'}
   $old=Split-Path $original -Leaf
   $oldEntry=if($Mode -eq 'Arduino'){"$old.ino"}else{$entry}
   if(!(Test-Path -LiteralPath (Join-Path $original $oldEntry))){throw "Source must contain $oldEntry"}
   if($Mode -ne 'Arduino' -and @(Get-ChildItem -LiteralPath $original -File -Filter '*.ino').Count){throw 'Native source cannot include .ino files'}
   if($p.StartsWith($original+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Source cannot contain destination'}
  }
  $stage=Join-Path $projects ('.new-'+[guid]::NewGuid().ToString('N'))
  try{
   New-Item -ItemType Directory -Force "$stage\src","$stage\builds","$stage\releases","$stage\backups"|Out-Null
   $target=Join-Path $stage "src\$Name"
   if(!$Mode){
   if($Source -and !(Test-Path (Join-Path $Source ((Split-Path $Source -Leaf)+'.ino')))){
    if(Test-Path (Join-Path $Source 'main.c')){$Mode='NativeC'}elseif(Test-Path (Join-Path $Source 'main.cpp')){$Mode='NativeCpp'}else{throw 'Native import requires main.c or main.cpp'}
   }else{$Mode='Arduino'}
  }
  $entry=switch($Mode){'Arduino'{"$Name.ino"};'NativeC'{'main.c'};'NativeCpp'{'main.cpp'}}
  if($Source){CopyTree $original $target;if($Mode -eq 'Arduino' -and $old -cne $Name){if(Test-Path -LiteralPath (Join-Path $target "$Name.ino")){throw 'Conflicting .ino name'};Rename-Item -LiteralPath (Join-Path $target "$old.ino") -NewName "$Name.ino"}}
   else{New-Item -ItemType Directory $target|Out-Null;$lines=if($Mode -eq 'Arduino'){@('void setup() {}','void loop() {}')}else{@('#include <avr/io.h>','#include <util/delay.h>','int main(void) {','  DDRB |= _BV(DDB5);','  for (;;) {','    PORTB ^= _BV(PORTB5);','    _delay_ms(500);','  }','}')}
   Set-Content -LiteralPath (Join-Path $target $entry) -Encoding ASCII -Value $lines}
   $now=[DateTimeOffset]::Now.ToString('o')
   $m=[ordered]@{schema_version=1;name=$Name;board='arduino:avr:pro:cpu=8MHzatmega328';mode=$Mode;sketch="src/$Name/$entry";created=$now;updated=$now;latest_build='';latest_release=''}
   WriteJson (Join-Path $stage 'project.json') $m
   Move-Item -LiteralPath $stage -Destination $p
  }finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
  Reply @{success=$true;project=$m;sketch_path=(Join-Path $src $entry)};exit 0
 }
 if(!(Test-Path -LiteralPath $manifest)){throw 'Project does not exist'}
 $m=ReadJson $manifest
 $sourceName=$Name
 $buildRoot=Join-Path $p 'builds'
 $releaseRoot=Join-Path $p 'releases'
 $entry=Split-Path $m.sketch -Leaf
 if($Diagnostic){
  ValidName $Diagnostic
  $sourceName=$Diagnostic
  $src=Join-Path $p "diagnostic-src\$Diagnostic"
  $entry="$Diagnostic.ino"
  if(!(Test-Path -LiteralPath (Join-Path $src $entry) -PathType Leaf)){throw 'Diagnostic source not found'}
  $buildRoot=Join-Path $p "diagnostic-builds\$Diagnostic"
  $releaseRoot=Join-Path $p "diagnostic-releases\$Diagnostic"
  New-Item -ItemType Directory -Force $buildRoot,$releaseRoot|Out-Null
 }
 if($Action -eq 'history'){
  $builds=@(Get-ChildItem -LiteralPath $buildRoot -Directory|Where-Object{Test-Path (Join-Path $_.FullName 'build.json')}|ForEach-Object{ReadJson (Join-Path $_.FullName 'build.json')})
  $releases=@(Get-ChildItem -LiteralPath $releaseRoot -Directory|Where-Object{Test-Path (Join-Path $_.FullName 'release.json')}|ForEach-Object{ReadJson (Join-Path $_.FullName 'release.json')})
  Reply @{success=$true;project=$m;builds=@($builds|Sort-Object created -Descending);releases=@($releases|Sort-Object created -Descending)};exit 0
 }
 if($Action -eq 'backup'){
  $id=[guid]::NewGuid().ToString('N');$target=Join-Path $p "backups\$id";CopyTree $src $target
  Reply @{success=$true;backup=$target};exit 0
 }
 if($Action -eq 'build'){
  $id=[guid]::NewGuid().ToString('N');$b=Join-Path $buildRoot $id
  New-Item -ItemType Directory -Force "$b\src","$b\output"|Out-Null
  CopyTree $src "$b\src\$sourceName"
  CopyTree (Join-Path $root 'workspace\libraries') "$b\libraries"
  $sourceHashes=@(TreeHashes "$b\src");$libraryHashes=@(TreeHashes "$b\libraries")
  $tools=@(Toolchain)
  $result=BuildSnapshot "$b\src\$sourceName" "$b\libraries" "$b\output"
  $record=[ordered]@{schema_version=1;id=$id;project=$Name;created=[DateTimeOffset]::Now.ToString('o');board=$m.board;source_hashes=$sourceHashes;library_hashes=$libraryHashes;toolchain=$tools;result=$result}
  if($Diagnostic){$record.diagnostic=$Diagnostic};WriteJson "$b\build.json" $record
  $m.updated=[DateTimeOffset]::Now.ToString('o');if(!$Diagnostic){$m.latest_build=$id};WriteJson $manifest $m
  Reply @{success=$result.success;build=$record};if($result.success){exit 0}else{exit 1}
 }
 if($Action -eq 'release'){
  ValidVersion $Version
  if($BuildId -notmatch '^[a-f0-9]{32}$'){throw 'Specify successful build ID'}
  $b=Join-Path $buildRoot $BuildId;$record=ReadJson "$b\build.json"
  if(!$record.result.success){throw 'Cannot release failed build'}
  VerifyTree "$b\src" $record.source_hashes;VerifyTree "$b\libraries" $record.library_hashes
  if((Get-FileHash -LiteralPath $record.result.hex_path).Hash -ne $record.result.sha256){throw 'Firmware checksum mismatch'}
  $rel=Join-Path $releaseRoot $Version;if(Test-Path -LiteralPath $rel){throw 'Version already exists; immutable release'}
  $stage=Join-Path $releaseRoot ('.new-'+[guid]::NewGuid().ToString('N'))
  try{
   New-Item -ItemType Directory -Force "$stage\firmware"|Out-Null
   CopyTree "$b\src" "$stage\src";CopyTree "$b\libraries" "$stage\libraries"
   foreach($f in Get-ChildItem "$b\output" -File|Where-Object {$_.Extension -in '.hex','.elf','.eep','.log'}){Copy-Item -LiteralPath $f.FullName "$stage\firmware"}
   Copy-Item "$b\build.json" "$stage\build.json"
   $r=[ordered]@{schema_version=1;project=$Name;version=$Version;build_id=$BuildId;created=[DateTimeOffset]::Now.ToString('o');hex="firmware/"+(Split-Path $record.result.hex_path -Leaf);sha256=$record.result.sha256;board=$record.board}
   if($Diagnostic){$r.diagnostic=$Diagnostic};WriteJson "$stage\release.json" $r;Move-Item -LiteralPath $stage -Destination $rel
  }finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
  if(!$Diagnostic){$m.latest_release=$Version};$m.updated=[DateTimeOffset]::Now.ToString('o');WriteJson $manifest $m
  Reply @{success=$true;release=$r;hex_path=(Join-Path $rel $r.hex)};exit 0
 }
 ValidVersion $Version
 $rel=Join-Path $releaseRoot $Version;$r=ReadJson "$rel\release.json";$record=ReadJson "$rel\build.json"
 VerifyTree "$rel\src" $record.source_hashes;VerifyTree "$rel\libraries" $record.library_hashes
 if((Get-FileHash -LiteralPath (Join-Path $rel $r.hex)).Hash -ne $r.sha256){throw 'Release firmware integrity mismatch'}
 if($Action -eq 'restore'){
  if(!$Destination){throw 'Specify new destination folder (existing folders never overwritten)'}
  $target=[IO.Path]::GetFullPath($Destination)
  if($target.StartsWith($p+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Restore outside managed project directory'}
  if((Split-Path $target -Leaf) -ne $sourceName){throw "Restore directory must be named $sourceName"}
  CopyTree "$rel\src\$sourceName" $target
  Reply @{success=$true;sketch_path=(Join-Path $target $entry);firmware=(Join-Path $rel $r.hex);sha256=$r.sha256};exit 0
 }
 if($Action -eq 'rebuild'){
  foreach($t in $record.toolchain){if((Get-FileHash -LiteralPath (Join-Path $root $t.path)).Hash -ne $t.sha256){throw "Toolchain fingerprint changed: $($t.path)"}}
  $id=[guid]::NewGuid().ToString('N');$out=Join-Path $buildRoot "$id\output"
  New-Item -ItemType Directory -Force $out|Out-Null
  $prior=$env:HERO_NO_CACHE;try{$env:HERO_NO_CACHE='1';$result=BuildSnapshot "$rel\src\$sourceName" "$rel\libraries" $out}finally{$env:HERO_NO_CACHE=$prior}
  $match=$result.success -and $result.sha256 -eq $r.sha256
  $report=@{success=$match;rebuild_id=$id;expected_sha256=$r.sha256;actual_sha256=$result.sha256;result=$result}
  WriteJson (Join-Path (Split-Path $out -Parent) 'rebuild.json') $report
  Reply $report;if($match){exit 0}else{exit 1}
 }
}catch{Reply @{success=$false;error=$_.Exception.Message};exit 1}
