namespace DotNetForge.Core.Media;

/// <summary>
/// What the media preview shows of a Word, Excel, PowerPoint or CSV file (.docs/pages/media.md#preview): plain data
/// the browser draws with text nodes, so nothing in a file can run. <see cref="Partial"/> says something was left out
/// (too much content, a chart, an image over the limits).
/// </summary>
public sealed record DocumentPreview(string Kind)
{
    public const string Document = "document";
    public const string Spreadsheet = "spreadsheet";
    public const string Presentation = "presentation";

    public IReadOnlyList<PreviewBlock>? Blocks { get; init; }
    public IReadOnlyList<PreviewSheet>? Sheets { get; init; }
    public IReadOnlyList<PreviewSlide>? Slides { get; init; }

    /// <summary>Slide width divided by height.</summary>
    public double? SlideRatio { get; init; }
    public bool Partial { get; init; }
}

/// <summary>A paragraph (<see cref="Runs"/>), a table (<see cref="Rows"/>) or an image (<see cref="Src"/>).</summary>
public sealed record PreviewBlock
{
    public IReadOnlyList<PreviewRun>? Runs { get; init; }
    public IReadOnlyList<IReadOnlyList<string>>? Rows { get; init; }

    /// <summary>A <c>data:</c> URI of a raster image.</summary>
    public string? Src { get; init; }

    /// <summary>The width an image is shown at, in CSS pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Heading level, 1 to 6.</summary>
    public int? Heading { get; init; }

    /// <summary>List level, from 1.</summary>
    public int? List { get; init; }
    public bool? Ordered { get; init; }

    /// <summary><c>center</c>, <c>right</c> or <c>justify</c>.</summary>
    public string? Align { get; init; }
}

public sealed record PreviewRun(string Text)
{
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }
    public bool? Strike { get; init; }

    /// <summary>On a slide: the font size as a percentage of the slide's width, so text scales with it.</summary>
    public double? Size { get; init; }

    /// <summary>Six hex digits, no <c>#</c>.</summary>
    public string? Color { get; init; }
}

public sealed record PreviewSheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed record PreviewSlide(IReadOnlyList<PreviewShape> Shapes)
{
    /// <summary>Six hex digits, no <c>#</c>.</summary>
    public string? Background { get; init; }
    public string? BackgroundImage { get; init; }
}

/// <summary>A shape on a slide; the box is in percent of the slide's width and height.</summary>
public sealed record PreviewShape(double X, double Y, double Width, double Height)
{
    public IReadOnlyList<PreviewBlock>? Blocks { get; init; }
    public IReadOnlyList<IReadOnlyList<string>>? Rows { get; init; }
    public string? Src { get; init; }

    /// <summary>Six hex digits, no <c>#</c>.</summary>
    public string? Fill { get; init; }

    /// <summary><c>center</c> or <c>bottom</c>; the top when empty.</summary>
    public string? Anchor { get; init; }
}
