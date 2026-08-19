using System.Net.Http;
using System.Text.RegularExpressions;

namespace WikiGenerator;

/// <summary>
/// Best-effort live-wiki lookups: the `image = ...` infobox field is hand-curated (the pak
/// has no source for it) and must survive every regeneration. Fetches the live wiki's
/// current page for a detail-page entity - the new split structure's own
/// `{ns}:{slug}:auto_infobox` subpage when it exists (a curator edits the image field
/// there after migration), falling back to the legacy flat `{ns}:{slug}` page (still the
/// live shape for anything not yet migrated) - and pulls out whatever `image = ...` line
/// it finds. A network failure, missing page, or absent field is not an error: it just
/// means nothing to preserve, and generation proceeds exactly as if this class didn't
/// exist.
/// </summary>
internal static class LiveWiki
{
    private const string BaseUrl = "https://wiki.aseanmotorclub.com/";
    private static readonly Regex ImageLine = new(@"^image\s*=.*$", RegexOptions.Multiline);

    /// <summary>Tries the new auto_infobox subpage first (post-migration source of truth),
    /// then the legacy flat page (pre-migration source).</summary>
    public static async Task<string?> FetchImageLine(HttpClient http, string ns, string slug) =>
        await FetchImageLineFrom(http, $"{ns}:{slug}:auto_infobox")
        ?? await FetchImageLineFrom(http, $"{ns}:{slug}");

    private static async Task<string?> FetchImageLineFrom(HttpClient http, string pageId)
    {
        try
        {
            var text = await http.GetStringAsync($"{BaseUrl}{pageId}?do=export_raw");
            var m = ImageLine.Match(text);
            return m.Success ? m.Value.TrimEnd() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Inserts a preserved `image = ...` line right after `name = ...` in a
    /// freshly rendered infobox block (the position the infobox plugin's own docs use); a
    /// no-op when there's nothing to preserve.</summary>
    public static string MergeImage(string infobox, string? imageLine)
    {
        if (imageLine is null) return infobox;
        var lines = infobox.Replace("\r\n", "\n").Split('\n').ToList();
        var insertAt = lines.Count > 1 && lines[1].StartsWith("name", StringComparison.Ordinal) ? 2 : 1;
        lines.Insert(insertAt, imageLine);
        return string.Join('\n', lines);
    }
}
