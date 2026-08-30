using Microsoft.AspNetCore.Http;

namespace EFCore.SchemaValidation;

public class SchemaValidationMiddleware
{
    private readonly RequestDelegate _next;

    public static SchemaValidationResult? ValidationResult { get; set; }

    public SchemaValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if ((path == "/" || path == "") && ValidationResult is not null && !ValidationResult.IsValid)
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(RenderErrorPage(ValidationResult));
            return;
        }

        await _next(context);
    }

    private static string RenderErrorPage(SchemaValidationResult result)
    {
        var logInfo = !string.IsNullOrEmpty(result.LogFilePath)
            ? $"<div class='log-file'>Full details logged to: <code>{result.LogFilePath}</code></div>"
            : string.Empty;

        var missingTablesHtml = result.MissingTables.Count > 0
            ? $"<div class='section'><h3>Missing Tables</h3><ul>{string.Join("", result.MissingTables.Select(t => $"<li><code>{t}</code></li>"))}</ul></div>"
            : string.Empty;

        var missingColumnsHtml = result.MissingColumns.Count > 0
            ? $"<div class='section'><h3>Missing Columns</h3><ul>{string.Join("", result.MissingColumns.Select(c => $"<li><code>{c}</code></li>"))}</ul></div>"
            : string.Empty;

        return $@"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Schema Validation Error</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #1a1a2e; color: #e0e0e0; min-height: 100vh; display: flex; justify-content: center; align-items: center; padding: 2rem; }}
        .container {{ max-width: 700px; width: 100%; background: #16213e; border-radius: 12px; padding: 2.5rem; box-shadow: 0 20px 60px rgba(0,0,0,0.5); border: 1px solid #0f3460; }}
        h1 {{ color: #e94560; font-size: 1.8rem; margin-bottom: 0.5rem; }}
        .subtitle {{ color: #a0a0b0; margin-bottom: 2rem; font-size: 0.95rem; }}
        .section {{ margin-bottom: 1.5rem; }}
        .section h3 {{ color: #e94560; font-size: 1.1rem; margin-bottom: 0.75rem; border-bottom: 1px solid #0f3460; padding-bottom: 0.5rem; }}
        ul {{ list-style: none; padding-left: 0; }}
        li {{ padding: 0.4rem 0; padding-left: 1rem; position: relative; }}
        li::before {{ content: ''; position: absolute; left: 0; top: 50%; transform: translateY(-50%); width: 6px; height: 6px; background: #e94560; border-radius: 50%; }}
        code {{ background: #0f3460; padding: 0.2rem 0.5rem; border-radius: 4px; font-size: 0.9rem; }}
        .log-file {{ background: #0f3460; border-left: 3px solid #e94560; padding: 1rem; border-radius: 0 8px 8px 0; margin-top: 1.5rem; font-size: 0.9rem; }}
        .log-file code {{ background: #16213e; border: 1px solid #0f3460; }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>Schema Validation Failed</h1>
        <p class='subtitle'>Your database schema does not match the EF Core model definitions.</p>
        {missingTablesHtml}
        {missingColumnsHtml}
        {logInfo}
    </div>
</body>
</html>";
    }
}
