using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DotNetForge.Core.Media;

/// <summary>
/// A .pptx as positioned slides: text, pictures, tables and filled shapes, in the places and sizes the slide gives
/// them. A slide inherits from its layout and its master - positions, text sizes, colours, the logo and bars they
/// draw - so those are read too. Charts and diagrams are left out (<see cref="DocumentPreview.Partial"/>).
/// </summary>
internal sealed partial class SlidePreview
{
    private const int MaxSlides = 200;
    private const int MaxShapesPerSlide = 500;
    private const string Part = "ppt/presentation.xml";
    private const string SlideLayoutType = "/slideLayout";
    private const string SlideMasterType = "/slideMaster";
    private const string ThemeType = "/theme";
    private const double EmuPerPoint = 12700;
    private const string OmittedFill = "E5E7EB";
    private const string OmittedText = "4B5563";

    /// <summary>The size of a default 16:9 presentation, when the file doesn't say.</summary>
    private const double DefaultWidth = 12192000;
    private const double DefaultHeight = 6858000;

    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private readonly OfficePackage _package;
    private readonly Dictionary<string, Source?> _sources = new();
    private double _width = DefaultWidth;
    private double _height = DefaultHeight;
    private bool _partial;

    public SlidePreview(OfficePackage package) => _package = package;

    [GeneratedRegex("^[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();

    /// <summary>A slide, layout or master part with what its shapes refer to.</summary>
    private sealed record Source(XElement Root, IReadOnlyList<OfficeRelationship> Relationships, Source? Layout, Source? Master, Palette Palette);

    /// <summary>The theme's colours and the master's mapping of names such as <c>tx1</c> onto them.</summary>
    private sealed record Palette(Dictionary<string, string> Theme, Dictionary<string, string> Map);

    /// <summary>Maps coordinates in a group onto the slide.</summary>
    private readonly record struct Frame(double ScaleX, double ScaleY, double OffsetX, double OffsetY)
    {
        public static readonly Frame Identity = new(1, 1, 0, 0);
    }

    public DocumentPreview Read()
    {
        var presentation = _package.Xml(Part)?.Root ?? throw new InvalidDataException("A presentation is missing.");
        if (presentation.Element(P + "sldSz") is { } size)
        {
            _width = Number(size.Attribute("cx")) is > 0 and var width ? width : DefaultWidth;
            _height = Number(size.Attribute("cy")) is > 0 and var height ? height : DefaultHeight;
        }

        var parts = _package.Relationships(Part).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().Path);
        var slides = new List<PreviewSlide>();
        foreach (var slide in presentation.Element(P + "sldIdLst")?.Elements(P + "sldId") ?? Enumerable.Empty<XElement>())
        {
            if (slides.Count == MaxSlides)
            {
                _partial = true;
                break;
            }

            if ((string?)slide.Attribute(R + "id") is { } id && parts.TryGetValue(id, out var path) && Slide(path) is { } read)
            {
                slides.Add(read);
            }
        }

        return new DocumentPreview(DocumentPreview.Presentation)
        {
            Slides = slides,
            SlideRatio = Math.Round(_width / _height, 4),
            Partial = _partial || _package.ImagesOmitted,
        };
    }

    private PreviewSlide? Slide(string path)
    {
        var root = _package.Xml(path)?.Root;
        if (root is null || (string?)root.Attribute("show") == "0")
        {
            return null;
        }

        var relationships = _package.Relationships(path);
        var layout = Related(relationships, SlideLayoutType);
        var master = layout?.Master ?? Related(relationships, SlideMasterType);
        var slide = new Source(root, relationships, layout, master, master?.Palette ?? ReadPalette(null, relationships));

        var shapes = new List<PreviewShape>();
        if ((string?)root.Attribute("showMasterSp") != "0")
        {
            if ((string?)layout?.Root.Attribute("showMasterSp") != "0" && master is not null)
            {
                Tree(master.Root.Element(P + "cSld")?.Element(P + "spTree"), Frame.Identity, master, false, shapes);
            }

            if (layout is not null)
            {
                Tree(layout.Root.Element(P + "cSld")?.Element(P + "spTree"), Frame.Identity, layout, false, shapes);
            }
        }

        Tree(root.Element(P + "cSld")?.Element(P + "spTree"), Frame.Identity, slide, true, shapes);
        var (background, image) = Background(slide) ?? Background(layout) ?? Background(master) ?? (null, null);
        return new PreviewSlide(shapes) { Background = background, BackgroundImage = image };
    }

    private Source? Related(IReadOnlyList<OfficeRelationship> relationships, string type)
    {
        var path = relationships.FirstOrDefault(r => r.Type.EndsWith(type, StringComparison.Ordinal))?.Path;
        if (path is null)
        {
            return null;
        }

        if (_sources.TryGetValue(path, out var known))
        {
            return known;
        }

        Source? source = null;
        if (_package.Xml(path)?.Root is { } root)
        {
            var own = _package.Relationships(path);
            if (type == SlideLayoutType)
            {
                var master = Related(own, SlideMasterType);
                source = new Source(root, own, null, master, master?.Palette ?? ReadPalette(null, own));
            }
            else
            {
                source = new Source(root, own, null, null, ReadPalette(root, own));
            }
        }

        _sources[path] = source;
        return source;
    }

    private Palette ReadPalette(XElement? master, IReadOnlyList<OfficeRelationship> relationships)
    {
        var theme = new Dictionary<string, string>(StringComparer.Ordinal);
        var map = master?.Element(P + "clrMap")?.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value)
            ?? new Dictionary<string, string> { ["tx1"] = "dk1", ["bg1"] = "lt1", ["tx2"] = "dk2", ["bg2"] = "lt2" };
        var path = relationships.FirstOrDefault(r => r.Type.EndsWith(ThemeType, StringComparison.Ordinal))?.Path;
        var colours = path is null ? null : _package.Xml(path)?.Root?.Element(A + "themeElements")?.Element(A + "clrScheme");
        foreach (var colour in colours?.Elements() ?? Enumerable.Empty<XElement>())
        {
            if (Literal(colour) is { } hex)
            {
                theme[colour.Name.LocalName] = hex;
            }
        }

        return new Palette(theme, map);
    }

