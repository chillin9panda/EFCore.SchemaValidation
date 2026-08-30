using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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
            Validate(context, connection);
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
            await ValidateAsync(context, connection, cancellationToken);
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

    private static void Validate(DbContext context, DbConnection connection)
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

        ThrowIfErrors(missingTables, missingColumns);
    }

    private static async Task ValidateAsync(DbContext context, DbConnection connection, CancellationToken cancellationToken)
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

        ThrowIfErrors(missingTables, missingColumns);
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

    private static void ThrowIfErrors(List<string> missingTables, List<string> missingColumns)
    {
        if (missingTables.Count == 0 && missingColumns.Count == 0) return;

        var errors = new List<string>();
        if (missingTables.Count > 0)
            errors.Add($"Missing Tables: {string.Join(", ", missingTables)}");
        if (missingColumns.Count > 0)
            errors.Add($"Missing Columns: {string.Join(", ", missingColumns)}");

        throw new InvalidOperationException(
            $"Schema Validation Failed!\n{string.Join("\n", errors)}");
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
