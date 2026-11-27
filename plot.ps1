param([Parameter(Mandatory=$true)][string]$Session)
$ErrorActionPreference='Stop'
try{
 if($Session -notmatch '^[a-f0-9]{32}$'){throw 'Invalid session ID'}
 $log=Join-Path $PSScriptRoot "serial-logs\$Session\events.jsonl"
 if(!(Test-Path -LiteralPath $log)){throw 'Session log not found'}
 $dir=Join-Path $PSScriptRoot ('plots\'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $dir -Force|Out-Null
 $csv=Join-Path $dir 'measurements.csv';$json=Join-Path $dir 'summary.json'
 $exe=Join-Path $PSScriptRoot 'runtime\dotnet.exe'
 $p=Start-Process -FilePath $exe -ArgumentList @(('"{0}"' -f (Join-Path $PSScriptRoot 'app\HeroAVRStudio.dll')),'--plot-export',('"{0}"' -f $log),('"{0}"' -f $csv),('"{0}"' -f $json)) -PassThru
 $null=$p.Handle
 if(!$p.WaitForExit(120000)){ $p.Kill();throw 'Plot export timed out'}
 $result=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
 if($p.ExitCode -ne 0 -or !$result.success){throw $result.error}
 $result|Add-Member -NotePropertyName csv -NotePropertyValue $csv
 $result|Add-Member -NotePropertyName summary -NotePropertyValue $json
 $result|Add-Member -NotePropertyName session -NotePropertyValue $Session
 ConvertTo-Json -InputObject $result -Depth 8;exit 0
}catch{ConvertTo-Json -InputObject @{success=$false;error=$_.Exception.Message};exit 1}
