using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EFCore.SchemaValidation.Tests;

public class SchemaValidationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch (IOException) { }
        }

        SchemaValidationMiddleware.ValidationResult = null;
    }

    private string CreateTempDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
        _tempFiles.Add(path);
        return $"Data Source={path};Pooling=false";
    }

    private string CreateTempLogFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-schema-validation.log");
        _tempFiles.Add(path);
        return path;
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

    [Fact]
    public void ValidateSchema_LogMode_ThrowsAndWritesLogFile()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);
        var logPath = CreateTempLogFile();

        var ex = Assert.Throws<InvalidOperationException>(() => context.ValidateSchema(o =>
        {
            o.OnError = SchemaValidationErrorAction.Log;
            o.LogFilePath = logPath;
        }));

        Assert.Contains("Missing Tables", ex.Message);
        Assert.Contains("[Orders]", ex.Message);
        Assert.True(File.Exists(logPath));
        var content = File.ReadAllText(logPath);
        Assert.Contains("Missing Tables", content);
        Assert.Contains("[Orders]", content);
    }

    [Fact]
    public void ValidateSchema_LogMode_ModelMatches_NoLogFile()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);
        var logPath = CreateTempLogFile();

        var ex = Record.Exception(() => context.ValidateSchema(o =>
        {
            o.OnError = SchemaValidationErrorAction.Log;
            o.LogFilePath = logPath;
        }));

        Assert.Null(ex);
        Assert.False(File.Exists(logPath));
    }

    [Fact]
    public void ValidateSchema_PageMode_DoesNotThrow_SetsResult()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);
        var logPath = CreateTempLogFile();

        var ex = Record.Exception(() => context.ValidateSchema(o =>
        {
            o.OnError = SchemaValidationErrorAction.Page;
            o.LogFilePath = logPath;
        }));

        Assert.Null(ex);
        Assert.NotNull(SchemaValidationMiddleware.ValidationResult);
        Assert.False(SchemaValidationMiddleware.ValidationResult!.IsValid);
        Assert.Contains("[Orders]", SchemaValidationMiddleware.ValidationResult.MissingTables);
        Assert.True(File.Exists(logPath));
    }

    [Fact]
    public void ValidateSchema_PageMode_ModelMatches_NoResult()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);

        var ex = Record.Exception(() => context.ValidateSchema(o =>
        {
            o.OnError = SchemaValidationErrorAction.Page;
        }));

        Assert.Null(ex);
        Assert.NotNull(SchemaValidationMiddleware.ValidationResult);
        Assert.True(SchemaValidationMiddleware.ValidationResult!.IsValid);
    }

    [Fact]
    public void ValidateSchema_LogMode_WithLogger_LogsToDebug()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);
        var logger = new TestLogger();
        var logPath = CreateTempLogFile();

        var ex = Assert.Throws<InvalidOperationException>(() => context.ValidateSchema(o =>
        {
            o.Logger = logger;
            o.OnError = SchemaValidationErrorAction.Log;
            o.LogFilePath = logPath;
        }));

        Assert.Contains("Missing Tables", ex.Message);
        Assert.True(logger.DebugLogged);
        Assert.Contains("Schema validation issues detected", logger.LastDebugMessage);
    }

    [Fact]
    public void ValidateSchema_LogMode_ModelMatches_LogsOkToDebug()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL)",
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);
        var logger = new TestLogger();

        var ex = Record.Exception(() => context.ValidateSchema(o =>
        {
            o.Logger = logger;
            o.OnError = SchemaValidationErrorAction.Log;
        }));

        Assert.Null(ex);
        Assert.True(logger.DebugLogged);
        Assert.Contains("Schema validation passed", logger.LastDebugMessage);
    }

    [Fact]
    public async Task ValidateSchemaAsync_LogMode_ThrowsAndWritesLogFile()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);
        var logPath = CreateTempLogFile();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.ValidateSchemaAsync(o =>
        {
            o.OnError = SchemaValidationErrorAction.Log;
            o.LogFilePath = logPath;
        }));

        Assert.Contains("Missing Tables", ex.Message);
        Assert.True(File.Exists(logPath));
    }

    [Fact]
    public async Task ValidateSchemaAsync_PageMode_DoesNotThrow_SetsResult()
    {
        var cs = CreateTempDb();
        CreateTables(cs,
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL, Quantity INTEGER)");

        using var context = new FullModelDbContext(cs);
        var logPath = CreateTempLogFile();

        var ex = await Record.ExceptionAsync(() => context.ValidateSchemaAsync(o =>
        {
            o.OnError = SchemaValidationErrorAction.Page;
            o.LogFilePath = logPath;
        }));

        Assert.Null(ex);
        Assert.NotNull(SchemaValidationMiddleware.ValidationResult);
        Assert.False(SchemaValidationMiddleware.ValidationResult!.IsValid);
        Assert.Contains("[Orders]", SchemaValidationMiddleware.ValidationResult.MissingTables);
    }

    [Fact]
    public void SchemaValidationResult_WriteLog_CreatesFileWithContent()
    {
        var logPath = CreateTempLogFile();
        var result = SchemaValidationResult.FromErrors(
            new List<string> { "[Orders]" },
            new List<string> { "[Products].[Price]" },
            logPath);

        Assert.False(result.IsValid);
        Assert.Contains("[Orders]", result.MissingTables);
        Assert.Contains("[Products].[Price]", result.MissingColumns);
        Assert.Contains("Schema Validation Failed!", result.ErrorMessage);

        result.WriteLog();

        Assert.True(File.Exists(logPath));
        var content = File.ReadAllText(logPath);
        Assert.Contains("Schema Validation Report", content);
        Assert.Contains("Missing Tables: [Orders]", content);
        Assert.Contains("Missing Columns: [Products].[Price]", content);
    }

    [Fact]
    public void SchemaValidationResult_Success_IsValid()
    {
        var result = SchemaValidationResult.Success();

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingTables);
        Assert.Empty(result.MissingColumns);
        Assert.Empty(result.ErrorMessage);
    }
}

internal class TestLogger : ILogger
{
    public bool DebugLogged { get; private set; }
    public string LastDebugMessage { get; private set; } = string.Empty;

#pragma warning disable CS8768 // Nullability of return type doesn't match implemented member (net6.0 only)
    IDisposable? ILogger.BeginScope<TState>(TState state) => null!;
#pragma warning restore CS8768

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (logLevel == LogLevel.Debug)
        {
            DebugLogged = true;
            LastDebugMessage = message;
        }
    }
}
