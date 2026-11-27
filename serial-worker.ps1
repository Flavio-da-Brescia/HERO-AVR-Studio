param([Parameter(Mandatory=$true)][string]$Port,[int]$Baud=38400,[Parameter(Mandatory=$true)][string]$Session)
$ErrorActionPreference='Stop'
$p=$null
$utf=New-Object System.Text.UTF8Encoding($false)
function Event($kind,$data){
 $obj=[ordered]@{time=[DateTimeOffset]::Now.ToString('o');kind=$kind;data=$data}
 $line=ConvertTo-Json -InputObject $obj -Depth 5 -Compress
 [IO.File]::AppendAllText((Join-Path $Session 'events.jsonl'),$line+[Environment]::NewLine,$utf)
}
try{
 if($Port -notmatch '^COM[1-9][0-9]*$' -or $Port -notin [IO.Ports.SerialPort]::GetPortNames()){throw 'Port not present'}
 $p=New-Object IO.Ports.SerialPort($Port,$Baud,'None',8,'One')
 $p.DtrEnable=$false;$p.RtsEnable=$false;$p.WriteTimeout=1000
 $p.Open();Event 'connected' "$Port $Baud"
 while(!(Test-Path (Join-Path $Session 'stop'))){
  $n=$p.BytesToRead
  if($n -gt 0){$buf=New-Object byte[] ([Math]::Min($n,4096));$read=$p.Read($buf,0,$buf.Length);if($read -gt 0){Event 'rx' ([Convert]::ToBase64String($buf,0,$read))}}
  foreach($f in Get-ChildItem -LiteralPath (Join-Path $Session 'queue') -Filter '*.json' -File|Sort-Object Name){
   try{
    $cmd=Get-Content -LiteralPath $f.FullName -Raw|ConvertFrom-Json
    switch($cmd.action){
     'send' {$b=[Convert]::FromBase64String($cmd.data);$p.Write($b,0,$b.Length);Event 'tx' $cmd.data}
     'dtr' {$p.DtrEnable=[bool]$cmd.value;Event 'dtr' $p.DtrEnable}
     'rts' {$p.RtsEnable=[bool]$cmd.value;Event 'rts' $p.RtsEnable}
     'reset' {$p.DtrEnable=$false;Start-Sleep -Milliseconds 100;$p.DtrEnable=$true;Start-Sleep -Milliseconds 100;$p.DtrEnable=$false;Event 'reset' 'DTR pulse'}
     default {throw 'Unknown command'}
    }
    Event 'ack' $f.BaseName
   }catch{Event 'command-error' ($f.BaseName+': '+$_.Exception.Message)}
   finally{Remove-Item -LiteralPath $f.FullName -Force}
  }
  Start-Sleep -Milliseconds 30
 }
}catch{Event 'error' $_.Exception.Message}
finally{if($p){if($p.IsOpen){$p.Close()};$p.Dispose()};Event 'disconnected' $Port;[IO.File]::WriteAllText((Join-Path $Session 'done'),'done')}
