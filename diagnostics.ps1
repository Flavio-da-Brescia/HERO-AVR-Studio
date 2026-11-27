param([ValidateSet('list','run','history')][string]$Action='list',[string]$Profile,[string]$Port,[string]$Confirm)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$base=Join-Path $root 'diagnostics'
function Json($v){ConvertTo-Json -InputObject $v -Depth 12}
function ReadJson($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function WriteJson($p,$v){[IO.File]::WriteAllText($p,(Json $v),(New-Object Text.UTF8Encoding($false)))}
$p=$null
try{
 if($Action -eq 'list'){
  $items=@(Get-ChildItem "$base\profiles" -File -Filter '*.json'|ForEach-Object{$j=ReadJson $_.FullName;[pscustomobject]@{file=$_.FullName;name=$j.name;steps=@($j.steps).Count;baud=$j.baud}})
  Json @{success=$true;profiles=$items};exit 0
 }
 if($Action -eq 'history'){
  $items=@(Get-ChildItem "$base\runs" -Directory|Where-Object {Test-Path (Join-Path $_.FullName 'result.json')}|ForEach-Object{ReadJson (Join-Path $_.FullName 'result.json')})
  Json @{success=$true;runs=@($items|Sort-Object started -Descending)};exit 0
 }
 if($Confirm -cne 'RUN-DIAGNOSTICS'){throw 'Explicit confirmation required: RUN-DIAGNOSTICS'}
 if($Port -notmatch '^COM[1-9][0-9]*$' -or $Port -notin [IO.Ports.SerialPort]::GetPortNames()){throw 'Explicit connected COM port required'}
 $source=(Resolve-Path -LiteralPath $Profile).Path
 $cfg=ReadJson $source
 if($cfg.schema_version -ne 1 -or !$cfg.name -or $cfg.baud -lt 300 -or $cfg.baud -gt 1000000 -or $cfg.timeout_ms -lt 100 -or $cfg.timeout_ms -gt 10000 -or $cfg.settle_ms -lt 0 -or $cfg.settle_ms -gt 10000){throw 'Invalid profile metadata'}
 if(@($cfg.steps).Count -lt 1 -or @($cfg.steps).Count -gt 50){throw 'Profile must contain 1-50 steps'}
 $ids=@{}
 $budget=0
 foreach($s in $cfg.steps){
  if($s.id -notmatch '^[A-Za-z][A-Za-z0-9_-]{0,63}$' -or $ids.ContainsKey($s.id) -or !$s.label){throw 'Invalid or duplicate step ID'}
  $ids[$s.id]=$true
  if($s.kind -eq 'fixture'){if(!$s.reason){throw 'Fixture step requires a reason'};continue}
  if($s.kind -notin 'exact','range','increasing' -or $s.command -notmatch '^[A-Z][A-Z0-9_]{0,31}$' -or $s.samples -lt 1 -or $s.samples -gt 100 -or [int]$s.interval_ms -lt 0 -or [int]$s.interval_ms -gt 5000){throw "Invalid step $($s.id)"}
  if($s.kind -eq 'range' -and ($null -eq $s.min -or $null -eq $s.max -or [double]$s.min -gt [double]$s.max)){throw 'Invalid range'}
  if($s.kind -eq 'exact' -and $null -eq $s.expected){throw 'Expected text missing'}
  if($s.kind -eq 'increasing' -and $s.samples -lt 2){throw 'Increasing test requires at least 2 samples'}
  $budget += [int]$s.samples*([int]$cfg.timeout_ms+[int]$s.interval_ms)
 }
 if($budget -gt 300000){throw 'Profile exceeds five-minute worst-case sampling budget'}
 $id=[guid]::NewGuid().ToString('N');$out=Join-Path $base "runs\$id"
 New-Item -ItemType Directory -Path $out|Out-Null
 Copy-Item -LiteralPath $source -Destination (Join-Path $out 'profile.json')
 $log=Join-Path $out 'serial.jsonl'
 function Log($kind,$data){$line=ConvertTo-Json -InputObject ([ordered]@{time=[DateTimeOffset]::Now.ToString('o');kind=$kind;data=$data}) -Compress;[IO.File]::AppendAllText($log,$line+[Environment]::NewLine)}
 $start=[DateTimeOffset]::Now.ToString('o');$results=New-Object 'System.Collections.Generic.List[object]'
 $connectionError=''
 try{
  $p=New-Object IO.Ports.SerialPort($Port,[int]$cfg.baud,'None',8,'One')
  $p.NewLine=[string][char]10;$p.ReadTimeout=100;$p.WriteTimeout=1000;$p.DtrEnable=$false;$p.RtsEnable=$false
  $p.Open();Log 'connected' "$Port $($cfg.baud)"
  Start-Sleep -Milliseconds ([int]$cfg.settle_ms);$p.DiscardInBuffer()
  foreach($s in $cfg.steps){
   if($s.kind -eq 'fixture'){$results.Add([pscustomobject]@{id=$s.id;label=$s.label;status='skipped';reason=$s.reason;values=@()});continue}
   $values=New-Object 'System.Collections.Generic.List[object]';$errors=New-Object 'System.Collections.Generic.List[string]';$last=$null
   for($i=0;$i -lt $s.samples;$i++){
    $token=[guid]::NewGuid().ToString('N').Substring(0,12);$prefix="HERO $token $($s.command) ";$command="$token $($s.command)"
    $p.WriteLine($command);Log 'tx' $command
    $sw=[Diagnostics.Stopwatch]::StartNew();$answer=$null
    while($sw.ElapsedMilliseconds -lt $cfg.timeout_ms){
     try{$line=$p.ReadLine().TrimEnd([char]13);Log 'rx' $line;if($line.StartsWith($prefix,[StringComparison]::Ordinal)){$answer=$line.Substring($prefix.Length);break}}catch [TimeoutException]{}
    }
    if($null -eq $answer){$errors.Add("Sample $($i+1): timeout")}
    elseif($s.kind -eq 'exact'){$values.Add($answer);if($answer -cne [string]$s.expected){$errors.Add("Sample $($i+1): unexpected response")}}
    else{
     $n=0.0
     if(![double]::TryParse($answer,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$n) -or [double]::IsNaN($n) -or [double]::IsInfinity($n)){$errors.Add("Sample $($i+1): invalid number")}
     else{$values.Add($n);if($s.kind -eq 'range' -and ($n -lt $s.min -or $n -gt $s.max)){$errors.Add("Sample $($i+1): outside range")};if($s.kind -eq 'increasing' -and $null -ne $last -and $n -le $last){$errors.Add("Sample $($i+1): not increasing")};$last=$n}
    }
    if($i+1 -lt $s.samples -and $s.interval_ms){Start-Sleep -Milliseconds ([int]$s.interval_ms)}
   }
   $stat=if($errors.Count){'failed'}else{'passed'}
   $results.Add([pscustomobject]@{id=$s.id;label=$s.label;status=$stat;reason=($errors -join '; ');values=@($values.ToArray())})
  }
 }catch{$connectionError=$_.Exception.Message;Log 'error' $connectionError}
 finally{if($p){if($p.IsOpen){$p.Close()};$p.Dispose();$p=$null};Log 'disconnected' $Port}
 $all=@($results.ToArray())
 $passed=@($all|Where-Object status -eq 'passed').Count;$failed=@($all|Where-Object status -eq 'failed').Count;$skipped=@($all|Where-Object status -eq 'skipped').Count
 $ok=(!$connectionError -and $failed -eq 0 -and $passed -gt 0 -and $all.Count -eq @($cfg.steps).Count)
 $qualification=if(!$ok){'failed'}elseif($skipped){'partial'}else{'passed'}
 $csv=Join-Path $out 'report.csv'
 $all|Select-Object id,label,status,reason,@{n='values';e={($_.values -join ';')}}|Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
 $r=[ordered]@{schema_version=1;success=$ok;qualification=$qualification;run_id=$id;profile=$cfg.name;profile_sha256=(Get-FileHash -LiteralPath (Join-Path $out 'profile.json')).Hash;port=$Port;baud=$cfg.baud;started=$start;finished=[DateTimeOffset]::Now.ToString('o');passed=$passed;failed=$failed;skipped=$skipped;steps=$all;error=$connectionError;csv=$csv;log=$log;output_dir=$out}
 WriteJson (Join-Path $out 'result.json') $r;Json $r
 if($ok){exit 0}else{exit 1}
}catch{if($p){$p.Dispose()};Json @{success=$false;error=$_.Exception.Message};exit 1}
