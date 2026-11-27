# TEST nn: description, comments, RESULT nn: PASS|FAIL, DIAGNOSTIC COMPLETED
function New-TestState {
 [pscustomobject]@{tests=(New-Object 'System.Collections.Generic.List[object]');current=$null;errors=(New-Object 'System.Collections.Generic.List[string]');complete=$false}
}
function Add-TestLine($state,[string]$line){
 $line=$line.Trim()
 if(!$line){return}
 if($line -ceq 'DIAGNOSTIC COMPLETED'){
  if($state.complete -or $null -ne $state.current -or !$state.tests.Count){$state.errors.Add('Unexpected or premature completion marker');return}
  $state.complete=$true;return
 }
 if($line -cmatch '^TEST ([0-9]{2}):\s*(\S.*)$'){
  $num=[int]$matches[1];$desc=$matches[2]
  if($null -ne $state.current -or $state.complete -or $num -ne ($state.tests.Count+1)){$state.errors.Add("Unexpected test header: $line");return}
  $test=[pscustomobject]@{id=$num.ToString('D2');description=$desc;comments=(New-Object 'System.Collections.Generic.List[string]');result='PENDING'}
  $state.tests.Add($test);$state.current=$test;return
 }
 if($line -cmatch '^RESULT ([0-9]{2}):\s*(PASS|FAIL)$'){
  $id=$matches[1];$result=$matches[2]
  if($state.complete -or $null -eq $state.current -or $state.current.id -cne $id){$state.errors.Add("Unexpected result: $line");return}
  $state.current.result=$result;$state.current=$null;return
 }
 if($line -match '^(TEST|RESULT)(\s|:|$)|^DIAGNOSTIC COMPLETED'){$state.errors.Add("Malformed protocol line: $line");return}
 if($null -ne $state.current){if($state.current.comments.Count -lt 500){$state.current.comments.Add($line)}else{$state.errors.Add('Too many comment lines')}}
}
function Test-Summary($state){
 $tests=@($state.tests.ToArray())
 $passed=@($tests|Where-Object result -eq 'PASS').Count
 $failed=@($tests|Where-Object result -eq 'FAIL').Count
 [pscustomobject]@{success=($state.complete -and $tests.Count -gt 0 -and $passed -eq $tests.Count -and $state.errors.Count -eq 0);total=$tests.Count;passed=$passed;failed=$failed;complete=$state.complete;errors=@($state.errors.ToArray());tests=$tests}
}
