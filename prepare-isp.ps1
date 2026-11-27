param([ValidateSet('uno','nano-new','nano-old','pro8')][string]$Board='uno',[string]$Port,[switch]$CompileOnly,[string]$Confirm,[string]$ProgressPath)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$out=Join-Path $root ('isp-logs\prepare-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $out|Out-Null
function Finish($ok,$extra){
 $r=[ordered]@{success=$ok;action='prepare-programmer';board=$Board;port=$Port;output_dir=$out}
 foreach($k in $extra.Keys){$r[$k]=$extra[$k]}
 $j=$r|ConvertTo-Json -Depth 8
 [IO.File]::WriteAllText("$out\result.json",$j)
 $j
 if($ok){exit 0}else{exit 1}
}
try{
 if(!$CompileOnly){
  if($Confirm -cne 'UPLOAD-HELPER'){throw 'Explicit helper upload confirmation required'}
  if($Port -notmatch '^COM[1-9][0-9]*$' -or $Port -notin [IO.Ports.SerialPort]::GetPortNames()){throw 'Select connected helper Arduino COM port'}
 }
 . "$root\hero-build.ps1"
 $profile=if($Board -eq 'pro8'){'helper8'}else{'helper16'}
 $baud=if($Board -in 'nano-old','pro8'){57600}else{115200}
 if($ProgressPath){[IO.File]::WriteAllText($ProgressPath,"Compiling ArduinoISP for selected helper ...")}
 $b=Invoke-HeroBuild "$root\examples\ArduinoISP" "$out\no-user-libraries" $out $profile
 if(!$b.success){throw "ArduinoISP compilation failed; inspect $($b.compiler_log)"}
 if($CompileOnly){Finish $true @{compiled=$true;verified=$false;hex=$b.hex_path;sha256=$b.sha256;memory=$b.memory}}
 $hex=$b.hex_path
 if((Get-FileHash -LiteralPath $hex).Hash -ne $b.sha256){throw 'Compiler output checksum mismatch'}
 $exe="$root\toolchain\avrdude\bin\avrdude.exe";$conf="$root\toolchain\avrdude\etc\avrdude.conf"
 $argsList=@('-C',('"{0}"' -f $conf),'-p','atmega328p','-c','arduino','-P',$Port,'-b',"$baud",'-D','-U',('"flash:w:{0}:i"' -f $hex),'-U',('"flash:v:{0}:i"' -f $hex))
 if($ProgressPath){[IO.File]::WriteAllText($ProgressPath,"Uploading and verifying ArduinoISP (45 s timeout) ...")}
 $p=Start-Process -FilePath $exe -ArgumentList $argsList -RedirectStandardOutput "$out\upload.stdout.log" -RedirectStandardError "$out\upload.stderr.log" -NoNewWindow -PassThru
 $null=$p.Handle
 if(!$p.WaitForExit(45000)){$p.Kill();$p.WaitForExit();throw 'Helper upload timed out after 45 seconds; helper firmware may be incomplete'}
 if($p.ExitCode -ne 0 -or (Get-Content "$out\upload.stderr.log" -Raw) -notmatch 'bytes of flash verified'){throw "Helper upload not verified; inspect $out\upload.stderr.log"}
 Finish $true @{compiled=$true;verified=$true;sha256=$b.sha256;baud=$baud}
}catch{Finish $false @{error=$_.Exception.Message}}
