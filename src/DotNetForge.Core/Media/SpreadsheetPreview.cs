using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DotNetForge.Core.Media;

/// <summary>A .xlsx or .csv as tables of the values a person sees: formulas show their last result.</summary>
internal sealed partial class SpreadsheetPreview
{
    private const int MaxRows = DocumentPreviewReader.MaxRows;
    private const int MaxColumns = DocumentPreviewReader.MaxColumns;
    private const int MaxSheets = 30;
    private const string Part = "xl/workbook.xml";

    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private readonly OfficePackage _package;
    private readonly List<string> _sharedStrings = new();
    private readonly List<string> _cellFormats = new();
    private bool _overflow;
    private DateTime _epoch = new(1899, 12, 30);

    public SpreadsheetPreview(OfficePackage package) => _package = package;

    [GeneratedRegex("^[A-Za-z]+")]
    private static partial Regex ColumnLetters();

    [GeneratedRegex("\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.")]
    private static partial Regex FormatLiterals();

    public DocumentPreview Read()
    {
        var workbook = _package.Xml(Part)?.Root ?? throw new InvalidDataException("A workbook is missing.");
        if ((string?)workbook.Element(S + "workbookPr")?.Attribute("date1904") is "1" or "true")
        {
            _epoch = new DateTime(1904, 1, 1);
        }

        ReadSharedStrings();
        ReadFormats();
        var parts = _package.Relationships(Part).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().Path);
        var sheets = new List<PreviewSheet>();
        foreach (var sheet in workbook.Element(S + "sheets")?.Elements(S + "sheet") ?? Enumerable.Empty<XElement>())
        {
            if ((string?)sheet.Attribute("state") is "hidden" or "veryHidden")
            {
                continue;
            }

            if (sheets.Count == MaxSheets)
            {
                _overflow = true;
                break;
            }

            var id = (string?)sheet.Attribute(R + "id");
            var data = id is not null && parts.TryGetValue(id, out var path) ? _package.Xml(path)?.Root?.Element(S + "sheetData") : null;
            // Charts and pictures float above the cells, in a drawing of their own.
            _overflow |= id is not null && parts.TryGetValue(id, out var part)
                && _package.Relationships(part).Any(r => r.Type.EndsWith("/drawing", StringComparison.Ordinal));
            sheets.Add(new PreviewSheet((string?)sheet.Attribute("name") ?? $"Sheet {sheets.Count + 1}", data is null ? Array.Empty<IReadOnlyList<string>>() : Rows(data)));
        }

