param(
    [string]$JourneyPath = '.local/phase10-gap18-refund/journey.json',
    [string]$EvidencePath = '.local/phase10-gap18-refund/sql-readback.json'
)
$ErrorActionPreference = 'Stop'
$journey = Get-Content -LiteralPath $JourneyPath -Raw | ConvertFrom-Json
if (!$journey.passed) { throw 'The authorized retained refund journey has not completed.' }
$refundId = [guid]$journey.refundId
$paymentId = [guid]$journey.paymentId
$connectionText = 'Server=.\SQL2022;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
$connection = [System.Data.SqlClient.SqlConnection]::new($connectionText)
$connection.Open()
try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 30
    $command.CommandText = @'
SELECT r.Id RefundId,r.AgencyId,r.PolicyId,r.CreditObligationId,r.Amount RefundAmount,
 r.State RefundState,r.RequestedBy,p.Id PaymentId,p.State PaymentState,p.WorkId,
 p.ProviderOperationId,p.OperationKey,p.RequestHash,w.State WorkState,
 o.State ProviderState,o.RequestHash ProviderRequestHash,o.OperationKey ProviderOperationKey,
 d.Id DecisionId,d.ActorId DecisionActor,
 (SELECT COUNT(*) FROM RefundDecision x WHERE x.RefundRequestId=r.Id AND x.Kind='approve') ApprovalCount,
 (SELECT COUNT(*) FROM FinanceRefundPayment x WHERE x.RefundRequestId=r.Id) PaymentCount,
 (SELECT COUNT(*) FROM DemoProviderOperation x WHERE x.OperationKey COLLATE Latin1_General_100_BIN2=p.OperationKey COLLATE Latin1_General_100_BIN2) OperationCount,
 (SELECT COUNT(*) FROM FinancePosting x WHERE x.SourceKind='refund' AND x.SourceId=p.Id) PostingCount,
 (SELECT SUM(x.CashDelta) FROM FinancePosting x WHERE x.SourceKind='refund' AND x.SourceId=p.Id) CashDelta,
 (SELECT TOP (1) x.Id FROM FinancePosting x WHERE x.SourceKind='refund' AND x.SourceId=p.Id) PostingId,
 (SELECT COUNT(*) FROM RefundCashReservation s JOIN Allocation a ON a.Id=s.AllocationId
  JOIN Receipt rec ON rec.Id=a.ReceiptId WHERE s.RefundRequestId=r.Id AND rec.AgencyId=r.AgencyId) SourceCount,
 (SELECT COUNT(*) FROM Journal j WHERE j.ObligationId=r.CreditObligationId AND j.PostedAt IS NOT NULL) PostedCreditCount
FROM RefundRequest r JOIN FinanceRefundPayment p ON p.RefundRequestId=r.Id
JOIN OutboxWork w ON w.Id=p.WorkId JOIN DemoProviderOperation o ON o.Id=p.ProviderOperationId
CROSS APPLY (SELECT TOP (1) x.Id,x.ActorId FROM RefundDecision x
 WHERE x.RefundRequestId=r.Id AND x.Kind='approve' ORDER BY x.DecidedAt,x.Id) d
WHERE r.Id=@refund AND p.Id=@payment
'@
    [void]$command.Parameters.Add('@refund',[System.Data.SqlDbType]::UniqueIdentifier)
    [void]$command.Parameters.Add('@payment',[System.Data.SqlDbType]::UniqueIdentifier)
    $command.Parameters['@refund'].Value = $refundId
    $command.Parameters['@payment'].Value = $paymentId
    $reader = $command.ExecuteReader()
    try {
        if (!$reader.Read()) { throw 'Exact saved refund/payment/operation chain is missing.' }
        $row = @{}
        for ($i=0; $i -lt $reader.FieldCount; $i++) { $row[$reader.GetName($i)] = $reader.GetValue($i) }
        if ($reader.Read()) { throw 'Expected one exact refund/payment chain.' }
    } finally { $reader.Dispose(); $command.Dispose() }
    if ($row.RefundState -ne 'approved' -or $row.PaymentState -ne 'paid' -or
        $row.WorkState -ne 'succeeded' -or $row.ProviderState -ne 'accepted' -or
        $row.ApprovalCount -ne 1 -or $row.PaymentCount -ne 1 -or $row.OperationCount -ne 1 -or
        $row.PostingCount -ne 1 -or $row.CashDelta -ne -[decimal]$row.RefundAmount -or
        $row.SourceCount -ne 1 -or $row.PostedCreditCount -ne 1 -or
        $row.DecisionActor -eq $row.RequestedBy -or
        $row.ProviderOperationId -ne [guid]$journey.payment.providerOperationId -or
        [Convert]::ToHexString([byte[]]$row.RequestHash) -cne [Convert]::ToHexString([byte[]]$row.ProviderRequestHash) -or
        $row.OperationKey -cne $row.ProviderOperationKey) {
        throw 'Refund/payment provider, cash posting, source, hash or independent approval evidence does not reconcile.'
    }
    $result = [ordered]@{
        passed = $true
        checkedAt = [DateTimeOffset]::UtcNow.ToString('o')
        refundId = $row.RefundId.ToString('D')
        agencyId = $row.AgencyId.ToString('D')
        policyId = $row.PolicyId.ToString('D')
        creditObligationId = $row.CreditObligationId.ToString('D')
        decisionId = $row.DecisionId.ToString('D')
        requesterId = $row.RequestedBy.ToString('D')
        decisionActorId = $row.DecisionActor.ToString('D')
        paymentId = $row.PaymentId.ToString('D')
        workId = $row.WorkId.ToString('D')
        providerOperationId = $row.ProviderOperationId.ToString('D')
        postingId = $row.PostingId.ToString('D')
        operationKey = $row.OperationKey
        requestHash = [Convert]::ToHexString([byte[]]$row.RequestHash)
        cashDelta = ([decimal]$row.CashDelta).ToString('0.00',[Globalization.CultureInfo]::InvariantCulture)
        refundAmount = ([decimal]$row.RefundAmount).ToString('0.00',[Globalization.CultureInfo]::InvariantCulture)
        approvalCount = $row.ApprovalCount
        providerOperationCount = $row.OperationCount
        postingCount = $row.PostingCount
    }
    $json = $result | ConvertTo-Json -Depth 5
    $json | Set-Content -LiteralPath $EvidencePath
    Write-Output $json
} finally { $connection.Dispose() }