    private static string? Literal(XElement holder)
    {
        var value = (string?)holder.Element(A + "srgbClr")?.Attribute("val") ?? (string?)holder.Element(A + "sysClr")?.Attribute("lastClr");
        return value is not null && Hex().IsMatch(value) ? value : null;
    }

    /// <summary>The colour in <paramref name="holder"/>: literal, or by name from the theme.</summary>
    private static string? Colour(XElement? holder, Palette palette)
    {
        if (holder is null)
        {
            return null;
        }

        if (Literal(holder) is { } literal)
        {
            return literal;
        }

        var name = (string?)holder.Element(A + "schemeClr")?.Attribute("val");
        if (name is null)
        {
            return null;
        }

        return palette.Theme.GetValueOrDefault(palette.Map.GetValueOrDefault(name, name));
    }

    private (string? Colour, string? Image)? Background(Source? source)
    {
        var background = source?.Root.Element(P + "cSld")?.Element(P + "bg");
        if (background is null)
        {
            return null;
        }

        var properties = background.Element(P + "bgPr");
        if (properties?.Element(A + "blipFill")?.Element(A + "blip") is { } blip)
        {
            return (null, _package.Image(Target(source!, blip)));
        }

        var colour = Colour(properties?.Element(A + "solidFill"), source!.Palette)
            ?? Colour(properties?.Element(A + "gradFill")?.Element(A + "gsLst")?.Elements(A + "gs").FirstOrDefault(), source.Palette)
            ?? Colour(background.Element(P + "bgRef"), source.Palette);
        return colour is null ? null : (colour, null);
    }

    private static string? Target(Source source, XElement blip)
    {
        var id = (string?)blip.Attribute(R + "embed");
        return id is null ? null : source.Relationships.FirstOrDefault(r => r.Id == id)?.Path;
    }

