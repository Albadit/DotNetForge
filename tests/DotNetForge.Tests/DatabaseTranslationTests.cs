using DotNetForge.Abstractions.Database;
using DotNetForge.Data;
using DotNetForge.Data.Database;
using DotNetForge.Data.Database.Providers.MongoDb;
using DotNetForge.Data.Database.Providers.MySql;
using DotNetForge.Data.Database.Providers.PostgreSql;
using DotNetForge.Data.Database.Providers.Sqlite;
using DotNetForge.Data.Database.Providers.SqlServer;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// How commands become native queries (.docs/database/security.md): parameterized SQL from each SQL database's own builder, MongoDB filters
/// with typed values, escaping, name validation. No database needed.
/// </summary>
public sealed class DatabaseTranslationTests
{
    private const string Injection = "x'; DROP TABLE \"Users\"; --";

    private static readonly DatabaseSchema Relational = SqliteDatabaseProvider.Schema(
        new DotNetForgeDbContext(new DbContextOptionsBuilder<DotNetForgeDbContext>().UseSqlite("Data Source=:memory:").Options).Model);

    private static readonly DatabaseSchema Mongo = MongoDbDatabaseProvider.Schema(
        new MongoDbContext(new DbContextOptionsBuilder<MongoDbContext>().UseMongoDB("mongodb://localhost:27017", "cms").Options).Model);

    private static readonly SqlBuilder Sqlite = new(
        "SQLite",
        (map, fields, filter, sort, skip, take) => Of(new SqliteSqlBuilder(map).Select(fields, filter, sort, skip, take)),
        (map, filter) => Of(new SqliteSqlBuilder(map).Count(filter)),
        (map, aggregation, sort) => Of(new SqliteSqlBuilder(map).Aggregate(aggregation, null, sort, null, null)),
        (map, set, filter) => Of(new SqliteSqlBuilder(map).Update(set, filter)),
        SqliteDialect.EscapeLike);

    private static readonly SqlBuilder PostgreSql = new(
        "PostgreSQL",
        (map, fields, filter, sort, skip, take) => Of(new PostgreSqlSqlBuilder(map).Select(fields, filter, sort, skip, take)),
        (map, filter) => Of(new PostgreSqlSqlBuilder(map).Count(filter)),
        (map, aggregation, sort) => Of(new PostgreSqlSqlBuilder(map).Aggregate(aggregation, null, sort, null, null)),
        (map, set, filter) => Of(new PostgreSqlSqlBuilder(map).Update(set, filter)),
        PostgreSqlDialect.EscapeLike);

    private static readonly SqlBuilder MySql = new(
        "MySQL",
        (map, fields, filter, sort, skip, take) => Of(new MySqlSqlBuilder(map).Select(fields, filter, sort, skip, take)),
        (map, filter) => Of(new MySqlSqlBuilder(map).Count(filter)),
        (map, aggregation, sort) => Of(new MySqlSqlBuilder(map).Aggregate(aggregation, null, sort, null, null)),
        (map, set, filter) => Of(new MySqlSqlBuilder(map).Update(set, filter)),
        MySqlDialect.EscapeLike);

    private static readonly SqlBuilder SqlServer = new(
        "SQL Server",
        (map, fields, filter, sort, skip, take) => Of(new SqlServerSqlBuilder(map).Select(fields, filter, sort, skip, take)),
        (map, filter) => Of(new SqlServerSqlBuilder(map).Count(filter)),
        (map, aggregation, sort) => Of(new SqlServerSqlBuilder(map).Aggregate(aggregation, null, sort, null, null)),
        (map, set, filter) => Of(new SqlServerSqlBuilder(map).Update(set, filter)),
        SqlServerDialect.EscapeLike);

    public static TheoryData<SqlBuilder> Builders() => new() { Sqlite, PostgreSql, MySql, SqlServer };

