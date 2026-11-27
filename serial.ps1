param([ValidateSet('start','stop','read','send','reset','dtr','rts','status')][string]$Action='status',[string]$Port,[int]$Baud=38400,[string]$Session,[string]$Data,[string]$Value,[int]$Tail=100)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$logs=Join-Path $root 'serial-logs'
function Reply($obj){ConvertTo-Json -InputObject $obj -Depth 12}
function Events($dir,$n){$p=Join-Path $dir 'events.jsonl';if(Test-Path $p){foreach($line in Get-Content -LiteralPath $p -Tail $n){try{ConvertFrom-Json -InputObject $line}catch{}}}}
function ResolveSession{
 if($Session -notmatch '^[a-f0-9]{32}$'){throw 'Specify session ID'}
 $dir=Join-Path $logs $Session
 if(!(Test-Path -LiteralPath $dir)){throw 'Unknown session'}
 $dir
}
try{
 if($Action -eq 'start'){
  if($Port -notmatch '^COM[1-9][0-9]*$' -or $Port -notin [IO.Ports.SerialPort]::GetPortNames()){throw 'Port not present'}
  if($Baud -lt 300 -or $Baud -gt 2000000){throw 'Invalid baud'}
  $id=[guid]::NewGuid().ToString('N');$dir=Join-Path $logs $id
  New-Item -ItemType Directory -Path (Join-Path $dir 'queue') -Force|Out-Null
  $p=Start-Process (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',('"{0}"' -f (Join-Path $root 'serial-worker.ps1')),'-Port',$Port,'-Baud',$Baud,'-Session',('"{0}"' -f $dir)) -WindowStyle Hidden -PassThru
  @{port=$Port;baud=$Baud;pid=$p.Id;session=$id}|ConvertTo-Json|Set-Content (Join-Path $dir 'session.json')
  for($i=0;$i -lt 50;$i++){ $e=@(Events $dir 10);if(@($e|Where-Object kind -eq 'connected').Count){Reply @{success=$true;session=$id;port=$Port;baud=$Baud;log=(Join-Path $dir 'events.jsonl')};exit 0};if(Test-Path (Join-Path $dir 'done')){throw (($e|Where-Object kind -eq 'error'|Select-Object -Last 1).data)};Start-Sleep -Milliseconds 100 }
  New-Item -ItemType File -Path (Join-Path $dir 'stop') -Force|Out-Null
  throw 'Connection timed out'
 }
 $dir=ResolveSession
 if($Action -eq 'read' -or $Action -eq 'status'){
  $meta=Get-Content (Join-Path $dir 'session.json') -Raw|ConvertFrom-Json
  $active=(!(Test-Path (Join-Path $dir 'done')) -and $null -ne (Get-Process -Id $meta.pid -ErrorAction SilentlyContinue))
  $result=@{success=$true;session=$Session;active=$active;port=$meta.port;baud=$meta.baud;log=(Join-Path $dir 'events.jsonl')}
  if($Action -eq 'read'){$result.events=@(Events $dir ([Math]::Max(1,[Math]::Min(5000,$Tail))))}
  Reply $result;exit 0
 }
 if($Action -eq 'stop'){
  New-Item -ItemType File -Path (Join-Path $dir 'stop') -Force|Out-Null
  for($i=0;$i -lt 50;$i++){if(Test-Path (Join-Path $dir 'done')){Reply @{success=$true;session=$Session;active=$false};exit 0};Start-Sleep -Milliseconds 100}
  throw 'Worker did not confirm port release'
 }
 if(Test-Path (Join-Path $dir 'done')){throw 'Session closed'}
 $cmd=@{action=$Action}
 if($Action -eq 'send'){if(!$Data){throw 'Specify base64 data'};$null=[Convert]::FromBase64String($Data);$cmd.data=$Data}
 if($Action -in 'dtr','rts'){if($Value -notin 'true','false'){throw 'Specify true or false'};$cmd.value=($Value -eq 'true')}
 $id=[DateTime]::UtcNow.Ticks.ToString()+'-'+[guid]::NewGuid().ToString('N')
 $tmp=Join-Path $dir ($id+'.tmp')
 $cmd|ConvertTo-Json -Compress|Set-Content -LiteralPath $tmp -Encoding UTF8
 Move-Item -LiteralPath $tmp -Destination (Join-Path $dir ('queue\'+$id+'.json'))
 for($i=0;$i -lt 50;$i++){
  foreach($e in @(Events $dir 200)){if($e.kind -eq 'ack' -and $e.data -eq $id){Reply @{success=$true;session=$Session;command=$id};exit 0};if($e.kind -eq 'command-error' -and $e.data.StartsWith($id)){throw $e.data}}
  Start-Sleep -Milliseconds 100
 }
 throw 'Command acknowledgement timed out; inspect log before retrying'
}catch{Reply @{success=$false;error=$_.Exception.Message};exit 1}
