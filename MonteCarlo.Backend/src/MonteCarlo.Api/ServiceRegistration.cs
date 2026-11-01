using MonteCarlo.Application.Common;
using MonteCarlo.Application.Services;
using MonteCarlo.Infrastructure.Integrations;

namespace MonteCarlo.Api;

public static class ServiceRegistration
{
    /// <summary>Registers the Application use cases. Kept in the composition root so Application has no DI dependency.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ReconciliationService>();
        services.AddScoped<MeetingService>();
        services.AddScoped<SuggestionService>();
        services.AddScoped<WorkItemService>();
        services.AddScoped<PeopleService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<CodeActivityService>();
        services.AddScoped<SearchService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<SimulationService>();
        return services;
    }
}

public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        {
            var (status, title) = ex switch
            {
                NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
                ValidationException => (StatusCodes.Status400BadRequest, "Invalid request"),
                GraphAccessException => (StatusCodes.Status502BadGateway, "Microsoft Graph access denied"),
                HttpRequestException => (StatusCodes.Status502BadGateway, "External service error"),
                OperationCanceledException => (499, "Cancelled"),
                _ => (StatusCodes.Status500InternalServerError, "Unexpected error")
            };
            if (status >= 500) log.LogError(ex, "Request failed: {Path}", ctx.Request.Path);
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(new { title, status, detail = ex.Message });
        }
    }
}
