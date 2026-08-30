using Microsoft.AspNetCore.Builder;

namespace EFCore.SchemaValidation;

public static class SchemaValidationPageExtensions
{
    public static IApplicationBuilder UseSchemaValidationPage(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SchemaValidationMiddleware>();
    }
}
