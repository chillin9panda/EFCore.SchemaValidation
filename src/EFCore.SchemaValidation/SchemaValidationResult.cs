namespace EFCore.SchemaValidation;

public class SchemaValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<string> MissingTables { get; }
    public IReadOnlyList<string> MissingColumns { get; }
    public string ErrorMessage { get; }
    public string? LogFilePath { get; }

    private SchemaValidationResult(
        List<string> missingTables,
        List<string> missingColumns,
        string? logFilePath)
    {
        MissingTables = missingTables.AsReadOnly();
        MissingColumns = missingColumns.AsReadOnly();
        IsValid = missingTables.Count == 0 && missingColumns.Count == 0;
        LogFilePath = logFilePath;

        var errors = new List<string>();
        if (missingTables.Count > 0)
            errors.Add($"Missing Tables: {string.Join(", ", missingTables)}");
        if (missingColumns.Count > 0)
            errors.Add($"Missing Columns: {string.Join(", ", missingColumns)}");
        ErrorMessage = errors.Count > 0
            ? $"Schema Validation Failed!\n{string.Join("\n", errors)}"
            : string.Empty;
    }

    public static SchemaValidationResult FromErrors(
        List<string> missingTables,
        List<string> missingColumns,
        string logFilePath)
    {
        return new SchemaValidationResult(missingTables, missingColumns, logFilePath);
    }

    public static SchemaValidationResult Success()
    {
        return new SchemaValidationResult(new List<string>(), new List<string>(), null);
    }

    public void WriteLog(string? overridePath = null)
    {
        var filePath = overridePath ?? LogFilePath;
        if (string.IsNullOrEmpty(filePath) || IsValid) return;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var content = $"Schema Validation Report - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n" +
                       new string('=', 60) + "\n\n" +
                       ErrorMessage + "\n\n" +
                       new string('=', 60) + "\n" +
                       $"Log generated at: {filePath}\n";

        File.WriteAllText(filePath, content);
    }
}