    private void Tree(XElement? tree, Frame frame, Source source, bool placeholders, List<PreviewShape> shapes)
    {
        foreach (var child in tree?.Elements() ?? Enumerable.Empty<XElement>())
        {
            if (shapes.Count >= MaxShapesPerSlide)
            {
                _partial = true;
                return;
            }

            if (child.Name == P + "sp")
            {
                Shape(child, frame, source, placeholders, shapes);
            }
            else if (child.Name == P + "pic")
            {
                Picture(child, frame, source, placeholders, shapes);
            }
            else if (child.Name == P + "graphicFrame")
            {
                Graphic(child, frame, shapes);
            }
            else if (child.Name == P + "grpSp")
            {
                Tree(child, Inside(child.Element(P + "grpSpPr")?.Element(A + "xfrm"), frame), source, placeholders, shapes);
            }
            else if (child.Name == Mc + "AlternateContent")
            {
                Tree(child.Element(Mc + "Choice"), frame, source, placeholders, shapes);
            }
        }
    }

    private static Frame Inside(XElement? transform, Frame frame)
    {
        if (transform is null)
        {
            return frame;
        }

        var (x, y) = Pair(transform.Element(A + "off"), "x", "y");
        var (width, height) = Pair(transform.Element(A + "ext"), "cx", "cy");
        var (childX, childY) = Pair(transform.Element(A + "chOff"), "x", "y");
        var (childWidth, childHeight) = Pair(transform.Element(A + "chExt"), "cx", "cy");
        var scaleX = childWidth > 0 ? width / childWidth : 1;
        var scaleY = childHeight > 0 ? height / childHeight : 1;
        return new Frame(frame.ScaleX * scaleX, frame.ScaleY * scaleY,
            frame.OffsetX + frame.ScaleX * (x - childX * scaleX), frame.OffsetY + frame.ScaleY * (y - childY * scaleY));
    }

    private PreviewShape Box(XElement transform, Frame frame)
    {
        var (x, y) = Pair(transform.Element(A + "off"), "x", "y");
        var (width, height) = Pair(transform.Element(A + "ext"), "cx", "cy");
        return new PreviewShape(
            Percent(frame.OffsetX + x * frame.ScaleX, _width), Percent(frame.OffsetY + y * frame.ScaleY, _height),
            Percent(width * frame.ScaleX, _width), Percent(height * frame.ScaleY, _height));
    }

    private static double Percent(double value, double whole) => Math.Round(value / whole * 100, 3);

    private void Picture(XElement picture, Frame frame, Source source, bool placeholders, List<PreviewShape> shapes)
    {
        var placeholder = picture.Element(P + "nvPicPr")?.Element(P + "nvPr")?.Element(P + "ph");
        if (placeholder is not null && !placeholders)
        {
            return;
        }

        var transform = Transform(picture) ?? Inherited(placeholder, source).Select(Transform).FirstOrDefault(t => t is not null);
        var blip = picture.Element(P + "blipFill")?.Element(A + "blip");
        if (transform is not null && blip is not null && _package.Image(Target(source, blip)) is { } src)
        {
            shapes.Add(Box(transform, frame) with { Src = src });
        }
    }

    private void Graphic(XElement graphic, Frame frame, List<PreviewShape> shapes)
    {
        var table = graphic.Descendants(A + "tbl").FirstOrDefault();
        var transform = graphic.Element(P + "xfrm");
        if (table is null)
        {
            // A chart or a diagram: its frame stays on the slide, so what is missing is plain to see.
            _partial = true;
            if (transform is not null)
            {
                shapes.Add(Box(transform, frame) with
                {
                    Fill = OmittedFill,
                    Anchor = "center",
                    Blocks = new[] { new PreviewBlock { Runs = new[] { new PreviewRun("Chart not shown") { Size = 1.5, Color = OmittedText } }, Align = "center" } },
                });
            }

            return;
        }

        if (transform is null)
        {
            _partial = true;
            return;
        }

        shapes.Add(Box(transform, frame) with
        {
            Rows = table.Elements(A + "tr")
                .Select(row => (IReadOnlyList<string>)row.Elements(A + "tc").Select(cell => string.Join('\n', cell.Descendants(A + "p").Select(PlainText))).ToList())
                .ToList(),
        });
    }

    private static string PlainText(XElement paragraph) => string.Concat(paragraph.Descendants(A + "t").Select(t => t.Value));

