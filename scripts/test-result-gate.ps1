$ErrorActionPreference = 'Stop'
$root = Join-Path (Get-Location) ('.local/trx-gate-cases-' + [guid]::NewGuid().ToString('N'))
$gate = Join-Path $PSScriptRoot 'assert-test-results.ps1'
function Write-Report([string]$directory,[string]$name,[string]$testName,[int]$passed,[int]$skipped,[string]$outcome) {
    $identity = [guid]::NewGuid().ToString('D')
    $now = [DateTimeOffset]::UtcNow.ToString('o')
    $xml = '<TestRun id="' + $identity + '"><Times start="' + $now + '" finish="' + $now + '"/><Results><UnitTestResult testId="' + $identity + '" testName="' + $testName + '" outcome="' + $outcome + '" /></Results><ResultSummary><Counters total="1" passed="' + $passed + '" notExecuted="' + $skipped + '" /></ResultSummary></TestRun>'
    [IO.File]::WriteAllText((Join-Path $directory $name),$xml)
}
foreach ($case in @('pass','skip','failure','missing-sql','missing-report','undercount')) {
    $directory = Join-Path $root $case
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    Write-Report $directory 'unit.trx' 'UnitExample' 1 0 'Passed'
    if ($case -ne 'missing-report') {
        $sqlName = if ($case -eq 'missing-sql') {'UnitOnly'} else {'RealSqlExample'}
        $passed = if ($case -in @('skip','failure')) {0} else {1}
        $skipped = if ($case -eq 'skip') {1} else {0}
        $outcome = if ($passed -eq 1) {'Passed'} else {'NotExecuted'}
        Write-Report $directory 'integration.trx' $sqlName $passed $skipped $outcome
    }
    $minimum = if ($case -eq 'undercount') {3} else {2}
    $rejected = $false
    try { & $gate -ResultsDirectory $directory -MinimumTests $minimum -MinimumSqlTests 1 | Out-Null }
    catch { $rejected = $true }
    if (($case -eq 'pass' -and $rejected) -or ($case -ne 'pass' -and !$rejected)) { throw "Result gate behaved incorrectly for $case." }
}
Write-Output 'Result gate: valid report accepted; skipped, failed, missing SQL/report and undercount rejected.'