    public static TheoryData<SqlBuilder, string> Selects() => new()
    {
        { Sqlite, """SELECT "Email", "Status" FROM "Users" WHERE ("Email" = @p0 AND "Status" IN (@p1, @p2)) ORDER BY "Email" DESC LIMIT 5 OFFSET 10""" },
        { PostgreSql, """SELECT "Email", "Status" FROM "Users" WHERE ("Email" = @p0 AND "Status" IN (@p1, @p2)) ORDER BY "Email" DESC LIMIT 5 OFFSET 10""" },
        { MySql, "SELECT `Email`, `Status` FROM `Users` WHERE (`Email` = @p0 AND `Status` IN (@p1, @p2)) ORDER BY `Email` DESC LIMIT 5 OFFSET 10" },
        { SqlServer, "SELECT [Email], [Status] FROM [Users] WHERE ([Email] = @p0 AND [Status] IN (@p1, @p2)) ORDER BY [Email] DESC OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY" },
    };

    public static TheoryData<SqlBuilder, string> Pagings() => new()
    {
        { Sqlite, """SELECT "Email" FROM "Users" LIMIT -1 OFFSET 3""" },
        { PostgreSql, """SELECT "Email" FROM "Users" OFFSET 3""" },
        { MySql, "SELECT `Email` FROM `Users` LIMIT 18446744073709551615 OFFSET 3" },
        { SqlServer, "SELECT [Email] FROM [Users] ORDER BY (SELECT NULL) OFFSET 3 ROWS" },
    };

    public static TheoryData<SqlBuilder, string> Aggregates() => new()
    {
        { Sqlite, """SELECT "Status" AS "Status", COUNT(*) AS "Total" FROM "Users" GROUP BY "Status" ORDER BY "Total" DESC""" },
        { PostgreSql, """SELECT "Status" AS "Status", COUNT(*) AS "Total" FROM "Users" GROUP BY "Status" ORDER BY "Total" DESC""" },
        { MySql, "SELECT `Status` AS `Status`, COUNT(*) AS `Total` FROM `Users` GROUP BY `Status` ORDER BY `Total` DESC" },
        { SqlServer, "SELECT [Status] AS [Status], COUNT(*) AS [Total] FROM [Users] GROUP BY [Status] ORDER BY [Total] DESC" },
    };

    [Theory]
    [MemberData(nameof(Selects))]
    public void Each_database_writes_parameterized_sql(SqlBuilder builder, string expected)
    {
        var map = Relational.Resolve("Users");
        var statement = builder.Select(
            map,
            new[] { map.Field("Email"), map.Field("Status") },
            DatabaseFilter.Eq("Email", "a@example.com") & DatabaseFilter.In("Status", new object?[] { 0, "Disabled" }),
            new[] { new DatabaseSort("Email", Descending: true) },
            10,
            5);

        Assert.Equal(expected, statement.Text);
        Assert.Equal(new object?[] { "a@example.com", UserStatus.Enabled, UserStatus.Disabled }, statement.Values);
    }

    [Theory]
    [MemberData(nameof(Pagings))]
    public void Each_database_pages_without_a_limit_or_sort(SqlBuilder builder, string expected)
    {
        var map = Relational.Resolve("Users");

        Assert.Equal(expected, builder.Select(map, new[] { map.Field("Email") }, null, Array.Empty<DatabaseSort>(), 3, null).Text);
    }

    [Theory]
    [MemberData(nameof(Builders))]
    public void Values_are_parameters_never_sql(SqlBuilder builder)
    {
        var statement = builder.Update(
            Relational.Resolve("Users"), new Dictionary<string, object?> { ["FirstName"] = Injection }, DatabaseFilter.Eq("Email", Injection));

        Assert.DoesNotContain("DROP", statement.Text, StringComparison.Ordinal);
        Assert.Equal(new object?[] { Injection, Injection }, statement.Values);
    }

