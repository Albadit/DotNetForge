using DotNetForge.Shared.Enums;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Core.Content;

/// <summary>A ready-made content type the "New content type" dialog offers.</summary>
/// <param name="Requires">Templates whose types this one links to; they are created first when missing.</param>
public sealed record ContentTypeTemplate(
    string Id, string Name, string Slug, string Description, string Icon, ContentTypeKind Kind, string TitleField,
    IReadOnlyList<string> Requires, Func<Func<string, Guid>, JArray> Fields);

/// <summary>
/// Starter content types (.docs/features/structured-content.md#starter-templates). They are ordinary types once created:
/// every field can be changed or removed. A template's relationship fields name another template by slug; creating
/// the type creates that one too when no type has that API id.
/// </summary>
public static class ContentTypeTemplates
{
    public static readonly IReadOnlyList<ContentTypeTemplate> All = new[]
    {
        new ContentTypeTemplate("blog-posts", "Blog posts", "blog-posts", "Articles with a picture, a category and an author.", "pages",
            ContentTypeKind.Collection, "title", new[] { "categories", "authors" }, type => Fields(
                Text("title", "Title", required: true, list: true),
                Slug("slug", "Slug", "title"),
                Field("excerpt", "Excerpt", FieldTypes.Textarea, o => o["maxLength"] = 300),
                Field("body", "Body", FieldTypes.RichText),
                Field("featuredImage", "Featured image", FieldTypes.Image),
                Relation("category", "Category", type("categories"), list: true),
                Relation("author", "Author", type("authors")),
                Field("publishedOn", "Published on", FieldTypes.Date, o => o["showInList"] = true))),
        new ContentTypeTemplate("categories", "Categories", "categories", "Names to group other content by.", "tag",
            ContentTypeKind.Collection, "name", Array.Empty<string>(), _ => Fields(
                Text("name", "Name", required: true, list: true),
                Slug("slug", "Slug", "name"),
                Field("description", "Description", FieldTypes.Textarea))),
        new ContentTypeTemplate("authors", "Authors", "authors", "People who write or present content.", "users",
            ContentTypeKind.Collection, "name", Array.Empty<string>(), _ => Fields(
                Text("name", "Name", required: true, list: true),
                Field("role", "Role", FieldTypes.Text, o => o["showInList"] = true),
                Field("photo", "Photo", FieldTypes.Image),
                Field("bio", "Bio", FieldTypes.RichText))),
        new ContentTypeTemplate("products", "Products", "products", "Things you sell, with a price, pictures and options.", "store",
            ContentTypeKind.Collection, "name", Array.Empty<string>(), _ => Fields(
                Text("name", "Name", required: true, list: true),
                Slug("slug", "Slug", "name"),
                Field("price", "Price", FieldTypes.Number, o => { o["required"] = true; o["min"] = 0; o["decimals"] = 2; o["showInList"] = true; o["width"] = "half"; }),
                Field("sku", "SKU", FieldTypes.Text, o => { o["unique"] = true; o["width"] = "half"; }),
                Field("image", "Product image", FieldTypes.Image),
                Field("gallery", "Gallery", FieldTypes.Image, o => o["multiple"] = true),
                Field("description", "Description", FieldTypes.RichText),
                Field("inStock", "In stock", FieldTypes.Boolean, o => { o["default"] = true; o["showInList"] = true; }),
                Field("features", "Features", FieldTypes.Array),
                Field("variants", "Variants", FieldTypes.Repeater, o => o["fields"] = Fields(
                    Text("name", "Name", required: true),
                    Field("price", "Price", FieldTypes.Number, p => { p["min"] = 0; p["decimals"] = 2; }))))),
        new ContentTypeTemplate("events", "Events", "events", "Dates and places people can attend.", "calendar",
            ContentTypeKind.Collection, "title", Array.Empty<string>(), _ => Fields(
                Text("title", "Title", required: true, list: true),
                Slug("slug", "Slug", "title"),
                Field("startsAt", "Starts", FieldTypes.Date, o => { o["withTime"] = true; o["required"] = true; o["showInList"] = true; o["width"] = "half"; }),
                Field("endsAt", "Ends", FieldTypes.Date, o => { o["withTime"] = true; o["width"] = "half"; }),
                Field("location", "Location", FieldTypes.Text, o => o["showInList"] = true),
                Field("image", "Image", FieldTypes.Image),
                Field("description", "Description", FieldTypes.RichText),
                Field("registrationUrl", "Registration link", FieldTypes.Url))),
        new ContentTypeTemplate("projects", "Projects", "projects", "Portfolio work with a client, pictures and a link.", "briefcase",
            ContentTypeKind.Collection, "title", Array.Empty<string>(), _ => Fields(
                Text("title", "Title", required: true, list: true),
                Slug("slug", "Slug", "title"),
                Field("client", "Client", FieldTypes.Text, o => o["showInList"] = true),
                Field("year", "Year", FieldTypes.Number, o => { o["integer"] = true; o["width"] = "half"; o["showInList"] = true; }),
                Field("cover", "Cover image", FieldTypes.Image),
                Field("summary", "Summary", FieldTypes.Textarea),
                Field("body", "Story", FieldTypes.RichText),
                Field("link", "Website", FieldTypes.Url))),
        new ContentTypeTemplate("testimonials", "Testimonials", "testimonials", "Quotes from happy customers.", "message",
            ContentTypeKind.Collection, "name", Array.Empty<string>(), _ => Fields(
                Field("quote", "Quote", FieldTypes.Textarea, o => o["required"] = true),
                Text("name", "Name", required: true, list: true),
                Field("company", "Company", FieldTypes.Text, o => o["showInList"] = true),
                Field("photo", "Photo", FieldTypes.Image),
                Field("rating", "Rating", FieldTypes.Number, o => { o["integer"] = true; o["min"] = 1; o["max"] = 5; o["showInList"] = true; }))),
        new ContentTypeTemplate("faq", "FAQ", "faq", "Questions and answers.", "help",
            ContentTypeKind.Collection, "question", Array.Empty<string>(), _ => Fields(
                Text("question", "Question", required: true, list: true),
                Field("answer", "Answer", FieldTypes.RichText, o => o["required"] = true),
                Field("topic", "Topic", FieldTypes.Select, o => { o["showInList"] = true; o["choices"] = Choices("General", "Billing", "Support"); }))),
        new ContentTypeTemplate("site-settings", "Site settings", "site-settings", "One set of values used across the site (a single type).", "settings",
            ContentTypeKind.Single, "siteName", Array.Empty<string>(), _ => Fields(
                Text("siteName", "Site name", required: true),
                Field("tagline", "Tagline", FieldTypes.Text),
                Field("logo", "Logo", FieldTypes.Image),
                Field("contact", "Contact", FieldTypes.Group, o => o["fields"] = Fields(
                    Field("email", "Email", FieldTypes.Email, p => p["width"] = "half"),
                    Field("phone", "Phone", FieldTypes.Text, p => p["width"] = "half"),
                    Field("address", "Address", FieldTypes.Textarea))),
                Field("social", "Social links", FieldTypes.Repeater, o => o["fields"] = Fields(
                    Text("network", "Network", required: true),
                    Field("url", "Link", FieldTypes.Url, p => p["required"] = true))))),
    };

