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
    # VSTest can put hundreds of MB of captured browser output in a TRX.
    # Read result attributes forward instead of loading that output as an XmlDocument.
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 0
    $stream = [System.IO.File]::OpenRead($report.FullName)
    $reader = [System.Xml.XmlReader]::Create($stream, $settings)
    $runId = $null
    $startedText = $null
    $finishedText = $null
    $counters = $null
    $results = [System.Collections.Generic.List[object]]::new()
    $definitions = [System.Collections.Generic.Dictionary[string,string]]::new()
    $entries = [System.Collections.Generic.HashSet[string]]::new()
    try {
        while ($reader.Read()) {
            if ($reader.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            switch ($reader.LocalName) {
                'TestRun' { $runId = $reader.GetAttribute('id') }
                'Times' {
                    $startedText = $reader.GetAttribute('start')
                    $finishedText = $reader.GetAttribute('finish')
                }
                'UnitTestResult' {
                    $results.Add([pscustomobject]@{
                        testId = $reader.GetAttribute('testId')
                        testName = $reader.GetAttribute('testName')
                        outcome = $reader.GetAttribute('outcome')
                    })
                }
                'UnitTest' {
                    $definitionId = $reader.GetAttribute('id')
                    $definitionName = $reader.GetAttribute('name')
                    if (!$definitionId -or !$definitionName -or !$definitions.TryAdd($definitionId, $definitionName)) {
                        throw 'Duplicate or unidentified discovered test definition.'
                    }
                }
                'TestEntry' {
                    $entryId = $reader.GetAttribute('testId')
                    if (!$entryId -or !$entries.Add($entryId)) { throw 'Duplicate or unidentified test entry.' }
                }
                'Counters' {
                    $counters = [pscustomobject]@{
                        total = [int]$reader.GetAttribute('total')
                        passed = [int]$reader.GetAttribute('passed')
                        notExecuted = [int]$reader.GetAttribute('notExecuted')
                    }
                }
            }
        }
    } finally {
        $reader.Dispose()
        $stream.Dispose()
    }
    if (!$runId -or !$runIds.Add($runId)) { throw 'Duplicate or unidentified test run.' }
    if (!$startedText -or !$finishedText) { throw 'Test report timestamps are missing.' }
    $started = [DateTimeOffset]::Parse($startedText)
    $finished = [DateTimeOffset]::Parse($finishedText)
    if ($started -lt $NotBeforeUtc -or $finished -lt $started -or $finished -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        throw 'Test report is stale or has invalid run timestamps.'
    }
    if (!$counters -or [int]$counters.total -le 0 -or [int]$counters.passed -ne [int]$counters.total -or [int]$counters.notExecuted -ne 0) {
        throw 'Test report contains failures, skipped tests or no executed cases.'
    }
    $total += [int]$counters.total
    if ($results.Count -ne [int]$counters.total) { throw 'Test counters do not match executed results.' }
    if ($definitions.Count -ne $results.Count -or $entries.Count -ne $results.Count) {
        throw 'Discovered test definitions or entries do not match executed results.'
    }
    foreach ($result in $results) {
        # xUnit may truncate two distinct theory argument displays to the same name.
        # The discovered testId is the unique case key; verify its exact name too.
        if ($result.outcome -ne 'Passed' -or !$result.testId -or !$result.testName -or
            !$testIds.Add([string]$result.testId)) {
            throw 'Duplicate, missing or unsuccessful test result identity or name.'
        }
        if (!$definitions.ContainsKey($result.testId) -or $definitions[$result.testId] -cne $result.testName -or
            !$entries.Contains($result.testId)) {
            throw 'Executed test does not match its discovered definition and entry.'
        }
    }
    $sqlTests += @($results | Where-Object { $_.testName -match 'RealSql|RealApiProcessRestart' -and $_.outcome -eq 'Passed' }).Count
}
if ($total -lt $MinimumTests -or $sqlTests -lt $MinimumSqlTests) { throw 'Required test or real-SQL coverage is missing.' }
Write-Output "Verified $total passing cases, including $sqlTests real-SQL scenarios; no skips."
