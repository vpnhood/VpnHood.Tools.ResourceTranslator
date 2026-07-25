using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace VpnHood.Tools.ResourceTranslator.Site;

/// <summary>
/// Finds the source pages of a site run. Returns paths relative to the site root, with forward
/// slashes, in a stable order so watch files and reports do not churn between runs.
/// </summary>
public static class SitePageDiscovery
{
    public static IReadOnlyList<string> Discover(SiteOptions options)
    {
        return Execute(options.RootPath, options.PagePatterns, options.ExcludePatterns);
    }

    /// <summary>Applies the page globs under an arbitrary root (used to scan a locale tree).</summary>
    public static IReadOnlyList<string> DiscoverUnder(string rootPath, IReadOnlyList<string> pagePatterns)
    {
        return Execute(rootPath, pagePatterns, excludePatterns: []);
    }

    private static IReadOnlyList<string> Execute(
        string rootPath, IReadOnlyList<string> includePatterns, IReadOnlyList<string> excludePatterns)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(includePatterns);
        matcher.AddExcludePatterns(excludePatterns);

        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(rootPath)));
        return result.Files
            .Select(file => file.Path.Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }
}