    private void Shape(XElement shape, Frame frame, Source source, bool placeholders, List<PreviewShape> shapes)
    {
        var placeholder = shape.Element(P + "nvSpPr")?.Element(P + "nvPr")?.Element(P + "ph");
        if (placeholder is not null && !placeholders)
        {
            return; // on a layout or master it is an empty frame for the slide's own content
        }

        var inherited = Inherited(placeholder, source);
        var transform = Transform(shape) ?? inherited.Select(Transform).FirstOrDefault(t => t is not null);
        if (transform is null)
        {
            return;
        }

        var properties = shape.Element(P + "spPr");
        var fill = properties?.Element(A + "noFill") is not null ? null
            : Colour(properties?.Element(A + "solidFill"), source.Palette)
              ?? (properties?.Element(A + "gradFill") is { } gradient ? Colour(gradient.Element(A + "gsLst")?.Elements(A + "gs").FirstOrDefault(), source.Palette) : null)
              ?? (Number(shape.Element(P + "style")?.Element(A + "fillRef")?.Attribute("idx")) > 0 ? Colour(shape.Element(P + "style")?.Element(A + "fillRef"), source.Palette) : null);

        var body = shape.Element(P + "txBody");
        var blocks = body is null ? null : Paragraphs(body, placeholder, inherited, source);
        if (fill is null && blocks is not { Count: > 0 })
        {
            return;
        }

        var anchor = new[] { body }.Concat(inherited.Select(s => s.Element(P + "txBody")))
            .Select(b => (string?)b?.Element(A + "bodyPr")?.Attribute("anchor")).FirstOrDefault(a => a is not null);
        shapes.Add(Box(transform, frame) with
        {
            Blocks = blocks,
            Fill = fill,
            Anchor = anchor switch { "ctr" => "center", "b" => "bottom", _ => null },
        });
    }

    private List<PreviewBlock> Paragraphs(XElement body, XElement? placeholder, IReadOnlyList<XElement> inherited, Source source)
    {
        var master = source.Master;
        var kind = (string?)placeholder?.Attribute("type");
        var style = placeholder is null ? "otherStyle" : kind is "title" or "ctrTitle" ? "titleStyle" : "bodyStyle";

        // Where a level's defaults can come from, nearest first.
        var levels = new List<XElement?> { body.Element(A + "lstStyle") };
        levels.AddRange(inherited.Select(s => s.Element(P + "txBody")?.Element(A + "lstStyle")));
        levels.Add(master?.Root.Element(P + "txStyles")?.Element(P + style));
        var scale = Number(body.Element(A + "bodyPr")?.Element(A + "normAutofit")?.Attribute("fontScale")) is > 0 and var fontScale ? fontScale / 100000 : 1;

        var blocks = new List<PreviewBlock>();
        var hasText = false;
        foreach (var paragraph in body.Elements(A + "p"))
        {
            var own = paragraph.Element(A + "pPr");
            var level = (int)Math.Clamp(Number(own?.Attribute("lvl")), 0, 8);
            var defaults = levels.Select(l => l?.Element(A + $"lvl{level + 1}pPr")).Where(l => l is not null).Select(l => l!).ToList();
            var chain = new[] { own }.Where(e => e is not null).Select(e => e!).Concat(defaults).ToList();
            var marker = chain.SelectMany(e => e.Elements()).FirstOrDefault(e => e.Name == A + "buNone" || e.Name == A + "buChar" || e.Name == A + "buAutoNum");
            var isList = marker is not null && marker.Name != A + "buNone";

            var runs = new List<PreviewRun>();
            foreach (var child in paragraph.Elements())
            {
                if (child.Name == A + "r" || child.Name == A + "fld")
                {
                    var text = child.Element(A + "t")?.Value;
                    if (!string.IsNullOrEmpty(text))
                    {
                        runs.Add(Run(text, child.Element(A + "rPr"), defaults, scale, kind, source));
                    }
                }
                else if (child.Name == A + "br")
                {
                    runs.Add(new PreviewRun("\n"));
                }
            }

            hasText |= runs.Count > 0;
            blocks.Add(new PreviewBlock
            {
                Runs = runs,
                List = isList && runs.Count > 0 ? level + 1 : null,
                Ordered = isList && marker!.Name == A + "buAutoNum" ? true : null,
                Align = chain.Select(e => (string?)e.Attribute("algn")).FirstOrDefault(a => a is not null) switch
                {
                    "ctr" => "center",
                    "r" => "right",
                    "just" => "justify",
                    _ => null,
                },
            });
        }

        return hasText ? blocks : new List<PreviewBlock>();
    }

