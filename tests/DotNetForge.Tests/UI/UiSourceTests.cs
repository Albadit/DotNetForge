using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace DotNetForge.Tests.UI;

/// <summary>
/// Guards of the DotNetForge UI library (.docs/ui/README.md) over the CMS's own sources: the old "Forge" class names,
/// tag helpers, tokens and script API are gone for good (.docs/ui/migration.md - a hard replace, no aliases), every
/// <c>dnf-</c> class the markup and scripts use is defined by a stylesheet (a typo would silently render unstyled),
/// and no stylesheet needs <c>!important</c> (.docs/ui/styling.md#layers).
/// </summary>
public sealed partial class UiSourceTests
{
    /// <summary>Every class the Forge stylesheets defined, minus the names that are now Tailwind utilities.</summary>
    private static readonly HashSet<string> LegacyClasses = new(StringComparer.Ordinal)
    {
        "active", "activity-icon", "alert", "alert-body", "alert-danger", "alert-info", "alert-success", "alert-title", "alert-warning",
        "auth-card", "auth-divider", "auth-foot", "auth-forge", "auth-heading", "auth-label-row", "auth-main", "auth-mobile-brand",
        "auth-panel", "auth-panel-logo", "auth-pitch", "auth-pitch-title", "auth-points", "auth-shell", "auth-state", "auth-switch",
        "auth-top", "avatar", "avatar-lg", "avatar-sm", "badge", "badge-accent", "badge-danger", "badge-dot", "badge-info",
        "badge-outline", "badge-success", "badge-warning", "breadcrumbs", "btn", "btn-block", "btn-danger", "btn-danger-soft",
        "btn-ghost", "btn-group", "btn-icon", "btn-lg", "btn-primary", "btn-secondary", "btn-sm", "bulk-bar", "bulk-select", "card",
        "card-accent", "card-body", "card-danger", "card-description", "card-footer", "card-header", "card-title", "cell-lead",
        "cell-primary", "cell-sub", "check", "check-hint", "check-text", "choice-card", "choice-cards", "choice-list", "choice-row",
        "cluster", "cluster-lg", "code-block", "code-editor", "code-editor-status", "code-editor-surface", "code-status-count",
        "code-status-errors", "code-status-item", "code-status-spacer", "code-status-warnings", "code-var", "col-actions", "col-check",
        "col-check-center", "col-rule", "collection-bar", "compact", "content", "content-manager", "content-medium", "content-narrow",
        "danger", "dashboard-columns", "dashboard-hero", "delivery", "delivery-body", "delivery-code", "delivery-list", "dialog",
        "dialog-body", "dialog-footer", "dialog-icon", "dialog-text", "dialog-title", "dl", "dl-compact", "dot", "dot-danger",
        "dot-success", "dot-warning", "dragging", "drop-target", "dropzone", "dropzone-files", "dropzone-title", "editor",
        "editor-aside", "editor-main", "editor-title", "effective-list", "email-preview", "empty-state", "empty-state-actions",
        "empty-state-icon", "empty-state-text", "empty-state-title", "end", "endpoint-call-row", "endpoint-call-text", "endpoint-calls",
        "endpoint-hints", "endpoint-op", "endpoint-op-head", "endpoint-op-settings", "endpoint-op-summary", "endpoint-op-toggle",
        "endpoint-params", "endpoint-path", "endpoint-path-prefix", "endpoint-request", "endpoint-sql", "endpoint-test", "entry-api",
        "entry-fields", "entry-table", "ext-error", "ext-frame", "ext-frame-card", "ext-frame-loading", "extension-badges",
        "extension-card", "extension-description", "extension-foot", "extension-head", "extension-icon", "extension-list",
        "extension-name", "extension-problem", "extension-problem-button", "fail", "field", "field-add", "field-badge", "field-empty",
        "field-error", "field-flags", "field-grip", "field-group", "field-hint", "field-item", "field-key", "field-label", "field-list",
        "field-name", "field-nested", "field-nested-title", "field-optional", "field-palette", "field-palette-item", "field-required",
        "field-row", "field-settings", "field-summary", "field-tools", "field-type-icon", "field-type-name", "fieldset", "file-dialog",
        "file-info", "file-name-extension", "file-name-row", "folder-crumbs", "folder-crumbs-sep", "folder-dialog", "folder-dialog-row",
        "folder-settings", "form", "form-actions", "form-grid", "form-grid-3", "form-narrow", "grid-2", "grid-3", "grid-4", "grid-auto",
        "has-error", "has-problem", "icon", "icon-lg", "icon-sm", "icon-xl", "input", "input-affix", "input-code", "input-icon",
        "input-sm", "is-active", "is-collapsed", "is-danger", "is-dragging", "is-dragover", "is-drop-target", "is-enabled", "is-error",
        "is-filtering", "is-invalid", "is-leaving", "is-loaded", "is-nav-open", "is-open", "is-over", "is-selected", "is-tabbed",
        "is-warning", "js", "layout-status", "list", "list-icon", "logo-on-dark", "logo-on-light", "main", "media-actions",
        "media-badges", "media-card", "media-dialog", "media-dialog-grid", "media-dialog-head", "media-folder", "media-items",
        "media-meta", "media-name", "media-picker", "media-picker-add", "media-picker-file", "media-picker-item", "media-preview",
        "media-preview-audio", "media-preview-entries", "media-preview-fallback", "media-preview-frame", "media-preview-head",
        "media-preview-image", "media-preview-stage", "media-preview-title", "media-preview-video", "media-preview-zip", "media-select",
        "media-thumb", "media-tile", "media-tile-file", "media-tile-name", "media-upload", "medium", "member-list", "menu", "menu-item",
        "menu-label", "menu-list", "menu-note", "menu-separator", "method-badge", "mfa-codes", "mfa-passkey-add", "mfa-qr", "mfa-text",
        "mfa-ways", "nav", "nav-badge", "nav-group", "nav-group-chevron", "nav-group-title", "nav-link", "nav-link-label", "nav-list",
        "nav-section", "nav-section-title", "note", "option-row", "option-rows", "page-actions", "page-description", "page-editor",
        "page-editor-tabs", "page-header", "page-header-row", "page-title", "page-tree", "pagination", "pagination-gap",
        "pagination-info", "pagination-pages", "pagination-size", "permission-add-row", "permission-group", "permission-group-desc",
        "permission-group-head", "permission-groups", "permission-subtitle", "permission-table", "probe-detail", "probe-failed",
        "probe-name", "probe-ok", "probe-step", "probe-steps", "probe-time", "profile-identity", "public", "relation-chip",
        "relation-chips", "relation-none", "relation-picker", "relation-results", "relation-search", "repeater", "repeater-head",
        "repeater-row", "repeater-row-head", "repeater-row-title", "repeater-rows", "repeater-tools", "row-grip", "rte", "rte-area",
        "rte-sep", "rte-toolbar", "schema-builder", "schema-json", "search-preview", "search-preview-text", "search-preview-title",
        "search-preview-url", "segmented", "settings-layout", "shell", "short", "sidebar", "sidebar-backdrop", "sidebar-brand",
        "sidebar-close", "sidebar-footer", "sidebar-header", "sidebar-user", "sidebar-user-button", "sidebar-user-email",
        "sidebar-user-name", "sidebar-user-text", "site-article", "site-brand", "site-footer", "site-header", "site-hero", "site-lead",
        "site-main", "site-main-full", "site-nav", "site-page-list", "site-preview-bar", "site-wrap", "skeleton", "skeleton-line",
        "skeleton-title", "skip-link", "span-2", "spread", "stack", "stack-lg", "stack-sm", "stat", "stat-grid", "stat-icon",
        "stat-label", "stat-text", "stat-value", "switch", "switch-row", "tab", "tab-panel", "tab-panel-title", "table-empty",
        "table-stack", "table-wrap", "tabs", "template-body", "template-card", "template-grid", "template-sample", "template-text",
        "template-variable", "template-variables", "text-mono", "text-truncate", "th-sort", "theme-icon-dark", "theme-icon-light",
        "theme-menu", "toast", "toast-danger", "toast-info", "toast-message", "toast-region", "toast-success", "toolbar",
        "toolbar-search", "toolbar-spacer", "tooltip", "topbar", "topbar-actions", "topbar-menu", "topbar-site", "tree-action",
        "tree-card", "tree-header-actions", "tree-item", "tree-link", "tree-row", "tree-search", "tree-slug", "tree-title",
        "tree-toggle", "tree-toggle-slot", "type-card", "type-card-actions", "type-card-head", "type-card-title", "type-grid",
        "type-icon", "type-stats", "upload-dialog", "upload-item", "upload-meta", "upload-name", "upload-preview", "upload-problem",
        "upload-remove", "upload-thumb", "validation-summary-errors", "validation-summary-valid", "webhook-header-add",
        "webhook-header-row", "webhook-url", "weight-medium", "weight-semibold", "widget-grid", "workflow", "workflow-card",
    };

