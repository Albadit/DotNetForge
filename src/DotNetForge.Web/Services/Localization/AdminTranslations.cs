using System.Globalization;
using System.Reflection;
using System.Text.Json;
using DotNetForge.Core.Validation;

namespace DotNetForge.Web.Services.Localization;

/// <summary>A language the admin can be shown in: its code, its own name (<c>Deutsch</c>) and the writing direction.</summary>
public sealed record AdminLanguageOption(string Code, string Name, bool RightToLeft);

/// <summary>
/// The translations of one admin language (.docs/features/internationalization.md#admin-language), keyed by the English
/// text: <see cref="Strings"/> for the views and controllers, <see cref="Script"/> for the admin scripts (sent to the
/// browser with every admin page) and <see cref="Builder"/> for the page builder editor (sent with the builder only).
/// </summary>
public sealed class AdminCatalog
{
    public AdminCatalog(AdminLanguageOption language, IReadOnlyDictionary<string, string> strings, IReadOnlyDictionary<string, string> script,
        IReadOnlyDictionary<string, string> builder)
    {
        Language = language;
        Strings = strings;
        Script = script;
        Builder = builder;
        ScriptJson = Json(script);
        BuilderJson = Json(builder);
    }

    public AdminLanguageOption Language { get; }

    public IReadOnlyDictionary<string, string> Strings { get; }

    public IReadOnlyDictionary<string, string> Script { get; }

    public IReadOnlyDictionary<string, string> Builder { get; }

    /// <summary><see cref="Script"/> as JSON, for the <c>dnf-text</c> meta tag of the admin layout; empty when there is none.</summary>
    public string ScriptJson { get; }

    /// <summary><see cref="Builder"/> as JSON, for the <c>dnf-text</c> meta tag of the page builder; empty when there is none.</summary>
    public string BuilderJson { get; }

    private static string Json(IReadOnlyDictionary<string, string> texts) => texts.Count == 0 ? string.Empty : JsonSerializer.Serialize(texts);
}

/// <summary>
/// The admin languages (.docs/features/internationalization.md#admin-language): English - the text in the code - and one
/// catalog per language embedded from <c>Localization/Admin/&lt;code&gt;.json</c>. A language is offered exactly when its
/// catalog ships; a text a catalog lacks shows in English. Loaded once; singleton.
/// </summary>
public sealed class AdminTranslations
{
    public const string English = "en";

    /// <summary>The embedded resource name prefix of the catalogs (see the web project file).</summary>
    public const string ResourcePrefix = "AdminText/";

    private static readonly HashSet<string> RightToLeftLanguages = new(StringComparer.OrdinalIgnoreCase) { "ar", "fa", "he", "ur" };

    private readonly Dictionary<string, AdminCatalog> _catalogs;

    public AdminTranslations()
        : this(typeof(AdminTranslations).Assembly)
    {
    }

    public AdminTranslations(Assembly assembly)
    {
        _catalogs = new Dictionary<string, AdminCatalog>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            var code = name[ResourcePrefix.Length..^".json".Length];
            using var stream = assembly.GetManifestResourceStream(name)!;
            _catalogs[code] = Parse(code, stream);
        }

        Languages = new[] { Option(English) }
            .Concat(_catalogs.Values.Select(c => c.Language).OrderBy(l => l.Name, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true)))
            .ToList();
    }

    /// <summary>English first, then every language with a catalog by its own name.</summary>
    public IReadOnlyList<AdminLanguageOption> Languages { get; }

    /// <summary>The catalog of a language (case-insensitive); null for English and for a language without one.</summary>
    public AdminCatalog? Find(string? code) =>
        code is not null && _catalogs.TryGetValue(code, out var catalog) ? catalog : null;

    /// <summary>The language offered under <paramref name="code"/> (case-insensitive), or null.</summary>
    public AdminLanguageOption? Language(string? code) =>
        Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>A catalog file: <c>{ "strings": { "Save": "Speichern" }, "script": { ... }, "builder": { ... } }</c>.</summary>
    public static AdminCatalog Parse(string code, Stream json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return new AdminCatalog(Option(code), Section(document.RootElement, "strings"), Section(document.RootElement, "script"),
            Section(document.RootElement, "builder"));
    }

    private static Dictionary<string, string> Section(JsonElement root, string name)
    {
        var section = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in values.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                {
                    section[p.Name] = p.Value.GetString()!;
                }
            }
        }

        return section;
    }

    private static AdminLanguageOption Option(string code) =>
        new(code, LocaleCodes.SuggestedName(code), RightToLeftLanguages.Contains(LocaleCodes.Language(code)));
}

/// <summary>
/// The admin text of the current request (.docs/features/internationalization.md#admin-language): views use it as
/// <c>@T["Save"]</c> or <c>@T["{0} entries", count]</c>, controllers through <c>T</c> of <c>AdminControllerBase</c>. The key
/// is always the English text (a string literal, so the catalogs can be checked); a missing translation shows the
/// English. <c>AdminLanguageMiddleware</c> picks the language per request; without it the text is English. Scoped.
/// </summary>
public sealed class AdminText : DotNetForge.UI.IDnfText
{
    private readonly AdminTranslations _translations;
    private AdminCatalog? _catalog;

    public AdminText(AdminTranslations translations)
    {
        _translations = translations;
        Language = translations.Languages[0];
    }

    /// <summary>The language the text is shown in.</summary>
    public AdminLanguageOption Language { get; private set; }

    /// <summary>The script translations as JSON (empty in English), for the admin layout.</summary>
    public string ScriptJson => _catalog?.ScriptJson ?? string.Empty;

    /// <summary>The page builder editor's translations as JSON (empty in English).</summary>
    public string BuilderJson => _catalog?.BuilderJson ?? string.Empty;

    /// <summary>Shows this request's text in <paramref name="code"/>; an unknown code keeps English.</summary>
    public void Use(string code)
    {
        Language = _translations.Language(code) ?? _translations.Languages[0];
        _catalog = _translations.Find(Language.Code);
    }

    /// <summary>The text in the admin language.</summary>
    public string this[string text] =>
        _catalog is not null && _catalog.Strings.TryGetValue(text, out var translated) ? translated : text;

    /// <summary>
    /// Marks an English text that is translated later through a variable (<c>T[option.Label]</c>): returns it unchanged,
    /// and the catalog check finds it like a <c>T["..."]</c> literal.
    /// </summary>
    public static string Key(string text) => text;

    /// <summary>The text in the admin language with <c>{0}</c>, <c>{1}</c>... filled in (numbers culture-neutral).</summary>
    public string this[string text, params object?[] args] =>
        string.Format(CultureInfo.InvariantCulture, this[text], args);
}