    private PreviewRun Run(string text, XElement? properties, IReadOnlyList<XElement> defaults, double scale, string? kind, Source source)
    {
        var inherited = defaults.Select(d => d.Element(A + "defRPr")).Where(d => d is not null).Select(d => d!).ToList();
        var chain = new[] { properties }.Where(p => p is not null).Select(p => p!).Concat(inherited).ToList();
        var hundredths = chain.Select(p => Number(p.Attribute("sz"))).FirstOrDefault(s => s > 0);
        if (hundredths <= 0)
        {
            hundredths = kind is "title" or "ctrTitle" ? 4400 : 1800;
        }

        return new PreviewRun(text)
        {
            Size = Math.Round(hundredths / 100 * scale / (_width / EmuPerPoint) * 100, 3),
            Color = chain.Select(p => Colour(p.Element(A + "solidFill"), source.Palette)).FirstOrDefault(c => c is not null),
            Bold = Toggle(chain, "b"),
            Italic = Toggle(chain, "i"),
            Underline = properties is not null && (string?)properties.Attribute("u") is not (null or "none") ? true : null,
            Strike = properties is not null && (string?)properties.Attribute("strike") is not (null or "noStrike") ? true : null,
        };
    }

    private static bool? Toggle(IEnumerable<XElement> chain, string name) =>
        chain.Select(p => (string?)p.Attribute(name)).FirstOrDefault(v => v is not null) is "1" or "true" ? true : null;

    /// <summary>The shapes a placeholder takes its defaults from: on the slide's layout, then on the master.</summary>
    private static IReadOnlyList<XElement> Inherited(XElement? placeholder, Source source)
    {
        var found = new List<XElement>();
        if (placeholder is null)
        {
            return found;
        }

        var type = (string?)placeholder.Attribute("type");
        var index = (string?)placeholder.Attribute("idx");
        var onLayout = Placeholders(source.Layout).FirstOrDefault(p => Matches(p.Placeholder, type, index));
        if (onLayout.Shape is not null)
        {
            found.Add(onLayout.Shape);
        }

        // On the master only the kinds count: a centred title is a title, a subtitle or content frame is the body.
        var kind = type is "ctrTitle" ? "title" : type is "title" or "dt" or "ftr" or "sldNum" ? type : "body";
        var onMaster = Placeholders(source.Master).FirstOrDefault(p => (string?)p.Placeholder.Attribute("type") == kind);
        if (onMaster.Shape is not null)
        {
            found.Add(onMaster.Shape);
        }

        return found;
    }

    private static IEnumerable<(XElement Shape, XElement Placeholder)> Placeholders(Source? source) =>
        source?.Root.Element(P + "cSld")?.Element(P + "spTree")?.Descendants().Where(e => e.Name == P + "sp" || e.Name == P + "pic")
            .Select(s => (Shape: s, Placeholder: s.Descendants(P + "ph").FirstOrDefault()))
            .Where(s => s.Placeholder is not null)
            .Select(s => (s.Shape, s.Placeholder!))
        ?? Enumerable.Empty<(XElement, XElement)>();

    private static bool Matches(XElement candidate, string? type, string? index)
    {
        if (index is not null)
        {
            return (string?)candidate.Attribute("idx") == index;
        }

        var own = (string?)candidate.Attribute("type");
        return own == type || (own is "ctrTitle" && type is "title") || (own is "title" && type is "ctrTitle");
    }

    private static XElement? Transform(XElement shape) => shape.Element(P + "spPr")?.Element(A + "xfrm");

    private static (double First, double Second) Pair(XElement? element, string first, string second) =>
        (Number(element?.Attribute(first)), Number(element?.Attribute(second)));

    private static double Number(XAttribute? attribute) =>
        double.TryParse((string?)attribute, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
