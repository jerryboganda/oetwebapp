using Ganss.Xss;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// Thin wrapper around <see cref="Ganss.Xss.HtmlSanitizer"/> that locks the
/// whitelist to exactly the tags, attributes, and URL schemes we actually need
/// for OET learner content. Every admin-authored HTML field MUST be routed
/// through <see cref="SanitizePassage"/> (or an equivalent domain-specific
/// method) before it is persisted — do NOT sanitize at render time only.
///
/// <para>
/// The decision to sanitize on <b>ingest</b> rather than on <b>render</b> is
/// deliberate: the reading player (and other surfaces) use
/// <c>dangerouslySetInnerHTML</c> for styled passages, and every render path is
/// another chance to forget the sanitizer. Enforcing at the single persist
/// choke-point means stored XSS can't enter the database in the first place.
/// </para>
/// </summary>
public interface IHtmlSanitizer
{
    /// <summary>
    /// Sanitize an admin-authored OET reading passage or exam text. Returns a
    /// safe string; never throws. <c>null</c> input returns empty string.
    /// </summary>
    string SanitizePassage(string? input);
}

/// <summary>
/// Default implementation. The <see cref="Ganss.Xss.HtmlSanitizer"/> instance is
/// cached per process because rebuilding its allowlist per call is wasteful and
/// the instance is thread-safe.
/// </summary>
public sealed class HtmlSanitizerService : IHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public HtmlSanitizerService()
    {
        var s = new HtmlSanitizer();

        // Keep allowed tags tight. OET passages are letters, clinical notes,
        // short articles, and tables — none of which need <script>, <iframe>,
        // <object>, form elements, or SVG (which has its own XSS surface).
        s.AllowedTags.Clear();
        foreach (var tag in new[]
        {
            "a", "b", "i", "u", "em", "strong", "mark", "small", "sup", "sub", "span", "div",
            "p", "br", "hr",
            "ul", "ol", "li",
            "blockquote", "pre", "code",
            "h1", "h2", "h3", "h4", "h5", "h6",
            "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "col", "colgroup",
            "figure", "figcaption",
        })
        {
            s.AllowedTags.Add(tag);
        }

        // Attribute allowlist: style is intentionally NOT included (CSS injection is
        // another XSS vector). We allow class for project-owned CSS hooks, and the
        // minimum set of semantic attributes. No on* handlers — HtmlSanitizer
        // already strips them but the allowlist is defence-in-depth.
        s.AllowedAttributes.Clear();
        foreach (var attr in new[]
        {
            "class", "id", "title", "dir", "lang",
            "href", "target", "rel",
            "colspan", "rowspan", "scope",
            "start", "type", // list ordering
            "data-oet-role", "data-oet-gap", "data-oet-ref", // project hooks, reserved for authored markup
        })
        {
            s.AllowedAttributes.Add(attr);
        }

        // URL schemes. Block javascript:, data: (for non-image contexts), vbscript:, etc.
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("mailto");

        // Force rel="noopener noreferrer" on external links. Prevents window.opener
        // tab-nabbing if an author sticks an external link in a passage.
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement el
                && string.Equals(el.TagName, "A", StringComparison.OrdinalIgnoreCase))
            {
                var href = el.GetAttribute("href");
                if (!string.IsNullOrEmpty(href) &&
                    (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                     || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    el.SetAttribute("rel", "noopener noreferrer");
                    if (string.IsNullOrEmpty(el.GetAttribute("target")))
                    {
                        el.SetAttribute("target", "_blank");
                    }
                }
            }
        };

        _sanitizer = s;
    }

    public string SanitizePassage(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return _sanitizer.Sanitize(input);
    }
}

/// <summary>
/// Null-object sanitizer. Used only by the legacy secondary constructor of
/// <see cref="OetLearner.Api.Services.Reading.ReadingStructureService"/>
/// for read-only call paths that never invoke write APIs. NEVER register this
/// in DI — if it escapes into a write path it silently disables sanitization.
/// </summary>
public sealed class NoOpHtmlSanitizer : IHtmlSanitizer
{
    public string SanitizePassage(string? input) => input ?? string.Empty;
}
