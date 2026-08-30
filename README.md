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
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` | `main` |

## Installation

```
dotnet add package EFCore.SchemaValidation
```

## Usage

### Basic (Throw on Error)

The default behavior throws an `InvalidOperationException` listing all missing tables and columns.

```csharp
using EFCore.SchemaValidation;

// Synchronous
context.ValidateSchema();

// Asynchronous
await context.ValidateSchemaAsync(cancellationToken);
```

### Error Handling Options

You can configure how validation errors are handled using the `SchemaValidationOptions` parameter:

```csharp
context.ValidateSchema(o =>
{
    o.OnError = SchemaValidationErrorAction.Throw; // default
    o.Logger = logger;                             // optional ILogger
    o.LogFilePath = "/path/to/logfile.log";        // optional, defaults to logs/schema-validation.log
});
```

#### `OnError` Modes

| Mode | Behavior |
|---|---|
| `Throw` | Throws `InvalidOperationException` (default, backward compatible) |
| `Log` | Throws `InvalidOperationException` **and** writes a log file with full details |
| `Page` | Stores the result for the middleware to render an HTML error page. Writes a log file. Does **not** throw. |

### API Projects — Log to File + Crash

For API-only projects, use `Log` mode. The app crashes with a clear error message, and a log file is written with full details:

```csharp
using var scope = app.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

dbContext.ValidateSchema(o =>
{
    o.OnError = SchemaValidationErrorAction.Log;
    o.Logger = logger;
});
```

When validation fails:
- An `InvalidOperationException` is thrown with missing tables/columns
- A log file is written at `logs/schema-validation.log` (relative to app directory)
- The logger receives a Debug-level message with the full error details

### MVC / Razor Pages — Error Page on Root URL

For MVC projects with Razor Pages, use `Page` mode. When a user visits `/`, they see a styled error page listing missing tables and columns:

```csharp
// Program.cs
using var scope = app.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

dbContext.ValidateSchema(o =>
{
    o.OnError = SchemaValidationErrorAction.Page;
});
```

Then register the middleware:

```csharp
app.UseSchemaValidationPage(); // Only intercepts requests to /
```

The middleware:
- Intercepts **only** the root URL (`/`)
- Renders a styled HTML error page listing missing tables and columns
- Shows the path to the log file with full details
- All other URLs (`/api/*`, `/products`, etc.) pass through unaffected

### Both Log + Page

You can combine both — log to file and show the error page:

```csharp
dbContext.ValidateSchema(o =>
{
    o.OnError = SchemaValidationErrorAction.Page;
    o.Logger = logger;
});
```

### Debug Logging

When a logger is provided, validation always logs at `Debug` level regardless of the `OnError` mode:

```csharp
dbContext.ValidateSchema(o =>
{
    o.Logger = logger;
    o.OnError = SchemaValidationErrorAction.Throw;
});
// Debug: "Schema validation passed. All tables and columns match the model."
// or
// Debug: "Schema validation issues detected: Schema Validation Failed!..."
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
- For `Page` mode: ASP.NET Core (middleware requires `Microsoft.AspNetCore.App`)

## License

[MIT](LICENSE)
