using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DotNetForge.Core.Media;
using Xunit;

namespace DotNetForge.Tests.Media;

/// <summary>
/// The media preview's reader (.docs/pages/media.md#preview): the samples in tests/fixtures/documents come from other
/// software (generate.py there), and the hand-built packages are the hostile files such software wouldn't write.
/// </summary>
public sealed class DocumentPreviewReaderTests
{
    private const string WordXmlHead = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>";

    private static string Fixtures([CallerFilePath] string file = "") =>
        Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "fixtures", "documents");

    private static DocumentPreview Sample(string name, string type)
    {
        using var file = File.OpenRead(Path.Combine(Fixtures(), name));
        return DocumentPreviewReader.Read(file, type);
    }

    private static MemoryStream Package(params (string Path, byte[] Content)[] parts)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in parts)
            {
                using var entry = zip.CreateEntry(path).Open();
                entry.Write(content);
            }
        }

        buffer.Position = 0;
        return buffer;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static string Text(PreviewBlock block) => string.Concat(block.Runs!.Select(r => r.Text));

    [Fact]
    public void A_word_document_keeps_its_headings_formatting_lists_tables_and_images()
    {
        var preview = Sample("sample.docx", DocumentPreviewReader.WordType);
        var blocks = preview.Blocks!;

        Assert.Equal(DocumentPreview.Document, preview.Kind);
        Assert.False(preview.Partial);
        Assert.Equal(1, blocks.Single(b => b.Runs is not null && Text(b) == "Quarterly report").Heading);
        Assert.Equal(1, blocks.Single(b => b.Runs is not null && Text(b) == "Summary").Heading);

        var paragraph = blocks.Single(b => b.Runs is not null && Text(b).StartsWith("Sales grew", StringComparison.Ordinal));
        Assert.Equal("Sales grew twelve percent in the third quarter.", Text(paragraph));
        Assert.True(paragraph.Runs!.Single(r => r.Text == "twelve percent").Bold);
        Assert.True(paragraph.Runs!.Single(r => r.Text == "third").Italic);

        var bullet = blocks.Single(b => b.Runs is not null && Text(b) == "First point");
        Assert.Equal(1, bullet.List);
        Assert.Null(bullet.Ordered);
        Assert.True(blocks.Single(b => b.Runs is not null && Text(b) == "Step one").Ordered);

        Assert.Equal(new[] { "North", "1200" }, blocks.Single(b => b.Rows is not null).Rows![1]);
        var image = blocks.Single(b => b.Src is not null);
        Assert.StartsWith("data:image/png;base64,", image.Src);
        Assert.Equal(96, image.Width); // one inch in the document, whatever the file's size
    }

    [Fact]
    public void A_workbook_shows_what_a_person_sees_on_its_visible_sheets()
    {
        var preview = Sample("sample.xlsx", DocumentPreviewReader.ExcelType);

        Assert.Equal(new[] { "Sales", "Notes" }, preview.Sheets!.Select(s => s.Name));
        Assert.Equal(new[] { "North", "1200.5", "25%", "2026-03-31" }, preview.Sheets![0].Rows[1]);

        // Gaps stay: "Far corner" is in row 3, column C of its sheet.
        var notes = preview.Sheets![1].Rows;
        Assert.Equal(3, notes.Count);
        Assert.Equal(new[] { string.Empty, string.Empty, "Far corner" }, notes[2]);
    }

    [Fact]
    public void A_presentation_inherits_positions_and_sizes_from_its_layout_and_master()
    {
        var preview = Sample("sample.pptx", DocumentPreviewReader.PowerPointType);
        var slides = preview.Slides!;

        Assert.Equal(4, slides.Count); // the hidden slide isn't shown
        Assert.True(preview.SlideRatio is > 1.3 and < 1.4); // the template's 4:3
        Assert.True(preview.Partial); // the chart

        // The title slide's text has no position or size of its own: both come from the layout and the master.
        var title = slides[0].Shapes.Single(s => s.Blocks is not null && Text(s.Blocks[0]) == "Quarterly review");
        Assert.True(title.Width is > 50 and <= 100, $"Title width {title.Width}");
        Assert.True(title.Y is > 10 and < 50, $"Title top {title.Y}");
        var subtitle = slides[0].Shapes.Single(s => s.Blocks is not null && Text(s.Blocks[0]) == "Third quarter");
        Assert.True(title.Blocks![0].Runs![0].Size > subtitle.Blocks![0].Runs![0].Size);

        // The body's levels come from the master's bullets.
        var body = slides[1].Shapes.Single(s => s.Blocks is { Count: 3 }).Blocks!;
        Assert.Equal(1, body[0].List);
        Assert.Equal(2, body[2].List);

        Assert.Contains(slides[3].Shapes, s => s.Blocks?[0].Runs?[0].Text == "Chart not shown");

        var blank = slides[2];
        Assert.Equal("102030", blank.Background);
        var box = blank.Shapes.Single(s => s.Fill == "FF6600");
        Assert.Equal("FFFFFF", box.Blocks![0].Runs![0].Color);
        Assert.Contains(blank.Shapes, s => s.Src?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true);
        Assert.Equal(new[] { "North", "1200" }, blank.Shapes.Single(s => s.Rows is not null).Rows![1]);
    }

    [Theory]
    [InlineData("name,note\nAda,\"line one\nline two\"\n", "Ada", "line one\nline two")]
    [InlineData("name;note\r\nAda;\"says \"\"hi\"\"\"\r\n", "Ada", "says \"hi\"")]
    [InlineData("name\tnote\nAda\t1,5\n", "Ada", "1,5")]
    public void A_csv_finds_its_separator_and_keeps_quoted_text(string csv, string first, string second)
    {
        var preview = DocumentPreviewReader.Read(new MemoryStream(Utf8(csv)), DocumentPreviewReader.CsvType);
        Assert.Equal(new[] { first, second }, preview.Sheets![0].Rows[1]);
    }

    [Fact]
    public void A_long_csv_is_cut_off_and_says_so()
    {
        var csv = string.Concat(Enumerable.Repeat("a,b\n", DocumentPreviewReader.MaxRows + 10));
        var preview = DocumentPreviewReader.Read(new MemoryStream(Utf8(csv)), DocumentPreviewReader.CsvType);
        Assert.Equal(DocumentPreviewReader.MaxRows, preview.Sheets![0].Rows.Count);
        Assert.True(preview.Partial);
    }

    [Fact]
    public void Script_in_a_document_stays_text()
    {
        const string script = "<script>alert(1)</script>";
        var body = $"{WordXmlHead}<w:p><w:r><w:t>{System.Security.SecurityElement.Escape(script)}</w:t></w:r></w:p></w:body></w:document>";
        var preview = DocumentPreviewReader.Read(Package(("word/document.xml", Utf8(body))), DocumentPreviewReader.WordType);
        Assert.Equal(script, Text(preview.Blocks![0])); // data for a text node; the page never parses it as HTML
    }

    [Fact]
    public void An_xml_entity_attack_is_refused()
    {
        const string attack = "<!DOCTYPE lolz [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;\">]>";
        var body = $"{attack}{WordXmlHead}<w:p><w:r><w:t>&b;</w:t></w:r></w:p></w:body></w:document>";
        Assert.Throws<InvalidDataException>(() => DocumentPreviewReader.Read(Package(("word/document.xml", Utf8(body))), DocumentPreviewReader.WordType));
    }

    [Fact]
    public void A_part_that_expands_past_the_limit_is_refused()
    {
        // Compresses to almost nothing, like a zip bomb.
        var bomb = new byte[DocumentPreviewReader.MaxPartCharacters + 1024];
        Array.Fill(bomb, (byte)' ');
        Assert.Throws<InvalidDataException>(() => DocumentPreviewReader.Read(Package(("word/document.xml", bomb)), DocumentPreviewReader.WordType));
    }

    [Theory]
    [InlineData("not a zip archive")]
    [InlineData("")]
    public void A_file_that_is_no_archive_is_refused(string content)
    {
        Assert.Throws<InvalidDataException>(() => DocumentPreviewReader.Read(new MemoryStream(Utf8(content)), DocumentPreviewReader.PowerPointType));
    }

    [Fact]
    public void Images_are_taken_only_from_inside_the_file_and_only_as_raster_images()
    {
        const string relationships = """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="image" Target="media/ok.png" />
              <Relationship Id="rId2" Type="image" Target="https://tracker.example/pixel.png" TargetMode="External" />
              <Relationship Id="rId3" Type="image" Target="media/script.svg" />
              <Relationship Id="rId4" Type="image" Target="../../../secret.png" />
            </Relationships>
            """;
        var images = string.Concat(Enumerable.Range(1, 4).Select(i => $"""<a:blip xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:embed="rId{i}" />"""));
        var body = $"{WordXmlHead}<w:p><w:r><w:drawing>{images}</w:drawing></w:r></w:p></w:body></w:document>";

        var preview = DocumentPreviewReader.Read(Package(
            ("word/document.xml", Utf8(body)),
            ("word/_rels/document.xml.rels", Utf8(relationships)),
            ("word/media/ok.png", new byte[] { 1, 2, 3 }),
            ("word/media/script.svg", Utf8("<svg onload=\"alert(1)\" />")),
            ("secret.png", new byte[] { 4 })), DocumentPreviewReader.WordType);

        Assert.Equal("data:image/png;base64,AQID", Assert.Single(preview.Blocks!).Src);
        Assert.True(preview.Partial); // the SVG was left out
    }

    [Fact]
    public void An_image_over_the_limit_is_left_out_and_noted()
    {
        const string relationships = """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="image" Target="media/big.png" /></Relationships>""";
        const string image = """<a:blip xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:embed="rId1" />""";
        var body = $"{WordXmlHead}<w:p><w:r><w:drawing>{image}</w:drawing></w:r></w:p></w:body></w:document>";

        var preview = DocumentPreviewReader.Read(Package(
            ("word/document.xml", Utf8(body)),
            ("word/_rels/document.xml.rels", Utf8(relationships)),
            ("word/media/big.png", RandomNumberGenerator.GetBytes(DocumentPreviewReader.MaxImageBytes + 1))), DocumentPreviewReader.WordType);

        Assert.Empty(preview.Blocks!);
        Assert.True(preview.Partial);
    }

    [Fact]
    public void Deleted_tracked_text_is_not_shown()
    {
        var body = $"{WordXmlHead}<w:p><w:r><w:t>Kept</w:t></w:r><w:del><w:r><w:delText>Removed</w:delText></w:r></w:del><w:ins><w:r><w:t> and added</w:t></w:r></w:ins></w:p></w:body></w:document>";
        var preview = DocumentPreviewReader.Read(Package(("word/document.xml", Utf8(body))), DocumentPreviewReader.WordType);
        Assert.Equal("Kept and added", Text(preview.Blocks![0]));
    }

    [Fact]
    public void A_formula_shows_its_last_result_and_hidden_sheets_stay_hidden()
    {
        const string workbook = """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="One" sheetId="1" r:id="rId1" /><sheet name="Secret" sheetId="2" state="hidden" r:id="rId2" /></sheets>
            </workbook>
            """;
        const string relationships = """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="worksheet" Target="worksheets/sheet1.xml" />
              <Relationship Id="rId2" Type="worksheet" Target="worksheets/sheet2.xml" />
            </Relationships>
            """;
        const string drawing = """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml" /></Relationships>""";
        const string sheet = """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1"><f>1+1</f><v>2</v></c><c r="B1" t="b"><v>1</v></c><c r="C1" t="inlineStr"><is><t>Inline</t></is></c></row></sheetData></worksheet>""";

        var preview = DocumentPreviewReader.Read(Package(
            ("xl/workbook.xml", Utf8(workbook)),
            ("xl/_rels/workbook.xml.rels", Utf8(relationships)),
            ("xl/worksheets/sheet1.xml", Utf8(sheet)),
            ("xl/worksheets/_rels/sheet1.xml.rels", Utf8(drawing)),
            ("xl/worksheets/sheet2.xml", Utf8(sheet))), DocumentPreviewReader.ExcelType);

        Assert.Equal("One", Assert.Single(preview.Sheets!).Name);
        Assert.Equal(new[] { "2", "TRUE", "Inline" }, preview.Sheets![0].Rows[0]);
        Assert.True(preview.Partial); // its chart or picture isn't shown
    }
}
