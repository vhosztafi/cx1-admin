using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class DocumentService
{
    // Called only after the containing documents have been authorized. Batch by
    // exact retained identities so a list does not make one request per row.
    private static async Task AddSourceMetadata(BackOfficeDbContext db,IReadOnlyList<(DocumentVersion Version,Dictionary<string,object?> View)> rows,CancellationToken token)
    {
        if(rows.Count==0)return;
        var policyIds=rows.Where(x=>x.Version.PolicyVersionId!=null).Select(x=>x.Version.PolicyVersionId!.Value).Distinct().ToArray();
        var quoteIds=rows.Where(x=>x.Version.QuoteRevisionId!=null).Select(x=>x.Version.QuoteRevisionId!.Value).Distinct().ToArray();
        var termsIds=rows.Where(x=>x.Version.ServicingTermsVersionId!=null).Select(x=>x.Version.ServicingTermsVersionId!.Value).Distinct().ToArray();
        var sources=new Dictionary<Guid,(string Label,DateTimeOffset Date)>();
        if(policyIds.Length>0)
        {
            var policies=await(from version in db.Set<PolicyVersion>() join term in db.Set<PolicyTerm>() on version.TermId equals term.Id
                where policyIds.Contains(version.Id) select new{version.Id,version.Sequence,version.EffectiveAt,term.Number}).AsNoTracking().ToArrayAsync(token);
            foreach(var p in policies)sources[p.Id]=($"Term {p.Number} · policy version {p.Sequence}",p.EffectiveAt);
        }
        if(quoteIds.Length>0)
        {
            var quotes=await db.Set<QuoteRevision>().AsNoTracking().Where(x=>quoteIds.Contains(x.Id)).Select(x=>new{x.Id,x.Number,x.SavedAt}).ToArrayAsync(token);
            foreach(var q in quotes)sources[q.Id]=($"Quote revision {q.Number}",q.SavedAt);
        }
        if(termsIds.Length>0)
        {
            var terms=await(from t in db.Set<ServicingTermsVersion>() join draft in db.Set<ServicingDraft>() on t.DraftId equals draft.Id
                where termsIds.Contains(t.Id) select new{t.Id,t.Sequence,t.PreparedAt,draft.Kind}).AsNoTracking().ToArrayAsync(token);
            foreach(var t in terms)sources[t.Id]=($"{(t.Kind=="renewal"?"Renewal":"Adjustment")} terms {t.Sequence}",t.PreparedAt);
        }
        var versionIds=rows.Select(x=>x.Version.Id).ToArray();
        var contents=await db.Set<DocumentVersionContent>().AsNoTracking().Where(x=>versionIds.Contains(x.VersionId)).ToDictionaryAsync(x=>x.VersionId,token);
        foreach(var (version,view) in rows)
        {
            view["sourceKind"]=version.SourceKind;
            var sourceId=version.PolicyVersionId??version.QuoteRevisionId??version.ServicingTermsVersionId;
            if(sourceId is Guid id && sources.TryGetValue(id,out var source)){view["sourceLabel"]=source.Label;view["sourceDate"]=source.Date;}
            else if(version.SourceKind=="upload"){view["sourceLabel"]="Uploaded evidence";view["sourceDate"]=version.CreatedAt;}
            if(version.SourceHash is not null)view["sourceHash"]=version.SourceHash;
            if(version.TemplateHash is not null)view["templateHash"]=version.TemplateHash;
            if(version.TermsHash is not null)view["termsHash"]=version.TermsHash;
            if(version.QuoteTermsVersionId is Guid termsId)view["quoteTermsVersionId"]=termsId;
            if(contents.TryGetValue(version.Id,out var content) && content.RendererVersion is not null)
            {
                view["rendererVersion"]=content.RendererVersion;view["projectionVersion"]=content.ProjectionVersion;
                view["fontVersion"]=content.FontVersion;view["pageCount"]=content.PageCount;
            }
        }
    }
}