        return new DocumentPreview(DocumentPreview.Spreadsheet) { Sheets = sheets, Partial = _overflow };
    }

    private void ReadSharedStrings()
    {
        var root = _package.Xml("xl/sharedStrings.xml")?.Root;
        if (root is not null)
        {
            _sharedStrings.AddRange(root.Elements(S + "si").Select(Text));
        }
    }

    /// <summary>What each cell style shows its number as: <c>d</c> a date, <c>%</c> a percentage, else the number.</summary>
    private void ReadFormats()
    {
        var root = _package.Xml("xl/styles.xml")?.Root;
        if (root is null)
        {
            return;
        }

        var custom = root.Element(S + "numFmts")?.Elements(S + "numFmt")
            .Where(f => f.Attribute("numFmtId") is not null)
            .GroupBy(f => (string)f.Attribute("numFmtId")!).ToDictionary(g => g.Key, g => (string?)g.First().Attribute("formatCode") ?? string.Empty)
            ?? new Dictionary<string, string>();
        foreach (var format in root.Element(S + "cellXfs")?.Elements(S + "xf") ?? Enumerable.Empty<XElement>())
        {
            var id = (string?)format.Attribute("numFmtId") ?? "0";
            _cellFormats.Add(custom.TryGetValue(id, out var code) ? FormatKind(code)
                : int.TryParse(id, out var builtIn) && builtIn is (>= 14 and <= 22) or (>= 45 and <= 47) ? "d"
                : builtIn is 9 or 10 ? "%"
                : string.Empty);
        }
    }

    private static string FormatKind(string code)
    {
        var plain = FormatLiterals().Replace(code, string.Empty);
        return plain.Contains('%') ? "%" : plain.Any(c => "ymdhsYMDHS".Contains(c)) ? "d" : string.Empty;
    }

    private List<IReadOnlyList<string>> Rows(XElement data)
    {
        var rows = new List<IReadOnlyList<string>>();
        var number = 0;
        foreach (var row in data.Elements(S + "row"))
        {
            number = int.TryParse((string?)row.Attribute("r"), out var r) && r > number ? r : number + 1;
            if (number > MaxRows)
            {
                _overflow = true;
                break;
            }

            var cells = new List<string>();
            foreach (var cell in row.Elements(S + "c"))
            {
                var column = Column((string?)cell.Attribute("r"), cells.Count);
                if (column >= MaxColumns)
                {
                    _overflow = true;
                    continue;
                }

                while (cells.Count < column)
                {
                    cells.Add(string.Empty);
                }

                cells.Add(Value(cell));
            }

            // Gaps between rows stay, so a row's place on the screen matches its number in the file.
            while (rows.Count < number - 1)
            {
                rows.Add(Array.Empty<string>());
            }

            rows.Add(cells);
        }

        return Trim(rows);
    }

    private string Value(XElement cell)
    {
        var raw = cell.Element(S + "v")?.Value;
        switch ((string?)cell.Attribute("t"))
        {
            case "s":
                return int.TryParse(raw, out var index) && index >= 0 && index < _sharedStrings.Count ? _sharedStrings[index] : string.Empty;
            case "inlineStr":
                return cell.Element(S + "is") is { } inline ? Text(inline) : string.Empty;
            case "b":
                return raw == "1" ? "TRUE" : "FALSE";
            case "str" or "e":
                return raw ?? string.Empty;
        }

        if (raw is null || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return raw ?? string.Empty;
        }

        var format = int.TryParse((string?)cell.Attribute("s"), out var style) && style >= 0 && style < _cellFormats.Count ? _cellFormats[style] : string.Empty;
        return format switch
        {
            "%" => (number * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%",
            "d" when number is >= 0 and < 2958466 => Date(number),
            _ => number.ToString("G15", CultureInfo.InvariantCulture),
        };
    }

    private string Date(double serial)
    {
        var date = _epoch.AddDays(serial);
        return date.TimeOfDay == TimeSpan.Zero ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Phonetic guides (<c>rPh</c>) are reading aids for the text, not part of it.</summary>
    private static string Text(XElement item) =>
        string.Concat(item.Descendants(S + "t").Where(t => t.Parent?.Name != S + "rPh").Select(t => t.Value));

    private static int Column(string? reference, int next)
    {
        var letters = reference is null ? null : ColumnLetters().Match(reference).Value;
        if (string.IsNullOrEmpty(letters))
        {
            return next;
        }

        var column = 0;
        foreach (var letter in letters)
        {
            column = Math.Min(column * 26 + char.ToUpperInvariant(letter) - 'A' + 1, MaxColumns + 1);
        }

        return column - 1;
    }

    private static List<IReadOnlyList<string>> Trim(List<IReadOnlyList<string>> rows)
    {
        while (rows.Count > 0 && rows[^1].All(string.IsNullOrEmpty))
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return rows;
    }

    /// <summary>
    /// A CSV as one sheet. The separator is whichever of comma, semicolon and tab the first line uses most: spreadsheets
    /// in much of Europe save semicolons.
    /// </summary>
    internal static DocumentPreview ReadCsv(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var overflow = false;
        var rows = new List<IReadOnlyList<string>>();
        var buffer = new char[4 * 1024 * 1024];
        var length = reader.ReadBlock(buffer, 0, buffer.Length);
        var text = buffer.AsSpan(0, length);
        if (!reader.EndOfStream)
        {
            overflow = true;
        }

        var firstLine = text.IndexOfAny('\r', '\n') is var end and >= 0 ? text[..end] : text;
        var separator = ',';
        foreach (var candidate in new[] { ';', '\t' })
        {
            if (Count(firstLine, candidate) > Count(firstLine, separator))
            {
                separator = candidate;
            }
        }

        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < length; i++)
        {
            var c = buffer[i];
            if (quoted)
            {
                if (c != '"')
                {
                    cell.Append(c);
                }
                else if (i + 1 < length && buffer[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (c == '"' && cell.Length == 0)
            {
                quoted = true;
            }
            else if (c == separator)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < length && buffer[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row.Take(MaxColumns).ToList());
                overflow |= row.Count > MaxColumns;
                row = new List<string>();
                if (rows.Count == MaxRows)
                {
                    return Csv(rows, overflow || i + 1 < length);
                }
            }
            else
            {
                cell.Append(c);
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row.Take(MaxColumns).ToList());
            overflow |= row.Count > MaxColumns;
        }

        return Csv(rows, overflow);
    }

    private static DocumentPreview Csv(List<IReadOnlyList<string>> rows, bool overflow) =>
        new(DocumentPreview.Spreadsheet) { Sheets = new[] { new PreviewSheet("CSV", Trim(rows)) }, Partial = overflow };

    private static int Count(ReadOnlySpan<char> text, char value)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (c == value)
            {
                count++;
            }
        }

        return count;
    }
}
