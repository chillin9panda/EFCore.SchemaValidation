using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace EFCore.SchemaValidation;

public static class SchemaValidation
{
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string MySqlProvider = "Pomelo.EntityFrameworkCore.MySql";

    public static void ValidateSchema(this DbContext context)
    {
        var connection = context.Database.GetDbConnection();
        bool wasClosed = connection.State == System.Data.ConnectionState.Closed;

        if (wasClosed) connection.Open();

        try
        {
            Validate(context, connection, null, SchemaValidationErrorAction.Throw);
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    public static void ValidateSchema(this DbContext context, Action<SchemaValidationOptions> configure)
    {
        var options = new SchemaValidationOptions();
        configure(options);

        var connection = context.Database.GetDbConnection();
        bool wasClosed = connection.State == System.Data.ConnectionState.Closed;

        if (wasClosed) connection.Open();

        try
        {
            Validate(context, connection, options.Logger, options.OnError, options.LogFilePath);
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    public static async Task ValidateSchemaAsync(this DbContext context, CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        bool wasClosed = connection.State == System.Data.ConnectionState.Closed;

        if (wasClosed) await connection.OpenAsync(cancellationToken);

        try
        {
            await ValidateAsync(context, connection, null, SchemaValidationErrorAction.Throw, null, cancellationToken);
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }

    public static async Task ValidateSchemaAsync(this DbContext context, Action<SchemaValidationOptions> configure, CancellationToken cancellationToken = default)
    {
        var options = new SchemaValidationOptions();
        configure(options);

        var connection = context.Database.GetDbConnection();
        bool wasClosed = connection.State == System.Data.ConnectionState.Closed;

        if (wasClosed) await connection.OpenAsync(cancellationToken);

        try
        {
            await ValidateAsync(context, connection, options.Logger, options.OnError, options.LogFilePath, cancellationToken);
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }

    private static bool IsSqlite(DbContext context) =>
        context.Database.ProviderName == SqliteProvider;

    private static string GetDefaultSchema(DbContext context)
    {
        return context.Database.ProviderName switch
        {
            SqlServerProvider => "dbo",
            NpgsqlProvider => "public",
            SqliteProvider => "main",
            MySqlProvider => context.Database.GetDbConnection().Database,
            _ => "dbo"
        };
    }

    private static void Validate(DbContext context, DbConnection connection, ILogger? logger, SchemaValidationErrorAction onError, string? logFilePath = null)
    {
        var result = CollectValidationResult(context, connection);

        if (logger != null)
        {
            if (result.IsValid)
                logger.LogDebug("Schema validation passed. All tables and columns match the model.");
            else
                logger.LogDebug("Schema validation issues detected: {ErrorMessage}", result.ErrorMessage);
        }

        if (onError == SchemaValidationErrorAction.Page)
        {
            SchemaValidationMiddleware.ValidationResult = result;
        }

        if (result.IsValid) return;

        HandleErrors(result, onError, logger, logFilePath);
    }

    private static async Task ValidateAsync(DbContext context, DbConnection connection, ILogger? logger, SchemaValidationErrorAction onError, string? logFilePath, CancellationToken cancellationToken)
    {
        var result = await CollectValidationResultAsync(context, connection, cancellationToken);

        if (logger != null)
        {
            if (result.IsValid)
                logger.LogDebug("Schema validation passed. All tables and columns match the model.");
            else
                logger.LogDebug("Schema validation issues detected: {ErrorMessage}", result.ErrorMessage);
        }

        if (onError == SchemaValidationErrorAction.Page)
        {
            SchemaValidationMiddleware.ValidationResult = result;
        }

        if (result.IsValid) return;

        HandleErrors(result, onError, logger, logFilePath);
    }

    private static void HandleErrors(SchemaValidationResult result, SchemaValidationErrorAction onError, ILogger? logger, string? logFilePath)
    {
        switch (onError)
        {
            case SchemaValidationErrorAction.Throw:
                throw new InvalidOperationException(result.ErrorMessage);

            case SchemaValidationErrorAction.Log:
                var resolvedLogFilePath = logFilePath ?? result.LogFilePath;
                result.WriteLog(resolvedLogFilePath);
                if (logger != null)
                    logger.LogError("Schema validation failed. Details written to: {LogFilePath}", resolvedLogFilePath);
                throw new InvalidOperationException(result.ErrorMessage);

            case SchemaValidationErrorAction.Page:
                var pageLogFilePath = logFilePath ?? result.LogFilePath;
                result.WriteLog(pageLogFilePath);
                SchemaValidationMiddleware.ValidationResult = result;
                break;
        }
    }

    private static SchemaValidationResult CollectValidationResult(DbContext context, DbConnection connection)
    {
        var missingTables = new List<string>();
        var missingColumns = new List<string>();
        var isSqlite = IsSqlite(context);
        var defaultSchema = GetDefaultSchema(context);

        foreach (var entity in context.Model.GetEntityTypes())
        {
            if (entity.FindPrimaryKey() is null) continue;

            var tableName = entity.GetTableName();
            var schema = entity.GetSchema() ?? defaultSchema;

            if (string.IsNullOrWhiteSpace(tableName)) continue;

            if (!TableExists(connection, schema, tableName, isSqlite))
            {
                missingTables.Add(isSqlite ? $"[{tableName}]" : $"[{schema}].[{tableName}]");
                continue;
            }

            var dbColumns = GetColumns(connection, schema, tableName, isSqlite);
            var storeObject = StoreObjectIdentifier.Table(tableName, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (string.IsNullOrWhiteSpace(columnName)) continue;

                if (!dbColumns.Contains(columnName))
                {
                    missingColumns.Add(isSqlite ? $"[{tableName}].[{columnName}]" : $"[{schema}].[{tableName}].[{columnName}]");
                }
            }
        }

        return SchemaValidationResult.FromErrors(missingTables, missingColumns,
            Path.Combine(AppContext.BaseDirectory, "logs", "schema-validation.log"));
    }

    private static async Task<SchemaValidationResult> CollectValidationResultAsync(DbContext context, DbConnection connection, CancellationToken cancellationToken)
    {
        var missingTables = new List<string>();
        var missingColumns = new List<string>();
        var isSqlite = IsSqlite(context);
        var defaultSchema = GetDefaultSchema(context);

        foreach (var entity in context.Model.GetEntityTypes())
        {
            if (entity.FindPrimaryKey() is null) continue;

            var tableName = entity.GetTableName();
            var schema = entity.GetSchema() ?? defaultSchema;

            if (string.IsNullOrWhiteSpace(tableName)) continue;

            if (!await TableExistsAsync(connection, schema, tableName, isSqlite, cancellationToken))
            {
                missingTables.Add(isSqlite ? $"[{tableName}]" : $"[{schema}].[{tableName}]");
                continue;
            }

            var dbColumns = await GetColumnsAsync(connection, schema, tableName, isSqlite, cancellationToken);
            var storeObject = StoreObjectIdentifier.Table(tableName, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (string.IsNullOrWhiteSpace(columnName)) continue;

                if (!dbColumns.Contains(columnName))
                {
                    missingColumns.Add(isSqlite ? $"[{tableName}].[{columnName}]" : $"[{schema}].[{tableName}].[{columnName}]");
                }
            }
        }

        return SchemaValidationResult.FromErrors(missingTables, missingColumns,
            Path.Combine(AppContext.BaseDirectory, "logs", "schema-validation.log"));
    }

    private static bool TableExists(DbConnection connection, string schema, string tableName, bool isSqlite)
    {
        using var command = connection.CreateCommand();

        if (isSqlite)
        {
            command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @table";
            AddParameter(command, "@table", tableName);
        }
        else
        {
            command.CommandText = @"
                SELECT COUNT(1)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table";
            AddParameter(command, "@schema", schema);
            AddParameter(command, "@table", tableName);
        }

        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string schema, string tableName, bool isSqlite, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();

        if (isSqlite)
        {
            command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @table";
            AddParameter(command, "@table", tableName);
        }
        else
        {
            command.CommandText = @"
                SELECT COUNT(1)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table";
            AddParameter(command, "@schema", schema);
            AddParameter(command, "@table", tableName);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) > 0;
    }

    private static HashSet<string> GetColumns(DbConnection connection, string schema, string tableName, bool isSqlite)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (isSqlite)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName})";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }
        else
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table";
            AddParameter(command, "@schema", schema);
            AddParameter(command, "@table", tableName);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }
        }

        return columns;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(DbConnection connection, string schema, string tableName, bool isSqlite, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (isSqlite)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName})";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }
        else
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table";
            AddParameter(command, "@schema", schema);
            AddParameter(command, "@table", tableName);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(0));
            }
        }

        return columns;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
