using Microsoft.Extensions.Logging;

namespace EFCore.SchemaValidation;

public class SchemaValidationOptions
{
    public ILogger? Logger { get; set; }
    public SchemaValidationErrorAction OnError { get; set; } = SchemaValidationErrorAction.Throw;
    public string LogFilePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "logs", "schema-validation.log");
}

public enum SchemaValidationErrorAction
{
    Throw,
    Log,
    Page
}
