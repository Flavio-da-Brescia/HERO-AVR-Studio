# HERO AVR build engine. Local toolchain only.
. (Join-Path $PSScriptRoot 'hero-preprocess.ps1')
function Invoke-HeroBuild([string]$Sketch,[string]$Libraries,[string]$Output){
 $root=$PSScriptRoot
 $gcc=Join-Path $root 'toolchain\avr-gcc\bin'
 $avr=Join-Path $root 'compatibility\arduino-avr'
 $core=Join-Path $avr 'cores\arduino';$variant=Join-Path $avr 'variants\eightanaloginputs'
 $work=Join-Path $Output 'work'
 New-Item -ItemType Directory -Force $work|Out-Null
 $messages=New-Object 'System.Collections.Generic.List[string]'
 $used=New-Object 'System.Collections.Generic.List[object]'
 $objectStats=[ordered]@{hits=0;misses=0;bypassed=0};$objectDetails=New-Object 'System.Collections.Generic.List[object]';$buildingCore=$false;$cacheHit=$false;$cacheKey='';$clock=[Diagnostics.Stopwatch]::StartNew();$sections=@();$hex='';$success=$false;$nl=[Environment]::NewLine
 function RunTool([string]$tool,[string[]]$args2){
  $ErrorActionPreference='Continue'
  $res=@(& (Join-Path $gcc "$tool.exe") @args2 2>&1)
  $rc=$LASTEXITCODE
  foreach($line in $res){$messages.Add([string]$line)}
  if($rc -ne 0){throw "$tool failed (exit $rc)"}
  $res|ForEach-Object {[string]$_}
 }
 function Sources([string]$folder,[bool]$recursive){
  Get-ChildItem -LiteralPath $folder -File -Recurse:$recursive | Where-Object {$_.Extension -cin '.c','.cpp','.cc','.cxx','.S'} | Sort-Object FullName
 }
 try{
  $item=Get-Item -LiteralPath $Sketch
  $dir=if($item.PSIsContainer){$item.FullName}else{$item.DirectoryName}
  $name=if($item.PSIsContainer){$item.Name}else{$item.BaseName}
  $inos=@(Get-ChildItem -LiteralPath $dir -File -Filter '*.ino'|Sort-Object Name)
  $compat=($inos.Count -gt 0 -and ($item.PSIsContainer -or $item.Extension -eq '.ino'))
  $units=@(Sources $dir $false)
  if(Test-Path "$dir\src"){$units+=@(Sources "$dir\src" $true)}
  $include=New-Object 'System.Collections.Generic.List[string]'
  $include.Add($dir)
  if(Test-Path "$dir\src"){$include.Add("$dir\src")}
  if($compat){
   $include.Add($core);$include.Add($variant)
   $primary=@($inos|Where-Object {$_.BaseName -eq (Split-Path $dir -Leaf)})
   if(!$primary.Count){throw 'Primary .ino filename must match sketch directory'}
   $ordered=@($primary)+@($inos|Where-Object {$_.FullName -ne $primary[0].FullName})
   $generated=Join-Path $work "$name.ino.cpp"
   Convert-HeroSketch @($ordered|ForEach-Object {$_.FullName}) $generated
   $units+=Get-Item $generated
  } elseif(!$units.Count){throw 'No native .c/.cpp/.S source files found'}
  $candidates=@();$tier=0
  if($compat){
   foreach($base in @($Libraries,(Join-Path $avr 'libraries'),(Join-Path $root 'compatibility\extra-libraries'))){
    if($base -and (Test-Path $base)){
     foreach($lib in Get-ChildItem -LiteralPath $base -Directory|Sort-Object Name){
      $ls=if(Test-Path "$($lib.FullName)\src"){"$($lib.FullName)\src"}else{$lib.FullName}
      $props=@{}
      if(Test-Path "$($lib.FullName)\library.properties"){
       foreach($line in Get-Content -LiteralPath "$($lib.FullName)\library.properties"){if($line -match '^([^#=]+)=(.*)$'){$props[$matches[1].Trim()]=$matches[2].Trim()}}
      }
      $arch=if($props.architectures){@($props.architectures -split ','|ForEach-Object {$_.Trim()})}else{@('*')}
      $candidates+= [pscustomobject]@{name=$lib.Name;path=$lib.FullName;src=$ls;modern=($ls -ne $lib.FullName);tier=$tier;props=$props;avr=('*' -in $arch -or 'avr' -in $arch)}
     }
    }
    $tier++
   }
  }
  $baseFlags=@('-mmcu=atmega328p','-DF_CPU=8000000UL','-Os','-g','-ffunction-sections','-fdata-sections','-flto','-Wall','-Wextra')
  if($compat){$baseFlags+=@('-DARDUINO=10819','-DARDUINO_AVR_PRO','-DARDUINO_ARCH_AVR')}
  function LanguageArgs($unit){
   if($unit.Extension -cin '.cpp','.cc','.cxx'){@('-std=gnu++11','-fpermissive','-fno-exceptions','-fno-threadsafe-statics','-Wno-error=narrowing')}
   elseif($unit.Extension -ceq '.S'){@('-x','assembler-with-cpp')}
   else{@('-std=gnu11','-fno-fat-lto-objects')}
  }
  $chosen=@{};$pass=0
  # GCC preprocesses actual active includes, including macros and conditionals.
  # Missing headers are returned by -MG. Resolve local libraries and repeat until closure.
  while($true){
   $pass++;if($pass -gt 64){throw 'Library dependency resolution exceeded 64 passes'}
   $flags=@($baseFlags);foreach($i in $include){$flags+=('-I'+$i)}
   $missing=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
   foreach($u in $units){
    $tool=if($u.Extension -cin '.cpp','.cc','.cxx'){'avr-g++'}else{'avr-gcc'}
    $dep=Join-Path $work 'hero-deps.d'
    $null=RunTool $tool (@($flags)+@(LanguageArgs $u)+@('-M','-MG','-MT','HERO_DEPS','-MF',$dep,$u.FullName))
    $raw=[IO.File]::ReadAllText($dep)
    $raw=[regex]::Replace($raw,'\\\r?\n',' ')
    $raw=$raw -replace '^\s*HERO_DEPS:\s*',''
    foreach($token in [regex]::Matches($raw,'(?:\\[ #]|[^\s])+')){
     $h=$token.Value.Replace('\ ',' ').Replace('\#','#').Replace('$$','$')
     if([IO.Path]::IsPathRooted($h)){if(!(Test-Path -LiteralPath $h -PathType Leaf)){throw "Dependency path missing: $h"};continue}
     # GCC emits unresolved headers as relative names. Existing relative paths can arise from local include paths.
     if(Test-Path -LiteralPath (Join-Path $dir $h) -PathType Leaf){continue}
     $null=$missing.Add($h)
    }
   }
   if(!$missing.Count){break}
   $added=$false;$unresolved=@()
   foreach($header in @($missing|Sort-Object)){
    if([IO.Path]::IsPathRooted($header) -or $header -match '(^|[/\\])\.\.([/\\]|$)'){throw "Invalid unresolved include: $header"}
    $hits=@($candidates|Where-Object {$_.avr -and (Test-Path -LiteralPath (Join-Path $_.src $header) -PathType Leaf)})
    if(!$hits.Count){$unresolved+=$header;continue}
    $best=($hits|Measure-Object tier -Minimum).Minimum
    $hits=@($hits|Where-Object tier -eq $best)
    if($hits.Count -gt 1){throw ("Ambiguous header "+$header+": "+(($hits|ForEach-Object {$_.name}) -join ', ')+". Remove the duplicate or use a unique header.")}
    $lib=$hits[0]
    if(!$chosen.ContainsKey($lib.path)){
     $chosen[$lib.path]=$true;$include.Add($lib.src);$added=$true
     $used.Add([pscustomobject]@{name=$(if($lib.props.name){$lib.props.name}else{$lib.name});version=$(if($lib.props.version){$lib.props.version}else{'local'});location='local';install_dir=$lib.path})
     $libUnits=@(Sources $lib.src $lib.modern)
     if(!$lib.modern -and (Test-Path "$($lib.src)\utility")){$include.Add("$($lib.src)\utility");$libUnits+=@(Sources "$($lib.src)\utility" $true)}
     $units+=$libUnits
    }else{$unresolved+=$header}
   }
   if(!$added){throw ('Unresolved active include(s): '+($unresolved -join ', '))}
  }
  function HashText([string]$v){
   $sha=[Security.Cryptography.SHA256]::Create()
   try{([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($v)))).Replace('-','')}finally{$sha.Dispose()}
  }
  # Hash compiler installation and engine once per build; shared by object/core caches.
  $fingerprints=New-Object 'System.Collections.Generic.List[string]'
  $hasher=[Security.Cryptography.SHA256]::Create()
  try{
   foreach($tree in @((Split-Path $gcc -Parent),$core,$variant)){
    foreach($f in Get-ChildItem -LiteralPath $tree -File -Recurse -Force|Sort-Object FullName){
     $stream=[IO.File]::OpenRead($f.FullName)
     try{$h=([BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-','')}finally{$stream.Dispose()}
     $fingerprints.Add($f.FullName.Substring($tree.Length)+':'+$h)
    }
   }
  }finally{$hasher.Dispose()}
  $fingerprints.Add((Get-FileHash -LiteralPath (Join-Path $root 'hero-build.ps1')).Hash)
  $fingerprints.Add((Get-FileHash -LiteralPath (Join-Path $root 'hero-preprocess.ps1')).Hash)
  $toolFingerprint=HashText ($fingerprints -join $nl)
  # Stable debug line paths for managed source/library snapshots and generated sketch files.
  # Only line markers change; expanded __FILE__, __DATE__, __TIME__ strings remain real inputs.
  function CanonicalPreprocessed([string]$text){
   $map=@(@{from=$work;to='/hero/generated'})
   if($dir -match '[\\/]projects[\\/][^\\/]+[\\/](?:builds|releases)[\\/][^\\/]+[\\/]src[\\/]'){
    $map+=@{from=$dir;to=('/hero/project/'+(Split-Path $dir -Leaf))}
    if($Libraries){$map+=@{from=$Libraries;to='/hero/project-libraries'}}
   }
   return [regex]::Replace($text,'(?m)^(\s*#\s+\d+\s+")((?:\\.|[^"\\])*)(".*)$',{
    param($m)
    $path=$m.Groups[2].Value.Replace('\\','\').Replace('\','/')
    foreach($pair in $map){
     $prefix=$pair.from.Replace('\','/').TrimEnd('/')
     if($path.StartsWith($prefix+'/',[StringComparison]::OrdinalIgnoreCase)){
      $path=$pair.to+$path.Substring($prefix.Length)
      return $m.Groups[1].Value+$path+$m.Groups[3].Value
     }
    }
    return $m.Value
   })
  }
  function CompileUnit($unit){
   $script:heroObjectCounter++
   $obj=Join-Path $work ('obj'+$script:heroObjectCounter+'.o')
   $tool=if($unit.Extension -cin '.cpp','.cc','.cxx'){'avr-g++'}else{'avr-gcc'}
   $lang=@(LanguageArgs $unit)
   $args3=@($flags)+$lang+@('-c')
   $state='bypassed'
   if($buildingCore -or $env:HERO_NO_CACHE -eq '1' -or $unit.Extension -ceq '.S'){
    $null=RunTool $tool ($args3+@($unit.FullName,'-o',$obj))
    if(!$buildingCore){$objectStats.bypassed++}
   }else{
    $pre=Join-Path $work ('unit'+$script:heroObjectCounter+'.i')
    $before=$messages.Count
    $null=RunTool $tool (@($flags)+$lang+@('-E',$unit.FullName,'-o',$pre))
    $canonical=CanonicalPreprocessed ([IO.File]::ReadAllText($pre))
    [IO.File]::WriteAllText($pre,$canonical,(New-Object Text.UTF8Encoding($false)))
    $preHash=(Get-FileHash -LiteralPath $pre).Hash
    # Preprocessed bytes capture macros, selected headers and their content. Include search flags are no longer needed.
    $compileFlags=@($baseFlags)+$lang
    $key=HashText (@('hero-object-v1',$toolFingerprint,$tool,($compileFlags -join '|'),$preHash) -join $nl)
    $cacheRoot=Join-Path $root 'build\hero-object-cache'
    $entry=Join-Path $cacheRoot $key
    $hit=$false
    try{
     $meta=Get-Content -LiteralPath "$entry\meta.json" -Raw -ErrorAction Stop|ConvertFrom-Json
     if($meta.key -eq $key -and $meta.sha256 -match '^[0-9A-F]{64}$' -and (Get-FileHash -LiteralPath "$entry\unit.o" -ErrorAction Stop).Hash -eq $meta.sha256){
      Copy-Item -LiteralPath "$entry\unit.o" -Destination $obj -Force
      if((Get-FileHash -LiteralPath $obj).Hash -eq $meta.sha256){$hit=$true;foreach($line in $meta.warnings){$messages.Add([string]$line)}}
     }
    }catch{}
    if($hit){$objectStats.hits++;$state='hit'}
    else{
     $objectStats.misses++;$state='miss'
     $warnStart=$messages.Count
     $kind=if($tool -eq 'avr-g++'){'c++-cpp-output'}else{'cpp-output'}
     $null=RunTool $tool ($compileFlags+@('-x',$kind,'-c',$pre,'-o',$obj))
     $warnings=@();for($i=$warnStart;$i -lt $messages.Count;$i++){$warnings+=$messages[$i]}
     New-Item -ItemType Directory -Force $cacheRoot|Out-Null
     $stage=Join-Path $cacheRoot ('.stage-'+[guid]::NewGuid().ToString('N'))
     New-Item -ItemType Directory $stage|Out-Null
     try{
      Copy-Item -LiteralPath $obj -Destination "$stage\unit.o"
      $meta=@{key=$key;sha256=(Get-FileHash -LiteralPath $obj).Hash;warnings=$warnings}
      [IO.File]::WriteAllText("$stage\meta.json",($meta|ConvertTo-Json -Depth 4),(New-Object Text.UTF8Encoding($false)))
      try{[IO.Directory]::Move($stage,$entry)}catch{}
     }finally{if(Test-Path $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
    }
   }
   if(!$buildingCore){$objectDetails.Add([pscustomobject]@{source=$unit.FullName;cache=$state})}
   return $obj
  }
  $script:heroObjectCounter=0
  $objects=@(foreach($unit in $units){CompileUnit $unit})
  if($compat){
   # Only cache the immutable platform core; sketch and library objects always rebuild.
   $savedFlags=$flags
   $flags=@('-mmcu=atmega328p','-DF_CPU=8000000UL','-Os','-g','-ffunction-sections','-fdata-sections','-flto','-Wall','-Wextra','-DARDUINO=10819','-DARDUINO_AVR_PRO','-DARDUINO_ARCH_AVR',('-I'+$core),('-I'+$variant))
   $archive=Join-Path $work 'core.a'
   $cacheRoot=Join-Path $root 'build\hero-core-cache'
   $cacheKey=HashText (@('hero-core-v2',$toolFingerprint,($flags -join '|')) -join $nl)
   $entry=Join-Path $cacheRoot $cacheKey
   if($env:HERO_NO_CACHE -ne '1'){
    try{
     $expected=(Get-Content -LiteralPath "$entry\sha256.txt" -Raw -ErrorAction Stop).Trim()
     if($expected -match '^[0-9A-F]{64}$' -and (Get-FileHash -LiteralPath "$entry\core.a" -ErrorAction Stop).Hash -eq $expected){
      Copy-Item -LiteralPath "$entry\core.a" -Destination $archive
      if((Get-FileHash -LiteralPath $archive).Hash -eq $expected){$cacheHit=$true}
     }
    }catch{}
   }
   if(!$cacheHit){
    if(Test-Path $archive){Remove-Item -LiteralPath $archive}
    $buildingCore=$true;try{$coreObjects=@(foreach($unit in (Sources $core $false)){CompileUnit $unit})}finally{$buildingCore=$false}
    $null=RunTool 'avr-gcc-ar' (@('rcs',$archive)+$coreObjects)
    if($env:HERO_NO_CACHE -ne '1'){
     New-Item -ItemType Directory -Force $cacheRoot|Out-Null
     $stage=Join-Path $cacheRoot ('.stage-'+[guid]::NewGuid().ToString('N'))
     New-Item -ItemType Directory $stage|Out-Null
     try{
      Copy-Item -LiteralPath $archive -Destination "$stage\core.a"
      (Get-FileHash -LiteralPath $archive).Hash | Set-Content -Encoding ASCII "$stage\sha256.txt"
      try{[IO.Directory]::Move($stage,$entry)}catch{# Another build may have published this key; never use unverified data.
      }
     }finally{if(Test-Path $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
    }
   }
   $flags=$savedFlags
   $objects+=$archive
  }
  $elf=Join-Path $Output "$name.ino.elf"
  $null=RunTool 'avr-gcc' (@('-mmcu=atmega328p','-Os','-g','-flto','-fuse-linker-plugin','-Wl,--gc-sections','-o',$elf)+$objects+@('-lm'))
  $sz=@(RunTool 'avr-size' @($elf))
  $row=@($sz|Where-Object {$_ -match '^\s*\d+\s+\d+\s+\d+\s+'})|Select-Object -First 1
  if($row -notmatch '^\s*(\d+)\s+(\d+)\s+(\d+)'){throw 'Cannot determine memory size'}
  $flash=[int]$matches[1]+[int]$matches[2];$ram=[int]$matches[2]+[int]$matches[3]
  $sections=@(@{name='text';size=$flash;max_size=30720},@{name='data';size=$ram;max_size=2048})
  if($flash -gt 30720 -or $ram -gt 2048){throw "Memory limit exceeded: flash $flash / SRAM $ram"}
  $hex=Join-Path $Output "$name.ino.hex"
  $null=RunTool 'avr-objcopy' @('-O','ihex','-R','.eeprom',$elf,$hex)
  $success=$true
 }catch{$messages.Add($_.Exception.Message)}
 $clock.Stop();$elapsed=[Math]::Round($clock.Elapsed.TotalSeconds,3);$log=Join-Path $Output 'compiler.log'
 $logObject=@{success=$success;compiler_out="HERO Build Engine | AVR-GCC | ATmega328P 8 MHz | Core cache hit: $cacheHit | Objects: $($objectStats.hits) reused, $($objectStats.misses) rebuilt, $($objectStats.bypassed) bypassed | $elapsed s";core_cache_hit=$cacheHit;object_cache=$objectStats;object_details=@($objectDetails.ToArray());duration_s=$elapsed;compiler_err=($messages -join $nl);builder_result=@{used_libraries=@($used.ToArray());executable_sections_size=$sections}}
 [IO.File]::WriteAllText($log,($logObject|ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
 [IO.File]::WriteAllText((Join-Path $Output 'stderr.log'),($messages -join $nl),(New-Object Text.UTF8Encoding($false)))
 [pscustomobject]@{success=$success;exit_code=$(if($success){0}else{1});hex_path=$(if($success){$hex}else{''});sha256=$(if($success){(Get-FileHash -LiteralPath $hex).Hash}else{''});compiler_log=$log;used_libraries=@($used.ToArray());memory=$sections;core_cache_hit=$cacheHit;core_cache_key=$cacheKey;object_cache=$objectStats;object_details=@($objectDetails.ToArray());duration_s=$elapsed}
}
