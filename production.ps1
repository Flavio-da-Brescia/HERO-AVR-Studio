param([ValidateSet('list','projects','configure','project','create','status','program','report','validate-log','ports','check-port')][string]$Action='list',[string]$Batch,[string]$Name,[string]$Project,[string]$DiagnosticHex,[string]$Hex,[string]$DiagnosticSha256,[string]$Sha256,[string]$Port,[string]$Operator,[string]$Confirm,[int]$Baud=38400,[int]$TimeoutSeconds=60,[string]$Log,[string]$Revision,[int]$TargetQuantity=0)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot;$base=Join-Path $root 'production'
. (Join-Path $root 'production-protocol.ps1')
$lock=$null;$portLock=$null
function Json($v){ConvertTo-Json -InputObject $v -Depth 20}
function WriteJson($path,$obj){
 $tmp=$path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
 [IO.File]::WriteAllText($tmp,(Json $obj),(New-Object Text.UTF8Encoding($false)))
 if(Test-Path -LiteralPath $path){[IO.File]::Replace($tmp,$path,($tmp+'.bak'));Remove-Item -LiteralPath ($tmp+'.bak') -Force}else{[IO.File]::Move($tmp,$path)}
}
function ReadJson($path){Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}
function CheckHex($path){
 $offset=0L;$end=$false;$seen=New-Object 'System.Collections.Generic.HashSet[int]'
 foreach($line in Get-Content -LiteralPath $path){
  $line=$line.Trim();if(!$line){continue}
  if($end -or $line -notmatch '^:([0-9A-Fa-f]{2}){5,}$'){throw 'Invalid Intel HEX'}
  $b=@(for($i=1;$i -lt $line.Length;$i+=2){[Convert]::ToInt32($line.Substring($i,2),16)})
  $n=$b[0];$addr=256*$b[1]+$b[2];$type=$b[3]
  if($b.Count -ne ($n+5) -or (($b|Measure-Object -Sum).Sum%256) -ne 0){throw 'Invalid HEX checksum or length'}
  switch($type){
   0 {if($offset+$addr+$n -gt 30720){throw 'HEX exceeds application flash'};for($i=0;$i -lt $n;$i++){if(!$seen.Add([int]($offset+$addr+$i))){throw 'Overlapping HEX records'}}}
   1 {if($n -ne 0 -or $addr -ne 0){throw 'Invalid EOF'};$end=$true}
   2 {if($n -ne 2 -or $addr -ne 0){throw 'Invalid segment record'};$offset=16*(256*$b[4]+$b[5])}
   4 {if($n -ne 2 -or $addr -ne 0){throw 'Invalid address record'};$offset=65536L*(256*$b[4]+$b[5])}
   default {throw 'Unsupported HEX record'}
  }
 }
 if(!$end -or !$seen.Count){throw 'Incomplete HEX'}
}
function CopyHex($source,$target,$expected){
 if(!$source){throw 'Both application HEX files are required'}
 $src=(Resolve-Path -LiteralPath $source).Path
 if([IO.Path]::GetExtension($src) -ne '.hex' -or $src -like '*with_bootloader*'){throw 'Application-only HEX required'}
 Copy-Item -LiteralPath $src -Destination $target;CheckHex $target
 $hash=(Get-FileHash -LiteralPath $target).Hash
 if($expected -and $hash -ne $expected){throw 'SHA256 mismatch'}
 [pscustomobject]@{name=[IO.Path]::GetFileName($src);sha256=$hash}
}
function ProjectDir($n){
 if($n -notmatch '^[A-Za-z][A-Za-z0-9_]{0,47}$'){throw 'Invalid project name'}
 $d=Join-Path $root "projects\$n"
 if(!(Test-Path -LiteralPath (Join-Path $d 'project.json'))){throw 'Create the managed project first'}
 $d
}
function Pair($d){
 $pointer=ReadJson (Join-Path $d 'production.json')
 if($pointer.revision -notmatch '^[a-f0-9]{32}$'){throw 'Invalid production configuration'}
 $folder=Join-Path $d "production-configs\$($pointer.revision)"
 $m=ReadJson (Join-Path $folder 'pair.json')
 [pscustomobject]@{folder=$folder;pair=$m}
}
function Records($dir){
 foreach($a in Get-ChildItem -LiteralPath (Join-Path $dir 'attempts') -Directory|Sort-Object Name){
  if(Test-Path (Join-Path $a.FullName 'result.json')){ReadJson (Join-Path $a.FullName 'result.json')}
  elseif(Test-Path (Join-Path $a.FullName 'started.json')){ReadJson (Join-Path $a.FullName 'started.json')}
 }
}
function Stats($records){
 $a=@($records|Where-Object status -eq 'passed');$b=@($records|Where-Object status -eq 'failed');$u=@($records|Where-Object status -eq 'unknown')
 [ordered]@{attempts=$records.Count;passed=$a.Count;failed=$b.Count;unknown=$u.Count;yield_percent=$(if($records.Count){[Math]::Round(100*$a.Count/$records.Count,2)}else{0})}
}
function AssertLocked($path,$hash){if($hash -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $path).Hash -ne $hash){throw 'Locked firmware checksum mismatch'};CheckHex $path}
function Progress($phase,$state,$message){
 WriteJson (Join-Path $attempt 'progress.json') ([ordered]@{phase=$phase;message=$message;diagnostics=$(if($null -ne $state){Test-Summary $state}else{$null})})
}
function Upload($file,$hash,$label){
 $raw=& (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'upload.ps1') -Port $m.port -Hex $file -Sha256 $hash
 $rc=$LASTEXITCODE
 [IO.File]::WriteAllText((Join-Path $attempt ($label+'-upload.json')),($raw -join [Environment]::NewLine))
 $u=($raw -join [Environment]::NewLine)|ConvertFrom-Json
 if($rc -ne 0 -or $u.success -ne $true -or $u.verified -ne $true -or $u.sha256 -ne $hash){throw "$label upload/verification failed: $($u.error)"}
 $u
}
function RunDiagnostic($state){
 $sp=$null;$pending='';$quiet=$null
 $logPath=Join-Path $attempt 'diagnostic-serial.txt'
 $utf=New-Object Text.UTF8Encoding($false)
 try{
  $sp=New-Object IO.Ports.SerialPort($m.port,[int]$m.diagnostic_baud,'None',8,'One')
  $sp.DtrEnable=$false;$sp.RtsEnable=$false;$sp.ReadTimeout=100;$sp.Open()
  $sp.DtrEnable=$false;Start-Sleep -Milliseconds 100;$sp.DiscardInBuffer();$sp.DtrEnable=$true;Start-Sleep -Milliseconds 100;$sp.DtrEnable=$false
  $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $m.timeout_seconds){
   $chunk=$sp.ReadExisting()
   if($chunk.Length){
    [IO.File]::AppendAllText($logPath,$chunk,$utf);$pending+=$chunk
    if($pending.Length -gt 65536){throw 'Diagnostic line buffer overflow'}
    while(($idx=$pending.IndexOf([char]10)) -ge 0){
     $line=$pending.Substring(0,$idx).TrimEnd([char]13);$pending=$pending.Substring($idx+1)
     Add-TestLine $state $line
     Progress 'diagnostics' $state 'Receiving diagnostic results'
     if($state.errors.Count){throw ($state.errors -join '; ')}
    }
    if($state.complete -and $null -eq $quiet){$quiet=$sw.ElapsedMilliseconds}
   }
   if($state.complete -and $null -ne $quiet -and ($sw.ElapsedMilliseconds-$quiet) -ge 750 -and !$pending.Trim()){return}
   Start-Sleep -Milliseconds 25
  }
  throw 'Diagnostic timeout or incomplete test sequence'
 }finally{if($sp){if($sp.IsOpen){$sp.Close()};$sp.Dispose()}}
}
try{
 if($Action -eq 'ports'){Json @{success=$true;ports=@([IO.Ports.SerialPort]::GetPortNames()|Sort-Object)};exit 0}
 if($Action -eq 'check-port'){
  if($Port -notmatch '^COM[1-9][0-9]*$' -or $Port -notin [IO.Ports.SerialPort]::GetPortNames()){throw 'Selected COM port is not present'}
  if($Baud -lt 300 -or $Baud -gt 1000000){throw 'Invalid diagnostic baud'}
  $portLock=[IO.File]::Open((Join-Path $base ($Port+'.lock')),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  $sp=New-Object IO.Ports.SerialPort($Port,$Baud,'None',8,'One')
  try{$sp.DtrEnable=$false;$sp.RtsEnable=$false;$sp.Open()}finally{if($sp.IsOpen){$sp.Close()};$sp.Dispose()}
  Json @{success=$true;port=$Port;baud=$Baud;available=$true};exit 0
 }
 if($Action -eq 'validate-log'){
  
  $s=New-TestState;foreach($line in Get-Content -LiteralPath $Log){Add-TestLine $s $line}
  $r=Test-Summary $s;Json $r;if($r.success){exit 0}else{exit 1}
 }
 if($Action -eq 'projects'){
  $items=@(Get-ChildItem (Join-Path $root 'projects') -Directory|Where-Object{Test-Path (Join-Path $_.FullName 'project.json')}|ForEach-Object{
   $config=$null;try{$config=(Pair $_.FullName).pair}catch{}
   [pscustomobject]@{name=$_.Name;configured=($null -ne $config);configuration=$config}
  })
  Json @{success=$true;projects=$items};exit 0
 }
 if($Action -eq 'project'){ $p=ProjectDir $Project;$cfg=$null;try{$cfg=(Pair $p).pair}catch{};Json @{success=$true;project=$Project;configured=($null -ne $cfg);configuration=$cfg};exit 0 }
 if($Action -eq 'configure'){
  $p=ProjectDir $Project
  if($Baud -lt 300 -or $Baud -gt 1000000 -or $TimeoutSeconds -lt 5 -or $TimeoutSeconds -gt 600){throw 'Baud 300-1000000; timeout 5-600 s'}
  $id=[guid]::NewGuid().ToString('N');$folder=Join-Path $p "production-configs\$id";New-Item -ItemType Directory -Force $folder|Out-Null
  $diag=CopyHex $DiagnosticHex (Join-Path $folder 'diagnostic.hex') $DiagnosticSha256
  $prod=CopyHex $Hex (Join-Path $folder 'firmware.hex') $Sha256
  if($diag.sha256 -eq $prod.sha256){throw 'Diagnostic and production HEX must differ'}
  $pair=[ordered]@{schema_version=2;project=$Project;revision=$id;created=[DateTimeOffset]::Now.ToString('o');diagnostic_name=$diag.name;diagnostic_sha256=$diag.sha256;source_name=$prod.name;sha256=$prod.sha256;diagnostic_baud=$Baud;timeout_seconds=$TimeoutSeconds}
  WriteJson (Join-Path $folder 'pair.json') $pair
  WriteJson (Join-Path $p 'production.json') @{schema_version=2;revision=$id}
  Json @{success=$true;configuration=$pair};exit 0
 }
 if($Action -eq 'list'){
  $items=@(Get-ChildItem -LiteralPath $base -Directory|Where-Object{Test-Path (Join-Path $_.FullName 'batch.json')}|ForEach-Object{ReadJson (Join-Path $_.FullName 'batch.json')})
  Json @{success=$true;batches=@($items|Sort-Object created -Descending)};exit 0
 }
 if($Action -eq 'create'){
  if(!$Name -or $Name.Length -gt 80 -or !$Operator -or $Operator.Length -gt 80){throw 'Specify batch and operator names'}
  if($Port -notmatch '^COM[1-9][0-9]*$'){throw 'Explicit COM port required'}
  $p=ProjectDir $Project;$cfg=Pair $p;$c=$cfg.pair
  if($Revision -and ($Revision -notmatch '^[a-f0-9]{32}$' -or $Revision -cne $c.revision)){throw 'Project firmware configuration changed; review the new revision'}
  if($TargetQuantity -lt 0 -or $TargetQuantity -gt 1000000){throw 'Target quantity must be 0 to 1000000'}
  $id=[guid]::NewGuid().ToString('N');$dir=Join-Path $base $id;New-Item -ItemType Directory -Force (Join-Path $dir 'attempts')|Out-Null
  $null=CopyHex (Join-Path $cfg.folder 'diagnostic.hex') (Join-Path $dir 'diagnostic.hex') $c.diagnostic_sha256
  $null=CopyHex (Join-Path $cfg.folder 'firmware.hex') (Join-Path $dir 'firmware.hex') $c.sha256
  $m=[ordered]@{schema_version=2;id=$id;name=$Name;operator=$Operator;target_quantity=$TargetQuantity;port=$Port;project=$Project;revision=$c.revision;created=[DateTimeOffset]::Now.ToString('o');diagnostic_name=$c.diagnostic_name;diagnostic_sha256=$c.diagnostic_sha256;source_name=$c.source_name;sha256=$c.sha256;diagnostic_baud=$c.diagnostic_baud;timeout_seconds=$c.timeout_seconds}
  WriteJson (Join-Path $dir 'batch.json') $m;Json @{success=$true;batch=$m};exit 0
 }
 if($Batch -notmatch '^[a-f0-9]{32}$'){throw 'Specify valid batch ID'}
 $dir=Join-Path $base $Batch;$m=ReadJson (Join-Path $dir 'batch.json')
 if($Action -in 'status','report'){
  $records=@(Records $dir);$r=@{success=$true;batch=$m;statistics=(Stats $records);records=$records}
  if($Action -eq 'report'){
   $reports=Join-Path $dir 'reports';New-Item -ItemType Directory -Force $reports|Out-Null;$csv=Join-Path $reports ([guid]::NewGuid().ToString('N')+'.csv')
   $cols=@('serial','sequence','started','finished','operator','port','status','diagnostic_passed','production_verified','duration_s','diagnostic_sha256','sha256','error')
   if($records.Count){$records|Select-Object $cols|Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8}else{Set-Content -LiteralPath $csv -Value ($cols -join ',') -Encoding UTF8};$r.csv=$csv
  }
  Json $r;exit 0
 }
 if($m.schema_version -ne 2){throw 'Legacy batch is read-only; create a new diagnostic-gated batch'}
 if($Confirm -cne 'PROGRAM-BOARD'){throw 'Explicit confirmation required: PROGRAM-BOARD'}
 if($m.port -notmatch '^COM[1-9][0-9]*$' -or $m.port -notin [IO.Ports.SerialPort]::GetPortNames()){throw 'Configured port is not connected'}
 if($m.timeout_seconds -lt 5 -or $m.timeout_seconds -gt 600){throw 'Invalid locked diagnostic settings'}
 $lock=[IO.File]::Open((Join-Path $dir 'batch.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 $portLock=[IO.File]::Open((Join-Path $base ($m.port+'.lock')),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 $diagFile=Join-Path $dir 'diagnostic.hex';$prodFile=Join-Path $dir 'firmware.hex'
 AssertLocked $diagFile $m.diagnostic_sha256;AssertLocked $prodFile $m.sha256
 $seq=1;$existing=@(Get-ChildItem (Join-Path $dir 'attempts') -Directory|Where-Object Name -Match '^[0-9]{8}$'|ForEach-Object{[int]$_.Name})
 if($existing.Count){$seq=[int](1+($existing|Measure-Object -Maximum).Maximum)}
 if($seq -gt 99999999){throw 'Sequence limit reached'}
 $attempt=Join-Path $dir ('attempts\'+$seq.ToString('D8'));New-Item -ItemType Directory $attempt|Out-Null
 $record=[ordered]@{serial=('HERO-'+$Batch+'-'+$seq.ToString('D8'));sequence=$seq;started=[DateTimeOffset]::Now.ToString('o');finished='';operator=$m.operator;port=$m.port;status='unknown';duration_s=0;sha256=$m.sha256;diagnostic_sha256=$m.diagnostic_sha256;diagnostic_upload_id='';upload_id='';diagnostic_passed=$false;production_verified=$false;diagnostics=$null;error='Interrupted or not completed'}
 WriteJson (Join-Path $attempt 'started.json') $record
 $sw=[Diagnostics.Stopwatch]::StartNew();$state=New-TestState
 try{
  Progress 'upload-diagnostic' $null 'Uploading diagnostic firmware'
  $u=Upload $diagFile $m.diagnostic_sha256 'diagnostic';$record.diagnostic_upload_id=$u.upload_id
  Progress 'diagnostics' $state 'Waiting for test results'
  RunDiagnostic $state
  $summary=Test-Summary $state;$record.diagnostics=$summary
  if(!$summary.success){throw 'Diagnostics did not pass; production firmware blocked'}
  $record.diagnostic_passed=$true
  AssertLocked $prodFile $m.sha256
  Progress 'upload-production' $state 'All tests passed; uploading production firmware'
  $u=Upload $prodFile $m.sha256 'production';$record.upload_id=$u.upload_id
  $record.production_verified=$true;$record.status='passed';$record.error=''
 }catch{$record.status='failed';$record.error=$_.Exception.Message;$record.diagnostics=Test-Summary $state}
 $sw.Stop();$record.duration_s=[Math]::Round($sw.Elapsed.TotalSeconds,3);$record.finished=[DateTimeOffset]::Now.ToString('o')
 WriteJson (Join-Path $attempt 'result.json') $record
 Progress $record.status $state $(if($record.status -eq 'passed'){'Production verified'}else{$record.error})
 $ok=$record.status -eq 'passed';Json @{success=$ok;record=$record;statistics=(Stats @(Records $dir));batch=$m};if($ok){exit 0}else{exit 1}
}catch{Json @{success=$false;error=$_.Exception.Message};exit 1}
finally{if($portLock){$portLock.Dispose()};if($lock){$lock.Dispose()}}
