namespace DotNetForge.PageBuilder.Rendering
{
    /// <summary>Page icons as inline SVG. Mirrors renderer/icons.ts.</summary>
    public static class Icons
    {
        private const string Open = "<svg class=\"pe-ico\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";

        /// <summary>The icon's SVG element (empty for an unknown name). Decorative unless a label is given.</summary>
        public static string Svg(string name, string label = "")
        {
            if (name == null || !Schema.PageIcons.TryGetValue(name, out var body) || string.IsNullOrEmpty(body)) return string.Empty;
            var a11y = string.IsNullOrEmpty(label) ? " aria-hidden=\"true\" focusable=\"false\"" : " role=\"img\" aria-label=\"" + Format.EscapeHtml(label) + "\"";
            return Open + a11y + ">" + body + "</svg>";
        }
    }
}
