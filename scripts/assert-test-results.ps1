param(
    [Parameter(Mandatory=$true)][string]$ResultsDirectory,
    [Parameter(Mandatory=$true)][int]$MinimumTests,
    [Parameter(Mandatory=$true)][int]$MinimumSqlTests,
    [DateTimeOffset]$NotBeforeUtc = [DateTimeOffset]::UtcNow.AddHours(-24)
)
$ErrorActionPreference = 'Stop'
$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
if ($reports.Count -lt 2) { throw 'Expected unit and integration TRX reports.' }
$total = 0
$sqlTests = 0
$runIds = [System.Collections.Generic.HashSet[string]]::new()
$testIds = [System.Collections.Generic.HashSet[string]]::new()
foreach ($report in $reports) {
    [xml]$xml = Get-Content -LiteralPath $report.FullName -Raw
    $runId = [string]$xml.TestRun.id
    if (!$runId -or !$runIds.Add($runId)) { throw 'Duplicate or unidentified test run.' }
    $started = [DateTimeOffset]::Parse([string]$xml.TestRun.Times.start)
    $finished = [DateTimeOffset]::Parse([string]$xml.TestRun.Times.finish)
    if ($started -lt $NotBeforeUtc -or $finished -lt $started -or $finished -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        throw 'Test report is stale or has invalid run timestamps.'
    }
    $counters = $xml.TestRun.ResultSummary.Counters
    if (!$counters -or [int]$counters.total -le 0 -or [int]$counters.passed -ne [int]$counters.total -or [int]$counters.notExecuted -ne 0) {
        throw 'Test report contains failures, skipped tests or no executed cases.'
    }
    $total += [int]$counters.total
    $results = @($xml.TestRun.Results.UnitTestResult)
    if ($results.Count -ne [int]$counters.total) { throw 'Test counters do not match executed results.' }
    foreach ($result in $results) {
        if ($result.outcome -ne 'Passed' -or !$result.testId -or !$testIds.Add([string]$result.testId)) {
            throw 'Duplicate, missing or unsuccessful test result.'
        }
    }
    $sqlTests += @($xml.TestRun.Results.UnitTestResult | Where-Object { $_.testName -match 'RealSql|RealApiProcessRestart' -and $_.outcome -eq 'Passed' }).Count
}
if ($total -lt $MinimumTests -or $sqlTests -lt $MinimumSqlTests) { throw 'Required test or real-SQL coverage is missing.' }
Write-Output "Verified $total passing cases, including $sqlTests real-SQL scenarios; no skips."
