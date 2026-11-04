using Microsoft.AspNetCore.Mvc.Rendering;

namespace DotNetForge.UI;

/// <summary>
/// Translates the few texts the components write themselves ("Actions", "Never", "5 min ago"). An app that shows its UI
/// in other languages registers one (the CMS: its admin text, .docs/features/internationalization.md#admin-language);
/// without one the text stays English. The key is the English text; <c>{0}</c> marks a value.
/// </summary>
public interface IDnfText
{
    /// <summary>The text in the UI language, or <paramref name="text"/> itself.</summary>
    string this[string text] { get; }
}

/// <summary>Looks texts up through the request's <see cref="IDnfText"/>, if any.</summary>
public static class DnfText
{
    /// <summary>The translator of the view's request, or one that keeps the English.</summary>
    public static Func<string, string> For(ViewContext? viewContext) =>
        viewContext?.HttpContext?.RequestServices?.GetService(typeof(IDnfText)) is IDnfText text ? key => text[key] : key => key;
}
