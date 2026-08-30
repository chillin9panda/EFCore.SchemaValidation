using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EFCore.SchemaValidation.Tests;

public class SchemaValidationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    private string CreateTempDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
        _tempFiles.Add(path);
        return $"Data Source={path}";
    }

    private void CreateTables(string connectionString, params string[] tableDefinitions)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        connection.Open();
        foreach (var sql in tableDefinitions)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public void ValidateSchema_ModelMatchesDb_NoException()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);

        var ex = Record.Exception(() => context.ValidateSchema());

        Assert.Null(ex);
    }

    [Fact]
    public void ValidateSchema_MissingTable_ThrowsWithTableName()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);

        var ex = Assert.Throws<InvalidOperationException>(() => context.ValidateSchema());

        Assert.Contains("Missing Tables", ex.Message);
        Assert.Contains("[Orders]", ex.Message);
    }

    [Fact]
    public void ValidateSchema_MissingColumn_ThrowsWithColumnName()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);

        var ex = Assert.Throws<InvalidOperationException>(() => context.ValidateSchema());

        Assert.Contains("Missing Columns", ex.Message);
        Assert.Contains("[Products].[Price]", ex.Message);
    }

    [Fact]
    public void ValidateSchema_MultipleMissingTables_ThrowsAllTables()
    {
        var cs = CreateTempDb();
        // No tables created at all

        using var context = new FullModelDbContext(cs);

        var ex = Assert.Throws<InvalidOperationException>(() => context.ValidateSchema());

        Assert.Contains("[Orders]", ex.Message);
        Assert.Contains("[Products]", ex.Message);
    }

    [Fact]
    public void ValidateSchema_MultipleMissingColumns_ThrowsAllColumns()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT)");

        using var context = new FullModelDbContext(cs);

        var ex = Assert.Throws<InvalidOperationException>(() => context.ValidateSchema());

        Assert.Contains("[Products].[Price]", ex.Message);
        Assert.Contains("[Products].[Quantity]", ex.Message);
    }

    [Fact]
    public void ValidateSchema_MixedMissing_ThrowsTablesAndColumns()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);

        var ex = Assert.Throws<InvalidOperationException>(() => context.ValidateSchema());

        Assert.Contains("Missing Tables", ex.Message);
        Assert.Contains("Missing Columns", ex.Message);
        Assert.Contains("[Orders]", ex.Message);
        Assert.Contains("[Products].[Price]", ex.Message);
    }

    [Fact]
    public void ValidateSchema_EntityWithoutPrimaryKey_IsSkipped()
    {
        var cs = CreateTempDb();
        // No tables needed - entity without PK is skipped

        using var context = new NoPrimaryKeyDbContext(cs);

        var ex = Record.Exception(() => context.ValidateSchema());

        Assert.Null(ex);
    }

    [Fact]
    public void ValidateSchema_EntityWithoutTableName_IsSkipped()
    {
        var cs = CreateTempDb();
        // No tables needed - entity without table name is skipped

        using var context = new NoTableNameDbContext(cs);

        var ex = Record.Exception(() => context.ValidateSchema());

        Assert.Null(ex);
    }

    [Fact]
    public void ValidateSchema_ConnectionWasOpen_DoesNotClose()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);
        context.Database.OpenConnection();

        var ex = Record.Exception(() => context.ValidateSchema());

        Assert.Null(ex);
        Assert.Equal(System.Data.ConnectionState.Open, context.Database.GetDbConnection().State);
        context.Database.CloseConnection();
    }

    [Fact]
    public void ValidateSchema_ConnectionWasClosed_OpensAndCloses()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);

        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);

        var ex = Record.Exception(() => context.ValidateSchema());

        Assert.Null(ex);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task ValidateSchemaAsync_ModelMatchesDb_NoException()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);

        var ex = await Record.ExceptionAsync(() => context.ValidateSchemaAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task ValidateSchemaAsync_MissingTable_ThrowsWithTableName()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.ValidateSchemaAsync());

        Assert.Contains("Missing Tables", ex.Message);
        Assert.Contains("[Orders]", ex.Message);
    }

    [Fact]
    public async Task ValidateSchemaAsync_MissingColumn_ThrowsWithColumnName()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.ValidateSchemaAsync());

        Assert.Contains("Missing Columns", ex.Message);
        Assert.Contains("[Products].[Price]", ex.Message);
    }

    [Fact]
    public async Task ValidateSchemaAsync_EntityWithoutPrimaryKey_IsSkipped()
    {
        var cs = CreateTempDb();

        using var context = new NoPrimaryKeyDbContext(cs);

        var ex = await Record.ExceptionAsync(() => context.ValidateSchemaAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task ValidateSchemaAsync_EntityWithoutTableName_IsSkipped()
    {
        var cs = CreateTempDb();

        using var context = new NoTableNameDbContext(cs);

        var ex = await Record.ExceptionAsync(() => context.ValidateSchemaAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task ValidateSchemaAsync_MultipleMissingTables_ThrowsAllTables()
    {
        var cs = CreateTempDb();

        using var context = new FullModelDbContext(cs);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.ValidateSchemaAsync());

        Assert.Contains("[Orders]", ex.Message);
        Assert.Contains("[Products]", ex.Message);
    }

    [Fact]
    public async Task ValidateSchemaAsync_MultipleMissingColumns_ThrowsAllColumns()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT)");

        using var context = new FullModelDbContext(cs);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.ValidateSchemaAsync());

        Assert.Contains("[Products].[Price]", ex.Message);
        Assert.Contains("[Products].[Quantity]", ex.Message);
    }

    [Fact]
    public async Task ValidateSchemaAsync_MixedMissing_ThrowsTablesAndColumns()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.ValidateSchemaAsync());

        Assert.Contains("Missing Tables", ex.Message);
        Assert.Contains("Missing Columns", ex.Message);
    }

    [Fact]
    public async Task ValidateSchemaAsync_ConnectionWasOpen_DoesNotClose()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);
        await context.Database.OpenConnectionAsync();

        var ex = await Record.ExceptionAsync(() => context.ValidateSchemaAsync());

        Assert.Null(ex);
        Assert.Equal(System.Data.ConnectionState.Open, context.Database.GetDbConnection().State);
        await context.Database.CloseConnectionAsync();
    }
}
