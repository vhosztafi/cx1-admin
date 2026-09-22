using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace BackOffice.IntegrationTests;

internal static class OperationalBrowserBuild
{
    internal static string Select(string root,string fallback)
    {
        var selected=Environment.GetEnvironmentVariable("COVER_OPERATIONAL_ACCEPTANCE_DIST");
        if(string.IsNullOrWhiteSpace(selected))return fallback;
        var allowed=Path.GetFullPath(Path.Combine(root,"apps/backoffice/.local"))+Path.DirectorySeparatorChar;
        var directory=Path.GetFullPath(Path.Combine(root,"apps/backoffice",selected));
        Assert.StartsWith(allowed,directory,StringComparison.OrdinalIgnoreCase);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"operational-source-manifest.json")));
        Assert.Equal("operational-build-1",manifest.RootElement.GetProperty("format").GetString());
        Assert.Equal(File.ReadAllText(Path.Combine(directory,"BUILD_ID")).Trim(),manifest.RootElement.GetProperty("buildId").GetString());
        var paths=new List<string>();
        foreach(var source in new[]{"apps/backoffice/app","apps/backoffice/components","apps/backoffice/lib","apps/backoffice/public","contracts/generated"})
            paths.AddRange(Directory.EnumerateFiles(Path.Combine(root,source),"*",SearchOption.AllDirectories).Select(path=>Path.GetRelativePath(root,path).Replace('\\','/')));
        paths.AddRange(["apps/backoffice/next.config.ts","apps/backoffice/postcss.config.mjs","apps/backoffice/package.json","pnpm-lock.yaml"]);
        var saved=manifest.RootElement.GetProperty("files");
        Assert.Equal(paths.Order(StringComparer.Ordinal),saved.EnumerateObject().Select(x=>x.Name).Order(StringComparer.Ordinal));
        foreach(var path in paths)Assert.Equal(saved.GetProperty(path).GetString(),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,path)))));
        return selected;
    }
}