    [Theory]
    [MemberData(nameof(Builders))]
    public void Like_patterns_match_text_literally(SqlBuilder builder)
    {
        var map = Relational.Resolve("Users");
        var column = builder.Select(map, new[] { map.Field("Email") }, null, Array.Empty<DatabaseSort>(), null, null).Text.Split(' ')[1];

        var exact = builder.Count(map, DatabaseFilter.Contains("Email", "50%_off!", ignoreCase: false));
        var ignoreCase = builder.Count(map, DatabaseFilter.Contains("Email", "50%_off!", ignoreCase: true));

        Assert.EndsWith($"{column} LIKE @p0 ESCAPE '!'", exact.Text, StringComparison.Ordinal);
        Assert.EndsWith($"LOWER({column}) LIKE LOWER(@p0) ESCAPE '!'", ignoreCase.Text, StringComparison.Ordinal);
        Assert.Equal("%50!%!_off!!%", Assert.Single(exact.Values));
    }

    [Fact]
    public void Only_sql_server_escapes_brackets()
    {
        Assert.Equal("![a]", SqlServer.EscapeLike("[a]"));
        Assert.Equal("[a]", Sqlite.EscapeLike("[a]"));
        Assert.Equal("[a]", PostgreSql.EscapeLike("[a]"));
        Assert.Equal("[a]", MySql.EscapeLike("[a]"));
    }

    [Theory]
    [MemberData(nameof(Aggregates))]
    public void Aggregates_group_and_sort_by_output_columns(SqlBuilder builder, string expected)
    {
        var statement = builder.Aggregate(
            Relational.Resolve("Users"),
            new DatabaseAggregation(new[] { "Status" }, new[] { new DatabaseAccumulator("Total", AggregateFunction.Count) }),
            new[] { new DatabaseSort("Total", Descending: true) });

        Assert.Equal(expected, statement.Text);
    }

    [Fact]
    public void Collections_and_fields_must_exist_in_the_model()
    {
        Assert.Throws<DatabaseNotFoundException>(() => Relational.Resolve("Customers"));
        Assert.Throws<DatabaseNotFoundException>(() => Relational.Resolve("Users").Field("Password"));
        Assert.Equal("AuditLogs", Relational.Resolve("AuditLogEntry").Native); // entity name works too
    }

    [Theory]
    [MemberData(nameof(Builders))]
    public void Keys_can_not_be_updated(SqlBuilder builder)
    {
        Assert.Throws<DatabaseQueryException>(() => builder.Update(
            Relational.Resolve("Users"), new Dictionary<string, object?> { ["Id"] = Guid.NewGuid() }, DatabaseFilter.And()));
    }

    [Theory]
    [InlineData("Users; DROP TABLE Users")]
    [InlineData("users\"")]
    [InlineData("$where")]
    [InlineData("a.b")]
    [InlineData("1users")]
    public void Databases_without_a_model_accept_only_plain_identifiers(string name)
    {
        Assert.Throws<DatabaseQueryException>(() => DatabaseSchema.Unmapped.Resolve(name));
        Assert.Throws<DatabaseQueryException>(() => DatabaseSchema.Unmapped.Resolve("events").Field(name));
    }

    [Theory]
    [MemberData(nameof(Builders))]
    public void Values_are_converted_to_the_field_type(SqlBuilder builder)
    {
        var map = Relational.Resolve("Users");
        var id = Guid.NewGuid();

        Assert.Equal(id, builder.Count(map, DatabaseFilter.Eq("Id", id.ToString())).Values[0]);
        Assert.Throws<DatabaseQueryException>(() => builder.Count(map, DatabaseFilter.Eq("Id", "not-a-guid")));
    }

    private static BsonDocument Render(string collection, DatabaseFilter filter) =>
        MongoExecutor.BuildFilter(Mongo.Resolve(collection), filter)
            .Render(new RenderArgs<BsonDocument>(BsonSerializer.LookupSerializer<BsonDocument>(), BsonSerializer.SerializerRegistry));

