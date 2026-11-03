using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace DotNetForge.Core.Media;

/// <summary>
/// Reads an uploaded document for the media preview (.docs/pages/media.md#preview). Uploads are untrusted, so the
/// reader only takes text and raster images out of them: no DTDs, no external references, and limits on every part.
/// A file that can't be read throws <see cref="InvalidDataException"/>.
/// </summary>
public static class DocumentPreviewReader
{
    public const string WordType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string ExcelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PowerPointType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    public const string CsvType = "text/csv";

    /// <summary>The largest XML part: a small, heavily compressed part can't expand without bound.</summary>
    public const long MaxPartCharacters = 32L * 1024 * 1024;

    public const int MaxImageBytes = 4 * 1024 * 1024;

    /// <summary>All images of one preview together; they travel base64-encoded in one response.</summary>
    public const int MaxImagesBytes = 16 * 1024 * 1024;

    /// <summary>Rows shown of one sheet.</summary>
    public const int MaxRows = 2000;

    /// <summary>Columns shown of one sheet.</summary>
    public const int MaxColumns = 100;

    public static bool CanRead(string contentType) => contentType is WordType or ExcelType or PowerPointType or CsvType;

    /// <param name="content">A seekable stream: Office files are ZIP archives.</param>
    /// <param name="contentType">One of the types <see cref="CanRead"/> accepts.</param>
    public static DocumentPreview Read(Stream content, string contentType)
    {
        try
        {
            if (contentType == CsvType)
            {
                return SpreadsheetPreview.ReadCsv(content);
            }

            using var package = new OfficePackage(content);
            return contentType switch
            {
                WordType => new WordPreview(package).Read(),
                ExcelType => new SpreadsheetPreview(package).Read(),
                PowerPointType => new SlidePreview(package).Read(),
                _ => throw new InvalidDataException($"No preview for {contentType}."),
            };
        }
        catch (XmlException e)
        {
            throw new InvalidDataException("The document's XML can't be read.", e);
        }
    }
}

/// <summary>A relationship of an Office part: its id, its type, and the full path of its target in the archive.</summary>
internal sealed record OfficeRelationship(string Id, string Type, string Path);

/// <summary>An Office file: a ZIP archive of XML parts and media, read with the limits of an untrusted file.</summary>
internal sealed class OfficePackage : IDisposable
{
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = DocumentPreviewReader.MaxPartCharacters,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    // SVG is left out on purpose: it is a document that can carry script, not a raster image.
    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
    };

    private readonly ZipArchive _zip;
    private int _imageBytes;

    public OfficePackage(Stream content) => _zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

    /// <summary>An image was left out: its format, its size or the budget of the whole preview.</summary>
    public bool ImagesOmitted { get; private set; }

    public void Dispose() => _zip.Dispose();

    public XDocument? Xml(string path)
    {
        var entry = _zip.GetEntry(path);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, Settings);
        return XDocument.Load(reader);
    }

    /// <summary>The internal relationships of a part. External ones are skipped: a preview never fetches anything.</summary>
    public IReadOnlyList<OfficeRelationship> Relationships(string path)
    {
        var slash = path.LastIndexOf('/');
        var file = slash < 0 ? $"_rels/{path}.rels" : $"{path[..slash]}/_rels/{path[(slash + 1)..]}.rels";
        var xml = Xml(file);
        if (xml?.Root is null)
        {
            return Array.Empty<OfficeRelationship>();
        }

        XNamespace ns = RelationshipNamespace;
        return xml.Root.Elements(ns + "Relationship")
            .Where(r => !string.Equals((string?)r.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
            .Select(r => (Id: (string?)r.Attribute("Id"), Type: (string?)r.Attribute("Type"), Path: Resolve(path, (string?)r.Attribute("Target"))))
            .Where(r => r.Id is not null && r.Type is not null && r.Path is not null)
            .Select(r => new OfficeRelationship(r.Id!, r.Type!, r.Path!))
            .ToList();
    }

    /// <summary>The image at <paramref name="path"/> as a <c>data:</c> URI, or null when it can't be shown.</summary>
    public string? Image(string? path)
    {
        var entry = path is null ? null : _zip.GetEntry(path);
        if (entry is null)
        {
            return null;
        }

        if (!ImageTypes.TryGetValue(Path.GetExtension(entry.Name), out var type) || entry.Length > DocumentPreviewReader.MaxImageBytes
            || _imageBytes + entry.Length > DocumentPreviewReader.MaxImagesBytes)
        {
            ImagesOmitted = true;
            return null;
        }

        // The sizes in a ZIP's directory are only claims: the read stops at the limit whatever the entry says.
        using var stream = entry.Open();
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            bytes.Write(buffer, 0, read);
            if (bytes.Length > DocumentPreviewReader.MaxImageBytes)
            {
                ImagesOmitted = true;
                return null;
            }
        }

        _imageBytes += (int)bytes.Length;
        return $"data:{type};base64,{Convert.ToBase64String(bytes.GetBuffer(), 0, (int)bytes.Length)}";
    }

    /// <summary>A relationship target as a full path; null when it climbs out of the archive.</summary>
    internal static string? Resolve(string part, string? target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return null;
        }

        var parts = new List<string>();
        var slash = part.LastIndexOf('/');
        if (!target.StartsWith('/') && slash >= 0)
        {
            parts.AddRange(part[..slash].Split('/'));
        }

        foreach (var segment in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (parts.Count == 0)
                {
                    return null;
                }

                parts.RemoveAt(parts.Count - 1);
            }
            else if (segment != ".")
            {
                parts.Add(segment);
            }
        }

        return string.Join('/', parts);
    }
}
