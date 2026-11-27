param(
 [string]$Root=$PSScriptRoot,
 [switch]$RequireOffline,
 [string]$TestPort
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Root)
$ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$tag=[guid]::NewGuid().ToString('N').Substring(0,12)
$out=Join-Path $root ('qualification\'+$tag)
New-Item -ItemType Directory -Force $out|Out-Null
$results=New-Object 'System.Collections.Generic.List[object]'
function NetworkState{
 @([Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()|Where-Object {$_.OperationalStatus -eq 'Up' -and $_.NetworkInterfaceType -ne 'Loopback'}|ForEach-Object {$_.Name})
}
function Record($name,$ok,$detail){
 $results.Add([pscustomobject]@{name=$name;passed=[bool]$ok;detail=$detail})
 Write-Output ($name+': '+$(if($ok){'PASS'}else{'FAIL'})+' | '+$detail)
 if(!$ok){throw "Qualification failure: $name"}
}
function J($script,$arguments){
 $raw=& $ps -NoProfile -NonInteractive -File (Join-Path $root $script) @arguments
 try{$r=$raw|ConvertFrom-Json}catch{throw "Invalid JSON from $script"}
 return $r
}
function Ok($r){$r.success -eq $true}
$env:PATH="$env:SystemRoot\System32;$env:SystemRoot"
$env:DOTNET_ROOT=Join-Path $root 'runtime'
$env:DOTNET_ROOT_X64=$env:DOTNET_ROOT
$env:DOTNET_MULTILEVEL_LOOKUP='0'
$env:ARDUINO_DIRECTORIES_DATA=Join-Path $out 'unused-arduino-data'
$env:ARDUINO_DIRECTORIES_USER=Join-Path $out 'unused-arduino-user'
$env:HTTP_PROXY='http://127.0.0.1:1'
$env:HTTPS_PROXY=$env:HTTP_PROXY
$env:ALL_PROXY=$env:HTTP_PROXY
$env:NO_PROXY=''
$env:HERO_NO_CACHE='1'
$before=@(NetworkState)
$success=$false;$errorText='';$project='Qualification_'+$tag
$hardware='not requested'
try{
 if($RequireOffline -and $before.Count){throw ('Active non-loopback network interfaces: '+($before -join ', ')+'; disconnect network and retry locally')}
 Record 'Bundled runtime/compiler/uploader' ((Test-Path "$root\runtime\dotnet.exe") -and (Test-Path "$root\toolchain\avr-gcc\bin\avr-gcc.exe") -and (Test-Path "$root\toolchain\avrdude\bin\avrdude.exe")) 'Required local executables present'
 Record 'No Arduino CLI' (!(Test-Path "$root\bin\arduino-cli.exe") -and !(Test-Path "$root\legacy-toolchain\arduino-cli.exe") -and !(Test-Path "$root\data\packages")) 'Legacy paths absent'
 foreach($t in @(
  @{name='NativeBlink';hash='98ED4092AE719071884B6600F88CC2FE962CD20B70F7A6FC8FBC8D3E77A78364'},
  @{name='BlinkA0';hash='F14B4FAB8DB2172786F7E8371DF5A0D6A6B0877C49439514CF41E69C94272000'},
  @{name='LoRaCompileTest';hash='B5C69AC6490FFCF7F040A7B75DCAD65A3693FCF870EEAC28E1C4595348254816'}
 )){
  $r=J 'hero.ps1' @('-Action','compile','-Sketch',(Join-Path $root ('examples\'+$t.name)))
  Record ('Uncached '+$t.name) ((Ok $r) -and $r.sha256 -eq $t.hash) ("SHA256="+$r.sha256+"; compiler log="+$r.compiler_log)
 }
 $r=J 'hero.ps1' @('-Action','compile','-Sketch',(Join-Path $root 'examples\InvalidSketch'))
 Record 'Reject invalid source' (!(Ok $r) -and !$r.hex_path) ('No firmware produced; '+$r.compiler_log)
 $r=J 'projects.ps1' @('-Action','create','-Name',$project,'-Mode','NativeC')
 Record 'Create native project' (Ok $r) $project
 $r=J 'projects.ps1' @('-Action','build','-Name',$project)
 Record 'Native project build' ((Ok $r) -and $r.build.result.sha256 -eq '98ED4092AE719071884B6600F88CC2FE962CD20B70F7A6FC8FBC8D3E77A78364') $r.build.result.sha256
 $b=$r.build
 $r=J 'projects.ps1' @('-Action','release','-Name',$project,'-BuildId',$b.id,'-Version','1.0.0')
 Record 'Immutable release' (Ok $r) $r.hex_path
 $r=J 'projects.ps1' @('-Action','rebuild','-Name',$project,'-Version','1.0.0')
 Record 'Uncached release reproducibility' ((Ok $r) -and $r.actual_sha256 -eq $b.result.sha256) $r.actual_sha256
 $r=J 'projects.ps1' @('-Action','restore','-Name',$project,'-Version','1.0.0','-Destination',(Join-Path $out $project))
 Record 'Restore native release source' ((Ok $r) -and (Test-Path -LiteralPath $r.sketch_path)) $r.sketch_path
 $good=Join-Path $out 'pass.txt';$bad=Join-Path $out 'fail.txt';$missing=Join-Path $out 'incomplete.txt'
 @('TEST 01: UART output','RESULT 01: PASS','DIAGNOSTIC COMPLETED')|Set-Content -Encoding ASCII $good
 @('TEST 01: UART output','RESULT 01: FAIL','DIAGNOSTIC COMPLETED')|Set-Content -Encoding ASCII $bad
 @('TEST 01: UART output','RESULT 01: PASS')|Set-Content -Encoding ASCII $missing
 $r=J 'production.ps1' @('-Action','validate-log','-Log',$good);Record 'Diagnostic protocol accept PASS' (Ok $r) 'Complete PASS'
 $r=J 'production.ps1' @('-Action','validate-log','-Log',$bad);Record 'Diagnostic protocol reject FAIL' (!(Ok $r)) 'Complete FAIL'
 $r=J 'production.ps1' @('-Action','validate-log','-Log',$missing);Record 'Diagnostic protocol reject missing marker' (!(Ok $r)) 'Incomplete diagnostic'
 $q=Start-Process "$root\app\HeroAVRStudio.exe" -ArgumentList '--selftest' -PassThru
 $null=$q.Handle;$q.WaitForExit()
 Record 'GUI integration selftest' ($q.ExitCode -eq 0) "Exit $($q.ExitCode)"
 if($TestPort){
  if($TestPort -notmatch '^COM[1-9][0-9]*$'){throw 'Specify a real COM port'}
  if(Get-Process HeroAVRStudio,avrdude -ErrorAction SilentlyContinue){throw 'Close all Studio windows/uploaders before physical qualification'}
  $r=J 'production.ps1' @('-Action','check-port','-Port',$TestPort,'-Baud','38400')
  Record 'Physical test port available' (Ok $r) $TestPort
  $hardware='started'
  $pn='Physical_'+$tag
  $r=J 'projects.ps1' @('-Action','create','-Name',$pn)
  Record 'Create physical test project' (Ok $r) $pn
  $prodSrc=Join-Path $root "projects\$pn\src\$pn\$pn.ino"
  @('void setup() { pinMode(13, OUTPUT); }','void loop() { digitalWrite(13, HIGH); delay(500); digitalWrite(13, LOW); delay(500); }')|Set-Content -Encoding ASCII $prodSrc
  $prod=J 'projects.ps1' @('-Action','build','-Name',$pn)
  Record 'Physical production build' (Ok $prod) $prod.build.result.sha256
  $diagDir=Join-Path $root "projects\$pn\diagnostic-src\SerialTest"
  New-Item -ItemType Directory -Force $diagDir|Out-Null
  $diagFile=Join-Path $diagDir 'SerialTest.ino'
  @('void setup() {',' Serial.begin(38400);',' delay(1500);',' Serial.println(F("ATmega328P 8 MHz | HERO installation qualification"));',' Serial.println(F("TEST 01: UART output"));',' Serial.println(F("RESULT 01: PASS"));',' Serial.println(F("DIAGNOSTIC COMPLETED"));',' Serial.flush();','}','void loop() {}')|Set-Content -Encoding ASCII $diagFile
  $diag=J 'hero.ps1' @('-Action','compile','-Sketch',$diagDir)
  Record 'Physical diagnostic build' (Ok $diag) $diag.sha256
  $config=J 'production.ps1' @('-Action','configure','-Project',$pn,'-DiagnosticHex',$diag.hex_path,'-DiagnosticSha256',$diag.sha256,'-Hex',$prod.build.result.hex_path,'-Sha256',$prod.build.result.sha256,'-Baud','38400','-TimeoutSeconds','15')
  Record 'Lock diagnostic/production pair' (Ok $config) $config.configuration.revision
  $batch=J 'production.ps1' @('-Action','create','-Project',$pn,'-Revision',$config.configuration.revision,'-Name','Installation qualification','-Operator','HERO qualification','-Port',$TestPort,'-TargetQuantity','1')
  Record 'Create qualification batch' (Ok $batch) $batch.batch.id
  $r=J 'production.ps1' @('-Action','program','-Batch',$batch.batch.id,'-Confirm','PROGRAM-BOARD')
  $r|ConvertTo-Json -Depth 16|Set-Content -Encoding UTF8 (Join-Path $out 'physical-result.json')
  Record 'Physical production gate and flash verification' ((Ok $r) -and $r.record.diagnostic_passed -and $r.record.production_verified) ("batch="+$batch.batch.id+"; seconds="+$r.record.duration_s)
  $hardware='passed; D13 blink firmware installed'
 }
 $success=$true
}catch{$errorText=$_.Exception.Message;Write-Output ("QUALIFICATION ERROR: "+$errorText)}
finally{
 $after=@(NetworkState)
 $offlineObserved=($before.Count -eq 0 -and $after.Count -eq 0)
 if($RequireOffline -and !$offlineObserved){$success=$false;$errorText='Non-loopback network interface observed active'}
 $report=[ordered]@{success=$success;root=$root;date=[DateTimeOffset]::Now.ToString('o');network_interfaces_before=$before;network_interfaces_after=$after;offline_observed=$offlineObserved;scope='Same-host or user-supplied test host; network state sampled at start/end; no network interception';hardware=$hardware;results=@($results.ToArray());error=$errorText}
 $report|ConvertTo-Json -Depth 10|Set-Content -Encoding UTF8 (Join-Path $out 'qualification.json')
 Write-Output ("REPORT: "+(Join-Path $out 'qualification.json'))
 if($success){Write-Output 'QUALIFICATION PASS'}else{Write-Output 'QUALIFICATION FAIL'}
}
if(!$success){exit 1}
