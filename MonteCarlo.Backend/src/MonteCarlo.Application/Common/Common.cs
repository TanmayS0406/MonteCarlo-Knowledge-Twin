using System.Text.Json;
using System.Text.Json.Serialization;
using MonteCarlo.Application.Abstractions;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Application.Common;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ValidationException(string message) : Exception(message);

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public static class KnowledgeStoreExtensions
{
    public static async Task<TenantSettings> GetSettingsAsync(this IKnowledgeStore store, CancellationToken ct = default)
    {
        var s = await store.FirstOrDefaultAsync(store.Settings, ct);
        if (s is not null) return s;
        s = new TenantSettings();
        store.Add(s);
        await store.SaveChangesAsync(ct);
        return s;
    }

    public static async Task<Project> ResolveProjectAsync(this IKnowledgeStore store, Guid? projectId, CancellationToken ct = default)
    {
        var q = projectId is { } id ? store.Projects.Where(p => p.Id == id) : store.Projects.OrderBy(p => p.CreatedAt);
        return await store.FirstOrDefaultAsync(q, ct) ?? throw new NotFoundException("Project not found. Create a project first.");
    }

    public static void Audit(this IKnowledgeStore store, string actor, string action, string entityType, object entityId, string details = "") =>
        store.Add(new AuditEntry
        {
            Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString() ?? "",
            Details = details.Length > 2000 ? details[..2000] : details
        });
}
