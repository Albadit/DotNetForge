using System.Text.RegularExpressions;
using DotNetForge.Abstractions.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DotNetForge.Data.Database;

/// <summary>A field of a <see cref="CollectionMap"/>: the name callers use and the name the database uses.</summary>
/// <param name="Name">Name in commands and results (the model property name).</param>
/// <param name="Native">Column or document element name.</param>
/// <param name="ClrType">Model type values are coerced to (<see cref="object"/> for unmapped databases).</param>
/// <param name="Property">The EF property, when the collection comes from the CMS model.</param>
public sealed record FieldMap(string Name, string Native, Type ClrType, bool IsKey, IReadOnlyProperty? Property)
{
    /// <summary>Key the database or the provider generates when an insert omits it.</summary>
    public bool IsGeneratedKey => IsKey && Property?.ValueGenerated == ValueGenerated.OnAdd;
}

/// <summary>
/// A table or collection as a command sees it. For the main database every name is checked against the CMS model, so
/// commands can only touch tables and fields that exist; additional databases have no model and accept any name that
/// passes <see cref="DatabaseSchema.EnsureIdentifier"/>.
/// </summary>
public sealed class CollectionMap
{
    private readonly Dictionary<string, FieldMap> _fields;

    public CollectionMap(string name, string native, string? schema, IReadOnlyList<FieldMap> fields, IReadOnlyEntityType? entityType)
    {
        Name = name;
        Native = native;
        Schema = schema;
        Fields = fields;
        EntityType = entityType;
        _fields = fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        Key = fields.Where(f => f.IsKey).ToList();
    }

    public string Name { get; }

    public string Native { get; }

    public string? Schema { get; }

    /// <summary>All fields in model order (unmapped collections: empty, fields are created on demand).</summary>
    public IReadOnlyList<FieldMap> Fields { get; }

    public IReadOnlyList<FieldMap> Key { get; }

    public IReadOnlyEntityType? EntityType { get; }

    public bool IsMapped => EntityType is not null;

    /// <summary>The field called <paramref name="name"/>; unknown names fail with <see cref="DatabaseNotFoundException"/>.</summary>
    public FieldMap Field(string name)
    {
        if (_fields.TryGetValue(name, out var field))
        {
            return field;
        }

        if (IsMapped)
        {
            throw new DatabaseNotFoundException($"'{Name}' has no field '{name}'.");
        }

        DatabaseSchema.EnsureIdentifier(name, "field");
        return new FieldMap(name, name, typeof(object), IsKey: false, Property: null);
    }
}

/// <summary>Resolves command collection names to <see cref="CollectionMap"/>s for one provider's naming.</summary>
public sealed partial class DatabaseSchema
{
    private readonly Dictionary<string, CollectionMap>? _mapped;

    private DatabaseSchema(Dictionary<string, CollectionMap>? mapped) => _mapped = mapped;

    /// <summary>
    /// Schema backed by the CMS model. <paramref name="collectionName"/> and <paramref name="fieldName"/> give the
    /// provider's native names (table/column, collection/element). Collections are found by native name
    /// (<c>Users</c>) or entity name (<c>User</c>).
    /// </summary>
    public static DatabaseSchema FromModel(
        IModel model,
        Func<IReadOnlyEntityType, (string Name, string? Schema)> collectionName,
        Func<IReadOnlyEntityType, IReadOnlyProperty, string> fieldName)
    {
        var maps = new Dictionary<string, CollectionMap>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityType in model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            var (native, schema) = collectionName(entityType);
            var key = entityType.FindPrimaryKey()?.Properties ?? (IReadOnlyList<IReadOnlyProperty>)Array.Empty<IReadOnlyProperty>();
            var fields = entityType.GetProperties()
                .Select(p => new FieldMap(p.Name, fieldName(entityType, p), p.ClrType, key.Contains(p), p))
                .ToList();
            var map = new CollectionMap(native, native, schema, fields, entityType);
            maps.TryAdd(native, map);
            maps.TryAdd(entityType.ClrType.Name, map);
        }

        return new DatabaseSchema(maps);
    }

    /// <summary>Schema for a database without a model: plain identifiers only.</summary>
    public static DatabaseSchema Unmapped { get; } = new(null);

    public CollectionMap Resolve(string collection)
    {
        if (_mapped is null)
        {
            EnsureIdentifier(collection, "collection");
            return new CollectionMap(collection, collection, null, Array.Empty<FieldMap>(), null);
        }

        return _mapped.TryGetValue(collection, out var map)
            ? map
            : throw new DatabaseNotFoundException($"The database has no table or collection '{collection}'.");
    }

    /// <summary>
    /// Names that reach a database unmapped must be plain identifiers: letters, digits and underscores, not starting with
    /// a digit, at most 128 characters. This rules out SQL syntax, MongoDB operators (<c>$</c>) and paths (<c>.</c>).
    /// </summary>
    public static void EnsureIdentifier(string name, string kind)
    {
        if (!IdentifierPattern().IsMatch(name))
        {
            throw new DatabaseQueryException(
                $"Invalid {kind} name: use letters, digits and underscores only (max. 128 characters, not starting with a digit).");
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex IdentifierPattern();
}
