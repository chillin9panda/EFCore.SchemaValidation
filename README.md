# EFCore.SchemaValidation

Cross-checks EF Core model definitions against the actual database schema to detect mismatches.

## Why?

When teams don't use EF Core migrations and instead manage the database schema manually via SQL scripts, it's easy for the C# model and the actual database to drift apart. A forgotten column, a renamed table, or a missing update during deployment can cause runtime errors. This library catches those mismatches early.

## Supported Providers

| Provider | Package | Default Schema |
|---|---|---|
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` | `dbo` |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` | `public` |
| MySQL / MariaDB | `Pomelo.EntityFrameworkCore.MySql` | database name |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` | *(none)* |

## Installation

```
dotnet add package EFCore.SchemaValidation
```

## Usage

```csharp
using EFCore.SchemaValidation;

// Synchronous
context.ValidateSchema();

// Asynchronous
await context.ValidateSchemaAsync(cancellationToken);
```

The extension method queries the database schema to verify that every entity in your `DbContext` model has a corresponding table and columns. If anything is missing, it throws an `InvalidOperationException` listing all mismatches.

### Example: Startup validation

```csharp
using var scope = app.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
dbContext.ValidateSchema();
```

### Example: Test validation

```csharp
[Fact]
public void DatabaseSchemaMatchesModel()
{
    using var context = new AppDbContext();
    context.ValidateSchema();
}
```

## Limitations

| Limitation | Detail |
|---|---|
| **No column type validation** | Checks that columns exist, but does not verify data types, nullability, or defaults. |
| **No index/constraint validation** | Does not check for missing indexes, foreign keys, or unique constraints. |
| **No extra entity detection** | Only detects missing tables and columns in the database. Does not detect extra tables or columns that aren't in the model. |
| **Skips entities without primary keys** | Entities without a defined primary key are excluded from validation. |

## Requirements

- .NET 6.0, 8.0, or 10.0
- A supported EF Core relational provider (see [Supported Providers](#supported-providers))

## License

[MIT](LICENSE)
