param([ValidateSet('help','core','serial','plot','projects','isp','production','diagnostics')][string]$Module='help',[string]$Action,[string]$Sketch,[string]$Port,[string]$Hex,[string]$Sha256,[string]$Source,[int]$Baud,[string]$Session,[string]$Data,[string]$Value,[int]$Tail,[string]$Name,[string]$BuildId,[string]$Version,[string]$Destination,[string]$Programmer,[string]$Confirm,[switch]$ExternalClockConfirmed,[string]$Batch,[string]$Operator,[string]$Profile,[string]$FirmwareOrigin,[string]$FirmwareVersion,[string]$FirmwareDate,[string]$FirmwareDateLabel,[int]$FirmwareFlashBytes,[string]$FirmwareProject,[string]$FirmwareBuildId,[string]$Project,[string]$DiagnosticHex,[string]$DiagnosticSha256,[int]$TimeoutSeconds,[string]$Log,[string]$Revision,[int]$TargetQuantity,[string]$Mode,[string]$Diagnostic)
$ErrorActionPreference='Stop'
try{
 if($Module -eq 'help'){
  ConvertTo-Json -InputObject @{version='2.0.1';schema_version=1;root=$PSScriptRoot;documentation=@('AGENTS.md','DIAGNOSTICS.md','QUICKSTART.md','README.md');modules=@{core='help status ports compile upload selftest libraries library-import';serial='start stop read send reset dtr rts status';plot='export';projects='list create history build release restore rebuild backup; -Diagnostic NAME selects diagnostic history/build/release/rebuild/restore';isp='detect backup verify write-flash write-eeprom bootloader configure-fuses';production='list projects project configure create status program report validate-log ports check-port';diagnostics='list run history'}} -Depth 5;exit 0
 }
 $scripts=@{core='hero.ps1';serial='serial.ps1';plot='plot.ps1';projects='projects.ps1';isp='isp.ps1';production='production.ps1';diagnostics='diagnostics.ps1'}
 $script=Join-Path $PSScriptRoot $scripts[$Module]
 $forward=@()
 if($Module -eq 'plot'){if($Action -ne 'export'){throw 'Use -Action export'}}elseif($Action){$forward+=@('-Action',$Action)}
 foreach($k in $PSBoundParameters.Keys){if($k -in 'Module','Action'){continue};if($k -eq 'ExternalClockConfirmed'){if($ExternalClockConfirmed){$forward+='-ExternalClockConfirmed'}}else{$forward+=@(( '-'+$k),([string]$PSBoundParameters[$k]))}}
 & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $script @forward
 exit $LASTEXITCODE
}catch{ConvertTo-Json -InputObject @{success=$false;error=$_.Exception.Message};exit 1}
