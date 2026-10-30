using System.Text;
using System.Xml.Linq;

namespace DotNetForge.Core.Media;

/// <summary>A .docx as paragraphs, headings, lists, tables and images - its text and structure, not its page layout.</summary>
internal sealed class WordPreview
{
    private const int MaxBlocks = 5000;
    private const string Part = "word/document.xml";
    private const double EmuPerPixel = 9525;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    private readonly OfficePackage _package;
    private readonly List<PreviewBlock> _blocks = new();
    private readonly Dictionary<string, int> _headings = new();
    private readonly Dictionary<string, XElement> _styleNumbering = new();
    private readonly HashSet<string> _bulletLevels = new();
    private Dictionary<string, string> _images = new();
    private bool _overflow;

    public WordPreview(OfficePackage package) => _package = package;

    public DocumentPreview Read()
    {
        var body = _package.Xml(Part)?.Root?.Element(W + "body") ?? throw new InvalidDataException("A Word document has a body.");
        _images = _package.Relationships(Part).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().Path);
        ReadStyles();
        ReadNumbering();
        Blocks(body);
        return new DocumentPreview(DocumentPreview.Document) { Blocks = _blocks, Partial = _overflow || _package.ImagesOmitted };
    }

    /// <summary>Which styles are headings. <c>w:name</c> is the same in every language; a style id isn't.</summary>
    private void ReadStyles()
    {
        var styles = _package.Xml("word/styles.xml")?.Root?.Elements(W + "style").ToList();
        if (styles is null)
        {
            return;
        }

        var byId = styles.Where(s => s.Attribute(W + "styleId") is not null)
            .GroupBy(s => (string)s.Attribute(W + "styleId")!).ToDictionary(g => g.Key, g => g.First());
        foreach (var (id, style) in byId)
        {
            var current = style;
            for (var depth = 0; current is not null && depth < 8; depth++)
            {
                if (HeadingOf(current) is { } level)
                {
                    _headings[id] = level;
                    break;
                }

                current = Value(current.Element(W + "basedOn")) is { } parent ? byId.GetValueOrDefault(parent) : null;
            }

            if (style.Element(W + "pPr")?.Element(W + "numPr") is { } numbering)
            {
                _styleNumbering[id] = numbering;
            }
        }
    }

    private static int? HeadingOf(XElement style)
    {
        var name = Value(style.Element(W + "name"))?.Trim().ToLowerInvariant();
        if (name is "title")
        {
            return 1;
        }

        if (name is "subtitle")
        {
            return 2;
        }

        if (name?.StartsWith("heading ", StringComparison.Ordinal) == true && int.TryParse(name[8..], out var level))
        {
            return Math.Clamp(level, 1, 6);
        }

        return int.TryParse(Value(style.Element(W + "pPr")?.Element(W + "outlineLvl")), out var outline) && outline is >= 0 and < 9
            ? Math.Min(outline + 1, 6)
            : null;
    }

    /// <summary>Which list levels are bullets (<c>numId:level</c>); the others are numbered.</summary>
    private void ReadNumbering()
    {
        var root = _package.Xml("word/numbering.xml")?.Root;
        if (root is null)
        {
            return;
        }

        var abstracts = root.Elements(W + "abstractNum").Where(a => a.Attribute(W + "abstractNumId") is not null)
            .GroupBy(a => (string)a.Attribute(W + "abstractNumId")!).ToDictionary(g => g.Key, g => g.First());
        foreach (var number in root.Elements(W + "num"))
        {
            var id = (string?)number.Attribute(W + "numId");
            var source = Value(number.Element(W + "abstractNumId"));
            if (id is null || source is null || !abstracts.TryGetValue(source, out var definition))
            {
                continue;
            }

            foreach (var level in definition.Elements(W + "lvl"))
            {
                if (Value(level.Element(W + "numFmt")) is "bullet" or "none")
                {
                    _bulletLevels.Add($"{id}:{(string?)level.Attribute(W + "ilvl")}");
                }
            }
        }
    }

    private void Blocks(XElement container)
    {
        foreach (var child in container.Elements())
        {
            if (child.Name == W + "p")
            {
                Paragraph(child);
            }
            else if (child.Name == W + "tbl")
            {
                Add(new PreviewBlock { Rows = Table(child) });
            }
            else if (child.Name == W + "sdt" && child.Element(W + "sdtContent") is { } content)
            {
                Blocks(content);
            }
        }
    }

    private void Paragraph(XElement paragraph)
    {
        var properties = paragraph.Element(W + "pPr");
        var style = Value(properties?.Element(W + "pStyle"));
        var numbering = properties?.Element(W + "numPr") ?? (style is null ? null : _styleNumbering.GetValueOrDefault(style));
        var numberId = Value(numbering?.Element(W + "numId"));
        var level = int.TryParse(Value(numbering?.Element(W + "ilvl")), out var ilvl) ? ilvl : 0;
        var isList = numberId is not null and not "0";

        var runs = new List<PreviewRun>();
        var extras = new List<XElement>();
        Inline(paragraph, runs, extras);
        _overflow |= paragraph.Descendants(A + "graphicData").Any(g => ((string?)g.Attribute("uri"))?.Contains("chart", StringComparison.Ordinal) == true
            || ((string?)g.Attribute("uri"))?.Contains("diagram", StringComparison.Ordinal) == true);
        if (runs.Count > 0)
        {
            Add(new PreviewBlock
            {
                Runs = runs,
                Heading = style is not null && _headings.TryGetValue(style, out var heading) ? heading : null,
                List = isList ? level + 1 : null,
                Ordered = isList && !_bulletLevels.Contains($"{numberId}:{level}") ? true : null,
                Align = Value(properties?.Element(W + "jc")) switch
                {
                    "center" => "center",
                    "right" or "end" => "right",
                    "both" or "distribute" => "justify",
                    _ => null,
                },
            });
        }

        foreach (var extra in extras)
        {
            if (extra.Name == W + "txbxContent")
            {
                Blocks(extra);
            }
            else if (_package.Image(_images.GetValueOrDefault((string?)extra.Attribute(R + "embed") ?? string.Empty)) is { } src)
            {
                Add(new PreviewBlock { Src = src, Width = ShownWidth(extra) });
            }
        }
    }

    /// <summary>An image's size in the document, which is rarely the size of its file.</summary>
    private static int? ShownWidth(XElement image)
    {
        var extent = image.Ancestors().FirstOrDefault(e => e.Name == Wp + "inline" || e.Name == Wp + "anchor")?.Element(Wp + "extent");
        return long.TryParse((string?)extent?.Attribute("cx"), out var width) && width > 0 ? (int)Math.Round(width / EmuPerPixel) : null;
    }

    /// <summary>The runs of a paragraph; links, tracked insertions and content controls wrap runs. Deleted text doesn't count.</summary>
    private static void Inline(XElement parent, List<PreviewRun> runs, List<XElement> extras)
    {
        foreach (var child in parent.Elements())
        {
            if (child.Name == W + "r")
            {
                Run(child, runs, extras);
            }
            else if (child.Name == W + "hyperlink" || child.Name == W + "ins" || child.Name == W + "smartTag")
            {
                Inline(child, runs, extras);
            }
            else if (child.Name == W + "sdt" && child.Element(W + "sdtContent") is { } content)
            {
                Inline(content, runs, extras);
            }
        }
    }

    private static void Run(XElement run, List<PreviewRun> runs, List<XElement> extras)
    {
        var text = new StringBuilder();
        foreach (var child in run.Elements())
        {
            if (child.Name == W + "t")
            {
                text.Append(child.Value);
            }
            else if (child.Name == W + "tab")
            {
                text.Append('\t');
            }
            else if (child.Name == W + "br" || child.Name == W + "cr")
            {
                text.Append('\n');
            }
            else if (child.Name == W + "noBreakHyphen")
            {
                text.Append('-');
            }
        }

        // A drawing can come twice, as a modern shape and as a fallback for old readers.
        extras.AddRange(run.Descendants().Where(e => (e.Name == A + "blip" || e.Name == W + "txbxContent") && !e.Ancestors(Mc + "Fallback").Any()));
        if (text.Length > 0)
        {
            var properties = run.Element(W + "rPr");
            runs.Add(new PreviewRun(text.ToString())
            {
                Bold = On(properties, "b"),
                Italic = On(properties, "i"),
                Underline = properties?.Element(W + "u") is { } u && (string?)u.Attribute(W + "val") != "none" ? true : null,
                Strike = On(properties, "strike"),
            });
        }
    }

    private static IReadOnlyList<IReadOnlyList<string>> Table(XElement table) =>
        table.Elements(W + "tr")
            .Select(row => (IReadOnlyList<string>)row.Elements(W + "tc").Select(CellText).ToList())
            .ToList();

    private static string CellText(XElement cell) =>
        string.Join('\n', cell.Descendants(W + "p").Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value))));

    private void Add(PreviewBlock block)
    {
        if (_blocks.Count < MaxBlocks)
        {
            _blocks.Add(block);
        }
        else
        {
            _overflow = true;
        }
    }

    private static string? Value(XElement? element) => (string?)element?.Attribute(W + "val");

    /// <summary>A toggle such as <c>w:b</c>: on when present, unless its value turns it off.</summary>
    private static bool? On(XElement? properties, string name)
    {
        var toggle = properties?.Element(W + name);
        return toggle is not null && (string?)toggle.Attribute(W + "val") is null or "1" or "true" or "on" ? true : null;
    }
}