    [Fact]
    public void Mongo_filters_use_typed_values_and_the_stored_field_names()
    {
        var id = Guid.Parse("8f1c6c55-8e7b-4d0e-9f3a-0b6a2a7f2e11");

        var filter = Render("Users", DatabaseFilter.Eq("Id", id) & DatabaseFilter.Eq("Status", 1) & DatabaseFilter.Gte("CreatedDate", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(new BsonBinaryData(id, GuidRepresentation.Standard), filter["_id"]);
        Assert.Equal(new BsonInt32(1), filter["Status"]);
        Assert.Equal(new BsonDateTime(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), filter["CreatedDate"]["$gte"]);
    }

    [Fact]
    public void Mongo_values_never_become_operators()
    {
        var filter = Render("Users", DatabaseFilter.Eq("Email", "{ \"$ne\": null }"));

        Assert.Equal(new BsonString("{ \"$ne\": null }"), filter["Email"]);
    }

    [Theory]
    [InlineData(TextMatch.Contains, false, "a\\.\\*b", "")]
    [InlineData(TextMatch.StartsWith, true, "^a\\.\\*b", "i")]
    public void Mongo_text_matching_escapes_regular_expressions(TextMatch match, bool ignoreCase, string pattern, string options)
    {
        var filter = Render("Users", new TextFilter("Email", match, "a.*b", ignoreCase));

        var regex = filter["Email"].AsBsonRegularExpression;
        Assert.Equal(pattern, regex.Pattern);
        Assert.Equal(options, regex.Options);
    }

    [Fact]
    public void Mongo_composite_keys_live_under_id()
    {
        var userId = Guid.NewGuid();

        var filter = Render("UserRoles", DatabaseFilter.Eq("UserId", userId));

        Assert.True(filter.Contains("_id.UserId"));
    }

    [Fact]
    public void Mongo_empty_in_matches_nothing()
    {
        Assert.Equal(new BsonDocument("Status", new BsonDocument("$in", new BsonArray())), Render("Users", DatabaseFilter.In("Status", Array.Empty<object?>())));
    }

    [Fact]
    public void Mongo_rejects_operator_and_path_names_without_a_model()
    {
        Assert.Throws<DatabaseQueryException>(() => MongoExecutor.BuildFilter(DatabaseSchema.Unmapped.Resolve("events"), DatabaseFilter.Eq("$where", "1")));
        Assert.Throws<DatabaseQueryException>(() => MongoExecutor.BuildFilter(DatabaseSchema.Unmapped.Resolve("events"), DatabaseFilter.Eq("a.b", "1")));
    }

    private static Statement Of(SqliteSqlStatement s) => new(s.Text, s.Parameters.Select(p => p.Value).ToArray());

    private static Statement Of(PostgreSqlSqlStatement s) => new(s.Text, s.Parameters.Select(p => p.Value).ToArray());

    private static Statement Of(MySqlSqlStatement s) => new(s.Text, s.Parameters.Select(p => p.Value).ToArray());

    private static Statement Of(SqlServerSqlStatement s) => new(s.Text, s.Parameters.Select(p => p.Value).ToArray());

    /// <summary>A built statement, the same shape for every SQL database.</summary>
    public sealed record Statement(string Text, object?[] Values);

    /// <summary>
    /// Each SQL database has its own copy of the SQL builder (.docs/database/providers.md#isolation-and-updates), so the
    /// behaviour above is checked on all four copies: a fix made in one and missed in another fails here.
    /// </summary>
    public sealed record SqlBuilder(
        string Database,
        Func<CollectionMap, IReadOnlyList<FieldMap>, DatabaseFilter?, IReadOnlyList<DatabaseSort>, int?, int?, Statement> Select,
        Func<CollectionMap, DatabaseFilter?, Statement> Count,
        Func<CollectionMap, DatabaseAggregation, IReadOnlyList<DatabaseSort>, Statement> Aggregate,
        Func<CollectionMap, IReadOnlyDictionary<string, object?>, DatabaseFilter?, Statement> Update,
        Func<string, string> EscapeLike)
    {
        public override string ToString() => Database;
    }
}