    /// <summary><c>dnf-</c> names that are not classes: element ids, storage keys, tag helper names.</summary>
    private static readonly HashSet<string> NotClasses = new(StringComparer.Ordinal)
    {
        "dnf-theme", "dnf-nav", "dnf-nav-scroll", "dnf-scroll", "dnf-page-size", "dnf-view", "dnf-confirm-dialog", "dnf-toast-template",
        "dnf-menu",
    };

    private static readonly string Root = FindRoot();

    /// <summary>From this source file's compile-time path, so it works whatever the build output folder is.</summary>
    private static string FindRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DotNetForge.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (DotNetForge.slnx) not found.");
    }

    /// <summary>The CMS's own UI sources: views, scripts and C# that renders markup; the bundled admin extensions.</summary>
    private static IEnumerable<string> SourceFiles()
    {
        var web = Path.Combine(Root, "src", "DotNetForge.Web");
        var ui = Path.Combine(Root, "src", "DotNetForge.UI");
        var roots = new[]
        {
            web, ui,
            Path.Combine(Root, "extensions", "admin"),
            Path.Combine(Root, "extensions", "widgets"),
        };
        return roots
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cshtml", StringComparison.Ordinal) || path.EndsWith(".js", StringComparison.Ordinal) ||
                           path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => !IsExcluded(path));
    }

    /// <summary>Build output, third-party libraries and the page builder (its own pe- design and its rendered output).</summary>
    private static bool IsExcluded(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/bin/", StringComparison.Ordinal) || p.Contains("/obj/", StringComparison.Ordinal) ||
               p.Contains("/wwwroot/lib/", StringComparison.Ordinal) || p.Contains("/wwwroot/builder/", StringComparison.Ordinal) ||
               p.Contains("/wwwroot/js/builder/", StringComparison.Ordinal) || p.Contains("/PageBuilder/", StringComparison.Ordinal);
    }

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    [Fact]
    public void No_legacy_Forge_class_remains_in_markup_or_scripts()
    {
        var found = new List<string>();
        foreach (var file in SourceFiles())
        {
            foreach (var (token, line) in ClassTokens(file))
            {
                if (LegacyClasses.Contains(token))
                {
                    found.Add($"{Relative(file)}:{line}: {token}");
                }
            }
        }

        Assert.True(found.Count == 0, "Legacy Forge classes (.docs/ui/migration.md):\n" + string.Join('\n', found.Distinct().Take(200)));
    }

    [Fact]
    public void Every_dnf_class_used_is_defined_by_a_stylesheet()
    {
        var defined = DefinedDnfClasses();
        var missing = new List<string>();
        foreach (var file in SourceFiles())
        {
            foreach (var (token, line) in ClassTokens(file))
            {
                if (token.StartsWith("dnf-", StringComparison.Ordinal) && !NotClasses.Contains(token) && !IsPartial(token) && !defined.Contains(token))
                {
                    missing.Add($"{Relative(file)}:{line}: {token}");
                }
            }
        }

        Assert.True(missing.Count == 0, "dnf- classes no stylesheet defines:\n" + string.Join('\n', missing.Distinct().Take(200)));
    }

    [Fact]
    public void No_legacy_tag_helper_token_or_script_api_remains()
    {
        var found = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            var checks = new List<Regex> { LegacyTokenUse(), LegacyScriptApi(), LegacyAssetPath() };
            if (file.EndsWith(".cshtml", StringComparison.Ordinal))
            {
                checks.Add(LegacyTagHelper());
            }

            foreach (var check in checks)
            {
                foreach (Match match in check.Matches(text))
                {
                    found.Add($"{Relative(file)}:{LineOf(text, match.Index)}: {match.Value.Trim()}");
                }
            }
        }

        foreach (var css in StylesheetFiles())
        {
            var text = File.ReadAllText(css);
            foreach (Match match in LegacyTokenUse().Matches(text))
            {
                found.Add($"{Relative(css)}:{LineOf(text, match.Index)}: {match.Value.Trim()}");
            }
        }

        Assert.True(found.Count == 0, "Legacy Forge tag helpers, tokens, script API or asset paths:\n" + string.Join('\n', found.Take(200)));
    }

    [Fact]
    public void Stylesheets_never_use_important()
    {
        var found = new List<string>();
        foreach (var css in StylesheetFiles())
        {
            var text = StripComments(File.ReadAllText(css));
            foreach (Match match in Important().Matches(text))
            {
                found.Add($"{Relative(css)}:{LineOf(text, match.Index)}");
            }
        }

        Assert.True(found.Count == 0, "!important in a stylesheet (.docs/ui/styling.md#layers):\n" + string.Join('\n', found));
    }

    [Fact]
    public void The_old_Forge_files_are_gone()
    {
        Assert.False(Directory.Exists(Path.Combine(Root, "src", "DotNetForge.Web", "wwwroot", "css", "forge")), "wwwroot/css/forge still exists.");
        Assert.False(Directory.Exists(Path.Combine(Root, "src", "DotNetForge.Web", "wwwroot", "js", "forge")), "wwwroot/js/forge still exists.");
        Assert.False(Directory.Exists(Path.Combine(Root, "src", "DotNetForge.Web", "Ui")) &&
                     Directory.EnumerateFiles(Path.Combine(Root, "src", "DotNetForge.Web", "Ui"), "*TagHelpers.cs").Any(),
            "The Forge tag helpers in src/DotNetForge.Web/Ui still exist.");
    }

    /// <summary>The library's sources and the CMS's screen stylesheets (not the page builder's own design).</summary>
    private static IEnumerable<string> StylesheetFiles()
    {
        var styles = Directory.EnumerateFiles(Path.Combine(Root, "src", "DotNetForge.UI", "Styles"), "*.css", SearchOption.AllDirectories);
        var webCss = Path.Combine(Root, "src", "DotNetForge.Web", "wwwroot", "css");
        var web = Directory.Exists(webCss) ? Directory.EnumerateFiles(webCss, "*.css", SearchOption.AllDirectories) : [];
        var extensions = new[] { "admin", "widgets" }
            .Select(kind => Path.Combine(Root, "extensions", kind))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.css", SearchOption.AllDirectories));
        return styles.Concat(web).Concat(extensions).Where(path => !IsExcluded(path));
    }

    private static HashSet<string> DefinedDnfClasses()
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var css in StylesheetFiles())
        {
            foreach (Match match in CssClassSelector().Matches(StripComments(File.ReadAllText(css))))
            {
                var name = match.Groups[1].Value;
                defined.Add(name);
                // A block whose elements or modifiers are styled is defined too (a BEM block used as a namespace).
                foreach (var separator in new[] { "__", "--" })
                {
                    var at = name.IndexOf(separator, StringComparison.Ordinal);
                    if (at > 0)
                    {
                        defined.Add(name[..at]);
                    }
                }
            }
        }

        return defined;
    }

    /// <summary>A class built by concatenation ("dnf-button--" + variant): only its complete forms can be checked.</summary>
    private static bool IsPartial(string token) =>
        token.EndsWith('-') || token.EndsWith("__", StringComparison.Ordinal) || token.EndsWith('_');

    // ---- Class extraction ----------------------------------------------------------------------------------------

    /// <summary>Every class name a file writes: class attributes (also inside C#/JS strings) and, in scripts,
    /// className/classList/class: values and class selectors.</summary>
    internal static IEnumerable<(string Token, int Line)> ClassTokens(string file)
    {
        var text = File.ReadAllText(file);
        if (file.EndsWith(".cshtml", StringComparison.Ordinal))
        {
            // class="..." attributes, plus class lists kept in C# strings of the view's code ("dnf-badge dnf-badge--x"):
            // a string whose words all look like class names and include a hyphenated one.
            var attributes = RazorClassValues(text).SelectMany(v => Tokens(v.Value).Select(t => (t, LineOf(text, v.Index))));
            var codeStrings = CSharpString().Matches(text)
                .Where(m => ClassList().IsMatch(m.Groups[1].Value) && m.Groups[1].Value.Contains('-'))
                .SelectMany(m => Tokens(m.Groups[1].Value).Select(t => (t, LineOf(text, m.Index))));
            return attributes.Concat(codeStrings);
        }

        var results = new List<(string, int)>();
        foreach (Match literal in StringLiteral().Matches(text))
        {
            var value = literal.Value[1..^1];
            var line = LineOf(text, literal.Index);
            foreach (Match attr in ClassAttributeInString().Matches(value))
            {
                results.AddRange(Tokens(InterpolationHoles().Replace(attr.Groups[2].Value, " ")).Select(t => (t, line)));
            }

            if (!file.EndsWith(".js", StringComparison.Ordinal))
            {
                continue;
            }

            var before = text[Math.Max(0, literal.Index - 60)..literal.Index];
            if (ClassContext().IsMatch(before))
            {
                results.AddRange(Tokens(InterpolationHoles().Replace(value, " ")).Select(t => (t, line)));
            }

            if (SelectorContext().IsMatch(before))
            {
                foreach (Match selector in SelectorClass().Matches(value))
                {
                    results.Add((selector.Groups[1].Value, line));
                }
            }
        }

        return results;
    }

    /// <summary>The values of class="..." in Razor: static text plus the string literals inside @(...) / @x expressions.</summary>
    private static IEnumerable<(string Value, int Index)> RazorClassValues(string text)
    {
        foreach (Match match in RazorClassStart().Matches(text))
        {
            var start = match.Index + match.Length;
            var builder = new StringBuilder();
            var i = start;
            while (i < text.Length && text[i] != '"')
            {
                if (text[i] == '@' && i + 1 < text.Length && text[i + 1] == '(')
                {
                    i = ReadParenthesized(text, i + 1, builder);
                    continue;
                }

                if (text[i] == '@' && i + 1 < text.Length && (char.IsLetter(text[i + 1]) || text[i + 1] == '_'))
                {
                    i++;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.'))
                    {
                        i++;
                    }

                    builder.Append(' ');
                    continue;
                }

                builder.Append(text[i]);
                i++;
            }

            yield return (builder.ToString(), match.Index);
        }
    }

    /// <summary>Skips a balanced (...) C# expression starting at <paramref name="open"/>, appending its string literals.</summary>
    private static int ReadParenthesized(string text, int open, StringBuilder literals)
    {
        var depth = 0;
        var i = open;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                var end = i + 1;
                while (end < text.Length && text[end] != '"')
                {
                    end += text[end] == '\\' ? 2 : 1;
                }

                literals.Append(' ').Append(text, i + 1, Math.Max(0, Math.Min(end, text.Length) - i - 1)).Append(' ');
                i = end + 1;
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i + 1;
                }
            }

            i++;
        }

        return i;
    }

    private static IEnumerable<string> Tokens(string value) =>
        ClassToken().Matches(value).Select(m => m.Value).Where(t => !t.Contains('{') && !t.Contains('$'));

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string StripComments(string css) => CssComment().Replace(css, match => new string('\n', match.Value.Count(c => c == '\n')));

    [GeneratedRegex(@"\bclass=""")]
    private static partial Regex RazorClassStart();

    [GeneratedRegex(@"(?<![=\w])""([^""\r\n]*)""")]
    private static partial Regex CSharpString();

    [GeneratedRegex(@"^\s*[a-z][a-z0-9_-]*(?:\s+[a-z][a-z0-9_:/-]*)*\s*$")]
    private static partial Regex ClassList();

    [GeneratedRegex(@"""(?:[^""\\\r\n]|\\.)*""|'(?:[^'\\\r\n]|\\.)*'|`[^`]*`")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"\bclass=(\\?[""'])(.*?)\1")]
    private static partial Regex ClassAttributeInString();

    [GeneratedRegex(@"\$\{[^}]*\}|\{[^}]*\}|""\s*\+[^+]*\+\s*""")]
    private static partial Regex InterpolationHoles();

    /// <summary>Directly before a string: className = / className: / class: / classList.x( / setAttribute("class", .</summary>
    [GeneratedRegex(@"(?:className\s*[=:]\s*|\bclass\s*:\s*|classList\.\w+\(\s*(?:[^()]*,\s*)?|setAttribute\(\s*[""']class[""']\s*,\s*)$")]
    private static partial Regex ClassContext();

    [GeneratedRegex(@"(?:querySelector(?:All)?|closest|matches|\$all|\$|each)\(\s*$|(?:selector|floating)\s*=\s*$")]
    private static partial Regex SelectorContext();

    [GeneratedRegex(@"\.([a-zA-Z][\w-]*)")]
    private static partial Regex SelectorClass();

    [GeneratedRegex(@"[A-Za-z0-9_:/\[\]&>.%!-]+")]
    private static partial Regex ClassToken();

    [GeneratedRegex(@"\.(dnf-[a-z0-9_-]+)")]
    private static partial Regex CssClassSelector();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();

    [GeneratedRegex(@"!important")]
    private static partial Regex Important();

    /// <summary>A Forge tag helper element.</summary>
    [GeneratedRegex(@"<(?:icon|badge|alert|empty-state|stat-card|timestamp|form-errors|action-menu|menu-link|menu-button|menu-post|menu-separator|menu-note)[\s/>]")]
    private static partial Regex LegacyTagHelper();

    /// <summary>A var(--x) of a token without the --dnf- prefix (the page builder's --pe-/--dnn- tokens live elsewhere).</summary>
    [GeneratedRegex(@"var\(\s*--(?!dnf-|tw-)[a-z][\w-]*")]
    private static partial Regex LegacyTokenUse();

    [GeneratedRegex(@"\bwindow\.Forge\b|\bForge\.(?:toast|confirm)\b|\bTone\.(?:Neutral|Success|Warning|Danger|Info|Accent)\b|\bTimestampTagHelper\.")]
    private static partial Regex LegacyScriptApi();

    [GeneratedRegex(@"/css/forge/|/js/forge/|/img/icons\.svg")]
    private static partial Regex LegacyAssetPath();
}
