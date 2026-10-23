using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetForge.PageBuilder.Rendering
{
    /// <summary>A piece of a rendered page: markup to write as is, or the place of a DNN module.</summary>
    public abstract class Segment
    {
        internal Segment()
        {
        }
    }

    public sealed class HtmlSegment : Segment
    {
        public HtmlSegment(string html)
        {
            Html = html;
        }

        public string Html { get; }
    }

    public sealed class ModuleSegment : Segment
    {
        public ModuleSegment(int moduleId)
        {
            ModuleId = moduleId;
        }

        public int ModuleId { get; }
    }

    /// <summary>
    /// Splits rendered HTML at the <c>&lt;!--pe:module:N--&gt;</c> markers the module
    /// component writes, so the host can put each module's live control between
    /// the pieces of markup. Authored text can never produce a marker: every
    /// '&lt;' of it is escaped.
    /// </summary>
    public static class Segments
    {
        private static readonly Regex Marker = new Regex("<!--pe:module:([0-9]+)-->", RegexOptions.CultureInvariant);

        /// <summary>The segments in order; adjacent markup is merged and no markup segment is empty.</summary>
        public static IReadOnlyList<Segment> Split(string html)
        {
            var output = new List<Segment>();
            if (string.IsNullOrEmpty(html)) return output;
            var pending = new StringBuilder();
            var position = 0;
            foreach (Match m in Marker.Matches(html))
            {
                pending.Append(html, position, m.Index - position);
                position = m.Index + m.Length;
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var moduleId))
                {
                    // Not a module id this platform can have: keep the text as it is.
                    pending.Append(m.Value);
                    continue;
                }
                if (pending.Length > 0)
                {
                    output.Add(new HtmlSegment(pending.ToString()));
                    pending.Clear();
                }
                output.Add(new ModuleSegment(moduleId));
            }
            pending.Append(html, position, html.Length - position);
            if (pending.Length > 0) output.Add(new HtmlSegment(pending.ToString()));
            return output;
        }
    }
}
