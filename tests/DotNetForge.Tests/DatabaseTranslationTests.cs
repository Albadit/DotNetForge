using DotNetForge.Abstractions.Database;
using DotNetForge.Data;
using DotNetForge.Data.Database;
using DotNetForge.Data.Database.MongoDb;
using DotNetForge.Data.Database.Relational;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// How commands become native queries (.docs/database/security.md): parameterized SQL per dialect, MongoDB filters
/// with typed values, escaping, name validation. No database needed.
/// </summary>
public sealed class DatabaseTranslationTests
{
    private const string Injection = "x'; DROP TABLE \"Users\"; --";

    private static readonly DatabaseSchema Relational = RelationalSchema.For(
        new DotNetForgeDbContext(new DbContextOptionsBuilder<DotNetForgeDbContext>().UseSqlite("Data Source=:memory:").Options).Model);

    private static readonly DatabaseSchema Mongo = MongoDbDatabaseProvider.Schema(
        new MongoDbContext(new DbContextOptionsBuilder<MongoDbContext>().UseMongoDB("mongodb://localhost:27017", "cms").Options).Model);

    public static TheoryData<SqlDialect, string> Dialects() => new()
    {
        { new SqliteDialect(), """SELECT "Email", "Status" FROM "Users" WHERE ("Email" = @p0 AND "Status" IN (@p1, @p2)) ORDER BY "Email" DESC LIMIT 5 OFFSET 10""" },
        { new PostgreSqlDialect(), """SELECT "Email", "Status" FROM "Users" WHERE ("Email" = @p0 AND "Status" IN (@p1, @p2)) ORDER BY "Email" DESC LIMIT 5 OFFSET 10""" },
        { new MySqlDialect(), "SELECT `Email`, `Status` FROM `Users` WHERE (`Email` = @p0 AND `Status` IN (@p1, @p2)) ORDER BY `Email` DESC LIMIT 5 OFFSET 10" },
        { new SqlServerDialect(), "SELECT [Email], [Status] FROM [Users] WHERE ([Email] = @p0 AND [Status] IN (@p1, @p2)) ORDER BY [Email] DESC OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY" },
    };

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Each_dialect_writes_parameterized_sql(SqlDialect dialect, string expected)
    {
        var map = Relational.Resolve("Users");
        var statement = new SqlCommandBuilder(dialect, map).Select(
            new[] { map.Field("Email"), map.Field("Status") },
            DatabaseFilter.Eq("Email", "a@example.com") & DatabaseFilter.In("Status", new object?[] { 0, "Disabled" }),
            new[] { new DatabaseSort("Email", Descending: true) }, skip: 10, take: 5);

        Assert.Equal(expected, statement.Text);
        Assert.Equal(new object?[] { "a@example.com", UserStatus.Enabled, UserStatus.Disabled }, statement.Parameters.Select(p => p.Value));
    }

    [Fact]
    public void Values_are_parameters_never_sql()
    {
        var statement = new SqlCommandBuilder(new PostgreSqlDialect(), Relational.Resolve("Users"))
            .Update(new Dictionary<string, object?> { ["FirstName"] = Injection }, DatabaseFilter.Eq("Email", Injection));

        Assert.Equal("""UPDATE "Users" SET "FirstName" = @p0 WHERE "Email" = @p1""", statement.Text);
        Assert.All(statement.Parameters, p => Assert.Equal(Injection, p.Value));
    }

    [Theory]
    [InlineData(false, """ "Email" LIKE @p0 ESCAPE '!'""", "%50!%!_off!!%")]
    [InlineData(true, """ LOWER("Email") LIKE LOWER(@p0) ESCAPE '!'""", "%50!%!_off!!%")]
    public void Like_patterns_match_text_literally(bool ignoreCase, string expectedCondition, string expectedPattern)
    {
        var statement = new SqlCommandBuilder(new SqliteDialect(), Relational.Resolve("Users"))
            .Count(DatabaseFilter.Contains("Email", "50%_off!", ignoreCase));

        Assert.EndsWith(expectedCondition.TrimStart(), statement.Text, StringComparison.Ordinal);
        Assert.Equal(expectedPattern, Assert.Single(statement.Parameters).Value);
    }

    [Fact]
    public void Sql_server_escapes_brackets_and_pages_without_a_sort()
    {
        var dialect = new SqlServerDialect();
        Assert.Equal("![a]", dialect.EscapeLike("[a]"));

        var statement = new SqlCommandBuilder(dialect, Relational.Resolve("Users")).Select(
            new[] { Relational.Resolve("Users").Field("Email") }, filter: null, Array.Empty<DatabaseSort>(), skip: null, take: 1);
        Assert.Equal("SELECT [Email] FROM [Users] ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY", statement.Text);
    }

    [Fact]
    public void Aggregates_group_and_sort_by_output_columns()
    {
        var statement = new SqlCommandBuilder(new PostgreSqlDialect(), Relational.Resolve("Users")).Aggregate(
            new DatabaseAggregation(new[] { "Status" }, new[] { new DatabaseAccumulator("Total", AggregateFunction.Count) }),
            filter: null, new[] { new DatabaseSort("Total", Descending: true) }, skip: null, take: null);

        Assert.Equal("""SELECT "Status" AS "Status", COUNT(*) AS "Total" FROM "Users" GROUP BY "Status" ORDER BY "Total" DESC""", statement.Text);
    }

    [Fact]
    public void Collections_and_fields_must_exist_in_the_model()
    {
        Assert.Throws<DatabaseNotFoundException>(() => Relational.Resolve("Customers"));
        Assert.Throws<DatabaseNotFoundException>(() => Relational.Resolve("Users").Field("Password"));
        Assert.Equal("AuditLogs", Relational.Resolve("AuditLogEntry").Native); // entity name works too
    }

    [Fact]
    public void Keys_can_not_be_updated()
    {
        Assert.Throws<DatabaseQueryException>(() => new SqlCommandBuilder(new SqliteDialect(), Relational.Resolve("Users"))
            .Update(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid() }, DatabaseFilter.And()));
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

    [Fact]
    public void Values_are_converted_to_the_field_type()
    {
        var map = Relational.Resolve("Users");
        var id = Guid.NewGuid();

        Assert.Equal(id, new SqlCommandBuilder(new SqliteDialect(), map).Count(DatabaseFilter.Eq("Id", id.ToString())).Parameters[0].Value);
        Assert.Throws<DatabaseQueryException>(() => new SqlCommandBuilder(new SqliteDialect(), map).Count(DatabaseFilter.Eq("Id", "not-a-guid")));
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
}
