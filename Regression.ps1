param([string]$Root=$PSScriptRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Root)
. (Join-Path $root 'hero-build.ps1')
$id=[guid]::NewGuid().ToString('N')
$reportDir=Join-Path $root ('qualification\regression-'+$id)
New-Item -ItemType Directory -Force $reportDir|Out-Null
Copy-Item -LiteralPath (Join-Path $root 'tests') -Destination "$reportDir\fixtures" -Recurse
$fixture=Join-Path $reportDir 'fixtures'
$results=New-Object 'System.Collections.Generic.List[object]'
$prior=$env:HERO_NO_CACHE
function Check($name,$ok,$detail){
 $results.Add([pscustomobject]@{name=$name;passed=[bool]$ok;detail=$detail})
 Write-Output ("$name : "+$(if($ok){'PASS'}else{'FAIL'})+" | $detail")
 if(!$ok){throw "Regression failed: $name"}
}
function Trial($name,$source,$libs){
 $out=Join-Path $reportDir $name
 New-Item -ItemType Directory -Force $out|Out-Null
 Invoke-HeroBuild $source $libs $out
}
$success=$false;$failure=''
try{
 $env:HERO_NO_CACHE='0'
 $source="$fixture\AdvancedSketch";$libs="$fixture\libraries"
 $a=Trial cold $source $libs
 Check 'Advanced sketch and transitive libraries' ($a.success -and $a.sha256 -eq '8E249FF25CCED28F4A2B15E569D69B54DC51535FE046B92C44BE1DB9226A2275' -and @($a.used_libraries).Count -eq 2) $a.sha256
 $b=Trial warm $source $libs
 Check 'Warm object cache' ($b.success -and $b.sha256 -eq $a.sha256 -and $b.object_cache.hits -eq 3 -and $b.object_cache.misses -eq 0) ("hits="+$b.object_cache.hits)
 $p="$libs\HeroBeta\src\HeroBeta.cpp"
 $s=[IO.File]::ReadAllText($p);[IO.File]::WriteAllText($p,$s.Replace('return 1','return 2'))
 $c=Trial source-edit $source $libs
 Check 'Selective source invalidation' ($c.success -and $c.sha256 -ne $b.sha256 -and $c.object_cache.hits -eq 2 -and $c.object_cache.misses -eq 1) $c.sha256
 Add-Content -LiteralPath "$libs\HeroBeta\src\HeroBeta.h" -Encoding ASCII 'static const uint8_t heroBetaHeaderVersion=3;'
 $d=Trial header-edit $source $libs
 Check 'Transitive header invalidation' ($d.success -and $d.object_cache.hits -eq 1 -and $d.object_cache.misses -eq 2) $d.sha256
 $env:HERO_NO_CACHE='1'
 $e=Trial uncached $source $libs
 Check 'Fresh vs cached HEX' ($e.success -and $e.sha256 -eq $d.sha256 -and $e.object_cache.bypassed -eq 3) $e.sha256
 $env:HERO_NO_CACHE='0'
 Copy-Item -LiteralPath "$fixture\DuplicateAlpha-negative" -Destination "$libs\DuplicateAlpha" -Recurse
 $f=Trial ambiguous $source $libs
 Check 'Ambiguous header rejection' (!$f.success -and (Get-Content -LiteralPath $f.compiler_log -Raw) -match 'Ambiguous header') 'Compiler rejected duplicate candidates'
 $g=Trial missing "$fixture\MissingHeader" $libs
 Check 'Missing header rejection' (!$g.success -and (Get-Content -LiteralPath $g.compiler_log -Raw) -match 'Unresolved active include') 'Compiler rejected unresolved dependency'
 $success=$true
}catch{$failure=$_.Exception.Message;Write-Output $failure}
finally{
 $env:HERO_NO_CACHE=$prior
 @{success=$success;error=$failure;date=[DateTimeOffset]::Now.ToString('o');results=@($results.ToArray())}|ConvertTo-Json -Depth 8|Set-Content -Encoding UTF8 "$reportDir\regression.json"
 Write-Output "REPORT: $reportDir\regression.json"
}
if(!$success){exit 1}
