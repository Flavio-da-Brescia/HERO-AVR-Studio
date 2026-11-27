param([ValidateSet('status','compile','selftest','help','ports','upload','libraries','library-import')][string]$Action='help',[string]$Sketch,[string]$Port,[string]$Hex,[string]$Sha256,[string]$Source)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
. (Join-Path $root 'hero-build.ps1')
$fqbn='arduino:avr:pro:cpu=8MHzatmega328'
try {
  if($Action -eq 'help'){ @{actions=@('status','compile -Sketch <path>','selftest','ports','libraries','library-import -Source <path>','upload -Port <COM> -Hex <path> [-Sha256 <hash>]');schema_version=1;version='2.0.1';board=$fqbn}|ConvertTo-Json;exit 0 }
  if($Action -eq 'status'){
    $v='HERO Build Engine / AVR-GCC 7.3.0'
    @{schema_version=1;version='2.0.1';board=$fqbn;root=$root;engine=$v;cli=$null;board_profile='hero-atmega328p-8mhz';core_present=(Test-Path "$root\compatibility\arduino-avr\platform.txt")}|ConvertTo-Json;exit 0
  }
  if($Action -eq 'libraries'){ & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'libraries.ps1') list; exit $LASTEXITCODE }
  if($Action -eq 'library-import'){ if(!$Source){throw 'Specify -Source'}; & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'libraries.ps1') import -Source $Source; exit $LASTEXITCODE }
  if($Action -eq 'ports'){ @{schema_version=1;success=$true;ports=@([System.IO.Ports.SerialPort]::GetPortNames()|Sort-Object)}|ConvertTo-Json;exit 0 }
  if($Action -eq 'upload'){
    if(!$Port -or !$Hex){throw 'Specify -Port and -Hex'}
    if($Sha256){ & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'upload.ps1') -Port $Port -Hex $Hex -Sha256 $Sha256 }else{ & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'upload.ps1') -Port $Port -Hex $Hex }
    exit $LASTEXITCODE
  }
  if($Action -eq 'selftest'){$Sketch=Join-Path $root 'examples\BlinkA0'}
  if(-not $Sketch){throw 'Specify -Sketch'}
  $src=(Resolve-Path -LiteralPath $Sketch).Path
  $id=[guid]::NewGuid().ToString('N')
  $out=Join-Path $root "build\$id"
  New-Item -ItemType Directory -Path $out | Out-Null
  $log=Join-Path $out 'compiler.log'
  $build=Invoke-HeroBuild $src (Join-Path $root 'workspace\libraries') $out
  $rc=$build.exit_code;$hexPath=$build.hex_path;$hash=$build.sha256;$ok=$build.success
  $hex=@();if($ok){$hex=@(@{path=$hexPath;sha256=$hash})}
  $result=@{schema_version=1;action=$Action;success=$ok;exit_code=$rc;board=$fqbn;sketch=$src;build_id=$id;output_dir=$out;compiler_log=$log;stderr="$out\stderr.log";hex=$hex;hex_path=$hexPath;sha256=$hash}
  $json=$result|ConvertTo-Json -Depth 8
  $json|Set-Content -Encoding UTF8 "$out\result.json"
  $json
  if($ok){exit 0}else{exit 1}
}catch{ @{schema_version=1;success=$false;error=$_.Exception.Message}|ConvertTo-Json;exit 1 }





