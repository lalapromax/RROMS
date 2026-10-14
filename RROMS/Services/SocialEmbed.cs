using System.Text.RegularExpressions;

namespace RROMS.Services;

/// A social post we know how to show: which site it is on, the address of the post itself, and its embeddable address.
public record SocialPost(string Platform, string PostUrl, string EmbedUrl);

/// 
/// Turns an administrator-supplied link into a safe embed (brief 6.J). Only the sites listed below are accepted, only
/// as https links, and the embed address is REBUILT from the post id we parsed - the administrator's raw text is never
/// put into an iframe. Anything else is rejected when saved, so a pasted "javascript:" or look-alike domain can't get in.
/// 
public static class SocialEmbed
{
    private static readonly Regex InstagramPath = new(@"^/(?<kind>reel|reels|p|tv)/(?<code>[A-Za-z0-9_-]{5,30})/?$", RegexOptions.Compiled);
    private static readonly Regex YouTubeId = new(@"^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);

    public static bool TryParse(string? url, out SocialPost? post)
    {
        post = null;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host.ToLowerInvariant();

        if (host is "instagram.com" or "www.instagram.com")
        {
            var match = InstagramPath.Match(uri.AbsolutePath);
            if (!match.Success) return false;

            var kind = match.Groups["kind"].Value == "reels" ? "reel" : match.Groups["kind"].Value;
            var code = match.Groups["code"].Value;
            var postUrl = $"https://www.instagram.com/{kind}/{code}/";
            post = new SocialPost("Instagram", postUrl, postUrl + "embed");
            return true;
        }

        if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be")
        {
            string? id = null;
            if (host == "youtu.be")
            {
                id = uri.AbsolutePath.Trim('/');
            }
            else if (uri.AbsolutePath == "/watch")
            {
                id = System.Web.HttpUtility.ParseQueryString(uri.Query)["v"];
            }
            else if (uri.AbsolutePath.StartsWith("/shorts/", StringComparison.Ordinal))
            {
                id = uri.AbsolutePath["/shorts/".Length..].Trim('/');
            }

            if (id == null || !YouTubeId.IsMatch(id)) return false;

            post = new SocialPost("YouTube", $"https://www.youtube.com/watch?v={id}", $"https://www.youtube-nocookie.com/embed/{id}");
            return true;
        }

        return false;
    }
}

/// Checks for links an administrator pastes into a "website" or "image" box.
public static class UrlSafety
{
    /// An absolute http(s) address. Rejects javascript:, data:, file: and relative paths.
    public static bool IsHttpUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// One of our own uploaded images (/uploads/...) or an https image address.
    public static bool IsAcceptableImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var trimmed = url.Trim();
        if (trimmed.StartsWith("/uploads/", StringComparison.Ordinal) && !trimmed.Contains("..")) return true;
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }
}
