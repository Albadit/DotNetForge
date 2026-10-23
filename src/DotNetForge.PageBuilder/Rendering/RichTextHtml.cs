using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace DotNetForge.PageBuilder.Rendering
{
    /// <summary>
    /// Rich text data to markup. Every character of text is escaped; the only
    /// tags are the ones written here. Mirrors renderer/richtext.ts.
    /// </summary>
    public static class RichTextHtml
    {
        /// <summary>The attributes of a link that opens in a new tab.</summary>
        public const string NewTab = " target=\"_blank\" rel=\"noopener noreferrer\"";

        public static string RunHtml(Run run)
        {
            var html = Format.EscapeHtml(run.Text).Replace("\n", "<br>");
            if (run.Code) html = "<code>" + html + "</code>";
            if (run.Strike) html = "<s>" + html + "</s>";
            if (run.Underline) html = "<u>" + html + "</u>";
            if (run.Italic) html = "<em>" + html + "</em>";
            if (run.Bold) html = "<strong>" + html + "</strong>";
            if (!string.IsNullOrEmpty(run.Href)) html = "<a href=\"" + Format.EscapeHtml(run.Href) + "\"" + (run.NewTab ? NewTab : string.Empty) + ">" + html + "</a>";
            return html;
        }

        /// <summary>Runs (normalized again, as stored data is untrusted) to inline markup.</summary>
        public static string RunsHtml(JToken runs)
        {
            return RunsHtml(RichText.NormalizeRuns(runs));
        }

        public static string BlocksHtml(JToken blocks)
        {
            var sb = new StringBuilder();
            foreach (var block in RichText.NormalizeBlocks(blocks)) sb.Append(BlockHtml(block));
            return sb.ToString();
        }

        private static string RunsHtml(IEnumerable<Run> runs)
        {
            var sb = new StringBuilder();
            foreach (var run in runs) sb.Append(RunHtml(run));
            return sb.ToString();
        }

        private static string BlockHtml(Block block)
        {
            switch (block.Type)
            {
                case "ul":
                case "ol":
                    {
                        var sb = new StringBuilder().Append('<').Append(block.Type).Append('>');
                        foreach (var item in block.Items) sb.Append("<li>").Append(RunsHtml(item)).Append("</li>");
                        return sb.Append("</").Append(block.Type).Append('>').ToString();
                    }
                case "quote":
                    return "<blockquote><p>" + RunsHtml(block.Runs) + "</p></blockquote>";
                default:
                    return "<" + block.Type + ">" + RunsHtml(block.Runs) + "</" + block.Type + ">";
            }
        }
    }
}
