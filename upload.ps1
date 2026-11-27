param([Parameter(Mandatory=$true)][string]$Port,[Parameter(Mandatory=$true)][string]$Hex,[string]$Sha256)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
try {
  if($Port -notmatch '^COM[1-9][0-9]*$'){throw 'Invalid COM port'}
  if($Port -notin [System.IO.Ports.SerialPort]::GetPortNames()){throw "Port not present: $Port"}
  $src=(Resolve-Path -LiteralPath $Hex).Path
  if([IO.Path]::GetExtension($src) -ne '.hex' -or $src -like '*with_bootloader*'){throw 'Select application-only HEX'}
  $id=[guid]::NewGuid().ToString('N')
  $out=Join-Path $root "uploads\$id"
  New-Item -ItemType Directory -Path $out | Out-Null
  $file=Join-Path $out 'firmware.hex'
  Copy-Item -LiteralPath $src -Destination $file
  $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
  if($Sha256 -and $hash -ne $Sha256){throw 'Firmware SHA256 mismatch'}
  # Validate Intel HEX checksum, record types and addresses before opening port.
  $end=$false; $bytes=0; $base=0
  foreach($line in Get-Content -LiteralPath $file){
    $line=$line.Trim()
    if(!$line){continue}
    if($end -or $line -notmatch '^:([0-9A-Fa-f]{2}){5,}$'){throw 'Invalid Intel HEX record'}
    $b=@(for($i=1;$i -lt $line.Length;$i+=2){[Convert]::ToInt32($line.Substring($i,2),16)})
    $n=$b[0]; $addr=256*$b[1]+$b[2]; $type=$b[3]
    if($b.Count -ne ($n+5) -or (($b|Measure-Object -Sum).Sum % 256) -ne 0){throw 'Invalid Intel HEX length/checksum'}
    switch($type){
      0 {if($base+$addr+$n -gt 30720){throw 'HEX exceeds application flash limit'};$bytes+=$n}
      1 {if($n -ne 0 -or $addr -ne 0){throw 'Invalid EOF'};$end=$true}
      2 {if($n -ne 2 -or $addr -ne 0){throw 'Invalid segment address'};$base=16*(256*$b[4]+$b[5])}
      4 {if($n -ne 2 -or $addr -ne 0){throw 'Invalid linear address'};$base=65536L*(256*$b[4]+$b[5])}
      default {throw "Unsupported record type $type"}
    }
  }
  if(!$end -or !$bytes){throw 'Empty or incomplete HEX'}
  $tool=Join-Path $root 'toolchain\avrdude'
  $exe=Join-Path $tool 'bin\avrdude.exe'
  $conf=Join-Path $tool 'etc\avrdude.conf'
  $stdout=Join-Path $out 'stdout.log'; $stderr=Join-Path $out 'stderr.log'
  $argsList=@('-C',('"{0}"' -f $conf),'-p','atmega328p','-c','arduino','-P',$Port,'-b','57600','-D','-U',('"flash:w:{0}:i"' -f $file),'-U',('"flash:v:{0}:i"' -f $file))
  $p=Start-Process -FilePath $exe -ArgumentList $argsList -RedirectStandardOutput $stdout -RedirectStandardError $stderr -NoNewWindow -PassThru
  $null=$p.Handle
  if(!$p.WaitForExit(90000)){ $p.Kill(); $p.WaitForExit(); throw 'Uploader timeout' }
  $rc=$p.ExitCode
  $log=Get-Content -LiteralPath $stderr -Raw
  $ok=($rc -eq 0 -and $log -match 'bytes of flash verified')
  $result=@{schema_version=1;action='upload';success=$ok;verified=$ok;exit_code=$rc;port=$Port;baud=57600;protocol='arduino/STK500v1';mcu='atmega328p';sha256=$hash;firmware=$file;upload_id=$id;stdout=$stdout;stderr=$stderr;data_bytes=$bytes}
  $json=$result|ConvertTo-Json -Depth 5
  $json|Set-Content -Encoding UTF8 "$out\result.json"
  $json
  if($ok){exit 0}else{exit 1}
}catch{ @{schema_version=1;action='upload';success=$false;error=$_.Exception.Message}|ConvertTo-Json;exit 1 }

