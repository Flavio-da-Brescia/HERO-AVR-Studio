param([ValidateSet('detect','backup','verify','write-flash','write-eeprom','bootloader','configure-fuses')][string]$Action='detect',[ValidateSet('usbasp','arduinoisp')][string]$Programmer='usbasp',[string]$Port,[string]$Hex,[string]$Sha256,[string]$Confirm,[switch]$ExternalClockConfirmed,[string]$ProgressPath)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$out=Join-Path $root ('isp-logs\'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $out|Out-Null
$tool=Join-Path $root 'toolchain\avrdude'
$exe=Join-Path $tool 'bin\avrdude.exe'
$conf=Join-Path $tool 'etc\avrdude.conf'
$boot=Join-Path $root 'compatibility\arduino-avr\bootloaders\atmega\ATmegaBOOT_168_atmega328_pro_8MHz.hex'
$steps=New-Object System.Collections.Generic.List[object]
function Finish($ok,$extra){
 $r=[ordered]@{schema_version=1;success=$ok;action=$Action;programmer=$Programmer;port=$Port;output_dir=$out;steps=@($steps.ToArray())}
 foreach($k in $extra.Keys){$r[$k]=$extra[$k]}
 $json=ConvertTo-Json -InputObject $r -Depth 12
 [IO.File]::WriteAllText((Join-Path $out 'result.json'),$json)
 $json
 if($ok){exit 0}else{exit 1}
}
function Run($label,$opts){
 if($ProgressPath){[IO.File]::WriteAllText($ProgressPath,("ISP: "+$label+" ..."))}
 $log=Join-Path $out ($label+'.stderr.log')
 $stdout=Join-Path $out ($label+'.stdout.log')
 $argsList=@('-C',('"{0}"' -f $conf),'-p','atmega328p')+$script:connection+$opts
 $p=Start-Process -FilePath $exe -ArgumentList $argsList -RedirectStandardOutput $stdout -RedirectStandardError $log -NoNewWindow -PassThru
 $null=$p.Handle
  $limit=if($Action -eq 'detect'){10000}else{120000}
 if(!$p.WaitForExit($limit)){
  $p.Kill();$p.WaitForExit()
  $steps.Add([pscustomobject]@{step=$label;exit_code=-1;stderr=$log;stdout=$stdout;timeout_ms=$limit})
  throw "Timeout: $label ($($limit/1000) seconds). Check programmer firmware, wiring and power."
 }
 $rc=$p.ExitCode
 $steps.Add([pscustomobject]@{step=$label;exit_code=$rc;stderr=$log;stdout=$stdout})
 if($rc -ne 0){throw "AVRDUDE failed at $label; inspect $log"}
}
function Mem($memory,$mode,$file,$format){@('-U',('"{0}:{1}:{2}:{3}"' -f $memory,$mode,$file,$format))}
function ReadFuses($label){
 $argsList=@()
 foreach($n in 'signature','lfuse','hfuse','efuse','lock'){$argsList+=Mem $n 'r' (Join-Path $out ($label+'-'+$n+'.bin')) 'r'}
 Run $label (@('-n')+$argsList)
 $result=@{}
 foreach($n in 'signature','lfuse','hfuse','efuse','lock'){$b=[IO.File]::ReadAllBytes((Join-Path $out ($label+'-'+$n+'.bin')));$result[$n]=[BitConverter]::ToString($b).Replace('-','')}
 if($result.signature -ne '1E950F'){throw 'Incorrect target signature'}
 $result
}
function CheckHex($path,$limit){
 $src=(Resolve-Path -LiteralPath $path).Path
 if([IO.Path]::GetExtension($src) -ne '.hex'){throw 'Expected Intel HEX file'}
 $base=0L;$end=$false;$bytes=0
 foreach($line in Get-Content -LiteralPath $src){
  $line=$line.Trim();if(!$line){continue}
  if($end -or $line -notmatch '^:([0-9A-Fa-f]{2}){5,}$'){throw 'Invalid Intel HEX'}
  $b=@(for($i=1;$i -lt $line.Length;$i+=2){[Convert]::ToInt32($line.Substring($i,2),16)})
  $n=$b[0];$addr=256*$b[1]+$b[2];$type=$b[3]
  if($b.Count -ne $n+5 -or (($b|Measure-Object -Sum).Sum%256) -ne 0){throw 'Invalid HEX checksum or length'}
  switch($type){
   0 {if($base+$addr+$n -gt $limit){throw 'HEX exceeds target memory limit'};$bytes+=$n}
   1 {if($n -ne 0 -or $addr -ne 0){throw 'Invalid EOF'};$end=$true}
   2 {if($n -ne 2 -or $addr -ne 0){throw 'Invalid segment record'};$base=16*(256*$b[4]+$b[5])}
   4 {if($n -ne 2 -or $addr -ne 0){throw 'Invalid address record'};$base=65536L*(256*$b[4]+$b[5])}
   default {throw 'Unsupported HEX record'}
  }
 }
 if(!$end -or !$bytes){throw 'Incomplete HEX'}
 $copy=Join-Path $out 'input.hex';Copy-Item -LiteralPath $src -Destination $copy
 $hash=(Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash
 if($Sha256 -and $Sha256 -ne $hash){throw 'SHA256 mismatch'}
 @{file=$copy;sha256=$hash;bytes=$bytes}
}
function BackupMemory{
 $a=@();$a+=Mem 'flash' 'r' (Join-Path $out 'flash-before.hex') 'i';$a+=Mem 'eeprom' 'r' (Join-Path $out 'eeprom-before.hex') 'i';Run 'backup' (@('-n')+$a)
}
try{
 if($Programmer -eq 'usbasp'){$script:connection=@('-c','usbasp','-P','usb','-B','10')}
 else{if($Port -notmatch '^COM[1-9][0-9]*$' -or $Port -notin [IO.Ports.SerialPort]::GetPortNames()){throw 'Explicit connected Arduino ISP COM port required'};$script:connection=@('-c','stk500v1','-P',$Port,'-b','19200')}
 $required=@{'write-flash'='ERASE-FLASH';'write-eeprom'='WRITE-EEPROM';'bootloader'='ERASE-AND-BURN';'configure-fuses'='SET-8MHZ-FUSES'}
 if($required.ContainsKey($Action) -and $Confirm -cne $required[$Action]){throw "Confirmation required: $($required[$Action])"}
 if($Action -in 'bootloader','configure-fuses' -and !$ExternalClockConfirmed){throw 'Confirm physical external 8 MHz crystal/resonator before selecting external clock fuses'}
 $inputHex=$null
 if($Action -in 'verify','write-flash','write-eeprom'){if(!$Hex){throw 'Specify -Hex'};$limit=if($Action -eq 'write-eeprom'){1024}else{32768};$inputHex=CheckHex $Hex $limit}
 if($Action -eq 'bootloader' -and !(Test-Path -LiteralPath $boot)){throw 'Bundled bootloader missing'}
 $before=ReadFuses 'detect'
 if($Action -eq 'detect'){Finish $true @{signature=$before.signature;fuses=$before}}
 if($Action -eq 'backup'){BackupMemory;Finish $true @{fuses=$before;flash=(Join-Path $out 'flash-before.hex');eeprom=(Join-Path $out 'eeprom-before.hex')}}
 if($Action -eq 'verify'){Run 'verify' (@('-n')+(Mem 'flash' 'v' $inputHex.file 'i'));Finish $true @{verified=$true;sha256=$inputHex.sha256;fuses=$before}}
 BackupMemory
 if($Action -eq 'write-flash'){
  Run 'erase-write' (@('-e')+(Mem 'flash' 'w' $inputHex.file 'i')+(Mem 'flash' 'v' $inputHex.file 'i'))
  Finish $true @{verified=$true;sha256=$inputHex.sha256;fuses_before=$before;warning='Chip erase removes existing bootloader and may erase EEPROM; restore separately if needed'}
 }
 if($Action -eq 'write-eeprom'){
  Run 'eeprom-write' ((Mem 'eeprom' 'w' $inputHex.file 'i')+(Mem 'eeprom' 'v' $inputHex.file 'i'))
  Finish $true @{verified=$true;sha256=$inputHex.sha256;fuses_before=$before}
 }
 if($Action -eq 'configure-fuses'){
  if($before.hfuse -ne 'DA'){throw 'Fuse-only operation requires existing bootloader-compatible high fuse DA; use bootloader workflow otherwise'}
  Run 'fuses' @('-U','efuse:w:0xFD:m','-U','hfuse:w:0xDA:m','-U','lfuse:w:0xFF:m')
 }else{
  Run 'erase-unlock' @('-e','-U','lock:w:0x3F:m')
  Run 'fuses' @('-U','efuse:w:0xFD:m','-U','hfuse:w:0xDA:m','-U','lfuse:w:0xFF:m')
  Run 'bootloader' (@('-D')+(Mem 'flash' 'w' $boot 'i')+(Mem 'flash' 'v' $boot 'i')+@('-U','lock:w:0x0F:m'))
 }
 $after=ReadFuses 'readback'
 if($after.lfuse -ne 'FF' -or $after.hfuse -ne 'DA' -or ([Convert]::ToInt32($after.efuse,16)-band 7) -ne 5){throw 'Fuse readback mismatch'}
 if($Action -eq 'bootloader' -and ([Convert]::ToInt32($after.lock,16)-band 63) -ne 15){throw 'Lock readback mismatch'}
 Finish $true @{verified=$true;fuses_before=$before;fuses_after=$after}
}catch{Finish $false @{error=$_.Exception.Message;warning='If a write step started, inspect logs and target state before retrying'}}
