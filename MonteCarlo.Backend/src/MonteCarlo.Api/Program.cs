using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MonteCarlo.Api;
using MonteCarlo.Api.Workers;
using MonteCarlo.Application.Dtos;
using MonteCarlo.Application.Services;
using MonteCarlo.Infrastructure;
using MonteCarlo.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Composition root: Infrastructure implements the Application ports.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<MaintenanceWorker>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Apply EF Core migrations (creates the LocalDB database on first run), then seed demo data.
using (var scope = app.Services.CreateScope())
{
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    log.LogInformation("Applying database migrations...");
    await db.Database.MigrateAsync();
    log.LogInformation("Database is up to date.");

    if (app.Configuration.GetValue("SeedDemoData", true) &&
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync())
    {
        log.LogInformation("Seeded demo project. Extracting the sample sprint call...");
        await scope.ServiceProvider.GetRequiredService<MeetingService>().IngestAsync(new IngestTranscriptRequest(
            null, "Sprint call", DateTimeOffset.UtcNow.AddHours(-1), DataSeeder.SampleTranscript));
    }
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapApiEndpoints();

app.Run();
