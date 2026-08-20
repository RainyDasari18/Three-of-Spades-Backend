using Microsoft.AspNetCore.Diagnostics;

namespace ThreeOfSpades.Api.Hosting;

public static class ExceptionMapping
{
    public static void UseDomainErrors(this WebApplication app)
    {
        app.UseExceptionHandler(err =>
        {
            err.Run(async ctx =>
            {
                var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
                var message = ex?.Message ?? "Unexpected error.";
                var status = message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 404
                    : message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ? 401
                    : 400;
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsJsonAsync(new { error = message });
            });
        });
    }
}