    public static ContentTypeTemplate? Find(string? id) => All.FirstOrDefault(t => t.Id == id);

    private static JArray Fields(params JObject[] fields) => new(fields);

    private static JObject Field(string key, string label, string type, Action<JObject>? configure = null)
    {
        var o = new JObject { ["id"] = ContentSchema.NewId(), ["key"] = key, ["label"] = label, ["type"] = type };
        configure?.Invoke(o);
        return o;
    }

    private static JObject Text(string key, string label, bool required = false, bool list = false) =>
        Field(key, label, FieldTypes.Text, o =>
        {
            if (required)
            {
                o["required"] = true;
            }

            if (list)
            {
                o["showInList"] = true;
            }
        });

    private static JObject Slug(string key, string label, string from) =>
        Field(key, label, FieldTypes.Slug, o => { o["unique"] = true; o["slugFrom"] = from; o["help"] = "Used in links, e.g. /blog/my-post."; });

    private static JObject Relation(string key, string label, Guid target, bool list = false) =>
        Field(key, label, FieldTypes.Relationship, o =>
        {
            o["relationTo"] = target.ToString();
            if (list)
            {
                o["showInList"] = true;
            }
        });

    private static JArray Choices(params string[] values) => new(values.Select(v => new JObject { ["value"] = v.ToLowerInvariant(), ["label"] = v }));
}
