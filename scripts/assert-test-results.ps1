param(
    [Parameter(Mandatory=$true)][string]$ResultsDirectory,
    [Parameter(Mandatory=$true)][int]$MinimumTests,
    [Parameter(Mandatory=$true)][int]$MinimumSqlTests
)
$ErrorActionPreference = 'Stop'
$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
if ($reports.Count -lt 2) { throw 'Expected unit and integration TRX reports.' }
$total = 0
$sqlTests = 0
foreach ($report in $reports) {
    [xml]$xml = Get-Content -LiteralPath $report.FullName -Raw
    $counters = $xml.TestRun.ResultSummary.Counters
    if (!$counters -or [int]$counters.total -le 0 -or [int]$counters.passed -ne [int]$counters.total -or [int]$counters.notExecuted -ne 0) {
        throw 'Test report contains failures, skipped tests or no executed cases.'
    }
    $total += [int]$counters.total
    $sqlTests += @($xml.TestRun.Results.UnitTestResult | Where-Object { $_.testName -match 'RealSql|RealApiProcessRestart' -and $_.outcome -eq 'Passed' }).Count
}
if ($total -lt $MinimumTests -or $sqlTests -lt $MinimumSqlTests) { throw 'Required test or real-SQL coverage is missing.' }
Write-Output "Verified $total passing cases, including $sqlTests real-SQL scenarios; no skips."
