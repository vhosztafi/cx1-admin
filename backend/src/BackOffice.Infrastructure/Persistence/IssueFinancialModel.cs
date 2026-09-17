using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureIssueFinancials(ModelBuilder model)
    {
        var obligation = Record<IssueFinancialObligation>(model, "IssueFinancialObligation"); obligation.ToTable(t => t.UseSqlOutputClause(false));
        Text(obligation, ("Purpose", 30), ("DebtorKind", 20), ("Currency", 3), ("Settlement", 30)); UnderwritingJson(obligation, "TermsSnapshotJson");
        obligation.HasAlternateKey(x => new { x.Id, x.TransactionId }); obligation.HasIndex(x => new { x.TransactionId, x.Purpose }).IsUnique();
        obligation.HasOne<PolicyTransaction>().WithMany().HasForeignKey(x => new { x.TransactionId, x.TermId, x.PolicyId }).HasPrincipalKey(x => new { x.Id, x.TermId, x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        obligation.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x => new { x.RelationshipId, x.ClientId, x.AgencyId }).HasPrincipalKey(x => new { x.Id, x.ClientId, x.AgencyId }).OnDelete(DeleteBehavior.NoAction);
        obligation.HasOne<AgencyTermsVersion>().WithMany().HasForeignKey(x => new { x.AgencyTermsVersionId, x.AgencyId }).HasPrincipalKey(x => new { x.Id, x.AgencyId }).OnDelete(DeleteBehavior.NoAction);
        obligation.HasOne<CapacityProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.NoAction);
        obligation.HasOne<Agency>().WithMany().HasForeignKey(x => x.DebtorAgencyId).OnDelete(DeleteBehavior.NoAction);
        obligation.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x => x.DebtorRelationshipId).OnDelete(DeleteBehavior.NoAction);
        foreach (var property in new[] { "Premium", "Tax", "Fee", "Commission", "FeeShare", "GrossDue", "InvoiceDue", "NetDue", "BrokerPayable" })
        { obligation.Property<decimal>(property).HasPrecision(15, 2); Check(obligation, property, $"[{property}]>=0"); }
        Check(obligation, "Purpose", "[Purpose]='first-issue'"); Check(obligation, "Currency", "[Currency]='GBP'");
        Check(obligation, "Debtor", "([DebtorKind]='agency' AND [DebtorAgencyId] IS NOT NULL AND [DebtorAgencyId]=[AgencyId] AND [DebtorRelationshipId] IS NULL) OR ([DebtorKind]='relationship' AND [DebtorRelationshipId] IS NOT NULL AND [DebtorRelationshipId]=[RelationshipId] AND [DebtorAgencyId] IS NULL)");
        Check(obligation, "Amounts", "[Commission]<=[Premium] AND [FeeShare]<=[Fee] AND [GrossDue]=[Premium]+[Tax]+[Fee] AND [NetDue]=[GrossDue]-[Commission]-[FeeShare]");
        Check(obligation, "Settlement", "([Settlement]='net-remittance' AND [DebtorKind]='agency' AND [InvoiceDue]=[NetDue] AND [BrokerPayable]=0) OR ([Settlement]='separate-payment' AND [InvoiceDue]=[GrossDue] AND [BrokerPayable]=[Commission]+[FeeShare])");

        var component = Record<IssueFinancialComponent>(model, "IssueFinancialComponent"); component.ToTable(t => t.UseSqlOutputClause(false)); Text(component, ("Code", 30));
        component.Property(x => x.Amount).HasPrecision(15, 2); component.HasIndex(x => new { x.ObligationId, x.Code }).IsUnique();
        component.HasAlternateKey(x => new { x.Id, x.TransactionId, x.Code });
        component.HasOne<IssueFinancialObligation>().WithMany().HasForeignKey(x => new { x.ObligationId, x.TransactionId }).HasPrincipalKey(x => new { x.Id, x.TransactionId }).OnDelete(DeleteBehavior.NoAction);
        Check(component, "Code", "[Code] IN ('premium','tax','fee','commission','fee-share')"); Check(component, "Amount", "[Amount]>=0"); Check(component, "Interval", "[CoverageStartsAt]<[CoverageEndsAt]");

        var journal = Record<Journal>(model, "Journal"); journal.ToTable(t => t.UseSqlOutputClause(false)); Text(journal, ("Purpose", 30), ("Currency", 3));
        journal.HasAlternateKey(x => new { x.Id, x.TransactionId }); journal.HasIndex(x => new { x.TransactionId, x.Purpose }).IsUnique();
        journal.HasOne<IssueFinancialObligation>().WithMany().HasForeignKey(x => new { x.ObligationId, x.TransactionId }).HasPrincipalKey(x => new { x.Id, x.TransactionId }).OnDelete(DeleteBehavior.NoAction);
        Check(journal, "Purpose", "[Purpose]='first-issue'"); Check(journal, "Currency", "[Currency]='GBP'"); Check(journal, "PostedAt", "[PostedAt] IS NULL OR [PostedAt]>=[CreatedAt]");

        var line = Record<JournalLine>(model, "JournalLine"); line.ToTable(t => t.UseSqlOutputClause(false)); Text(line, ("ComponentCode", 30), ("AccountCode", 40), ("PartyKind", 20));
        line.Property(x => x.Debit).HasPrecision(15, 2); line.Property(x => x.Credit).HasPrecision(15, 2);
        line.HasIndex(x => new { x.JournalId, x.SourceComponentId, x.AccountCode }).IsUnique();
        line.HasOne<Journal>().WithMany().HasForeignKey(x => new { x.JournalId, x.TransactionId }).HasPrincipalKey(x => new { x.Id, x.TransactionId }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<IssueFinancialComponent>().WithMany().HasForeignKey(x => new { x.SourceComponentId, x.TransactionId, x.ComponentCode }).HasPrincipalKey(x => new { x.Id, x.TransactionId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        Check(line, "Amount", "([Debit]>0 AND [Credit]=0) OR ([Credit]>0 AND [Debit]=0)");
        Check(line, "Account", "[AccountCode] IN ('agency-receivable','relationship-receivable','insurer-payable','fee-income','broker-remuneration-payable')");
        Check(line, "Party", "([AccountCode]='fee-income' AND [PartyKind]='internal' AND [PartyId] IS NULL) OR ([AccountCode] IN ('agency-receivable','broker-remuneration-payable') AND [PartyKind]='agency' AND [PartyId] IS NOT NULL) OR ([AccountCode]='relationship-receivable' AND [PartyKind]='relationship' AND [PartyId] IS NOT NULL) OR ([AccountCode]='insurer-payable' AND [PartyKind]='provider' AND [PartyId] IS NOT NULL)");
        Check(line, "Interval", "[CoverageStartsAt]<[CoverageEndsAt]");
    }
}
