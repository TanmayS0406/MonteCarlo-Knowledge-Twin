using Microsoft.EntityFrameworkCore;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Infrastructure.Persistence;

/// <summary>Seeds a demo project (GST reconciliation) so the product is usable on first run.</summary>
public sealed class DataSeeder(AppDbContext db)
{
    public const string SampleTranscript = """
        [00:00:05] Ananya Gupta: Morning all, let's go through the sprint. Rohan, where are we on the GSTN API client?
        [00:00:20] Rohan Mehta: GST-140 is mostly done, but the GSTN sandbox keeps timing out on large returns. I'll fix the GSTN timeout by Friday.
        [00:01:10] Meera Iyer: The ITC matching engine is done from my side, the PR for GST-142 got merged yesterday.
        [00:02:30] Priya Sharma: For the results UI I need the mismatch report API first.
        [00:03:05] Arjun Nair: I can take the mismatch report API, I'll start on it tomorrow.
        [00:04:40] Arjun Nair: API Setu integration is blocked, we are waiting for API Setu credentials from the client.
        [00:06:00] Sneha Kulkarni: Excel export is taking longer than planned, the formatting is messy.
        [00:06:30] Ananya Gupta: Honestly, nobody asked for it in v1. Let's drop Excel export from v1 and move it to v2 because CSV covers the audit need.
        [00:06:45] Priya Sharma: Agreed, CSV download is enough for now.
        [00:07:20] Vikram Rao: I'll write the E2E test plan for reconciliation by Wednesday.
        [00:08:10] Kabir Singh: Staging certificates expire next week, I will renew the staging SSL certificates by Monday.
        [00:09:00] Ananya Gupta: We decided to go with the nightly batch for vendor follow-up emails instead of real time, it is simpler to operate.
        [00:09:30] Ananya Gupta: Thanks everyone.
        """;

    /// <summary>Returns true when demo data was created (caller then runs extraction on the sample meeting).</summary>
    public async Task<bool> SeedAsync(CancellationToken ct = default)
    {
        if (!await db.Set<TenantSettings>().AnyAsync(ct))
            db.Add(new TenantSettings { TeamName = "GST Platform Team" });

        if (await db.ProjectSet.AnyAsync(ct))
        {
            await db.SaveChangesAsync(ct);
            return false;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var project = new Project
        {
            Key = "GST",
            Name = "GST Reconciliation Platform",
            Description = "Match purchase invoices with GSTR-2B, flag ITC mismatches and chase vendors.",
            StartDate = today.AddDays(-60),
            Deadline = WorkingCalendar.AddWorkingDays(today, 32),
            CurrentRelease = "v1"
        };
        db.Add(project);

        Person P(string name, WorkRole role, double cap, string skills) => new()
        {
            Name = name,
            Email = name.ToLowerInvariant().Replace(' ', '.') + "@example.com",
            Role = role,
            Capacity = cap,
            Skills = skills,
            StartDate = today.AddDays(-200)
        };
        var ananya = P("Ananya Gupta", WorkRole.Product, 0.3, "Product, GST domain");
        var meera = P("Meera Iyer", WorkRole.Backend, 0.8, ".NET, SQL, matching algorithms");
        var rohan = P("Rohan Mehta", WorkRole.Backend, 1.0, ".NET, GSTN APIs, resilience");
        var arjun = P("Arjun Nair", WorkRole.Backend, 0.9, ".NET, integrations");
        var priya = P("Priya Sharma", WorkRole.Frontend, 1.0, "React, TypeScript");
        var sneha = P("Sneha Kulkarni", WorkRole.Frontend, 0.9, "React, reporting");
        var vikram = P("Vikram Rao", WorkRole.QA, 1.0, "Playwright, API testing");
        var kabir = P("Kabir Singh", WorkRole.DevOps, 0.6, "Azure, CI/CD");
        db.AddRange(ananya, meera, rohan, arjun, priya, sneha, vikram, kabir);

        var items = new Dictionary<string, WorkItem>();
        WorkItem W(string key, string title, WorkRole role, WorkItemStatus status, double? est, Person? owner, int priority,
            int lastActivityDaysAgo, string release = "v1", double? actual = null, string ac = "", params string[] deps)
        {
            var w = new WorkItem
            {
                ProjectId = project.Id,
                ExternalRef = key,
                Title = title,
                Description = $"{title} for the GST reconciliation platform.",
                AcceptanceCriteria = ac,
                Role = role,
                Status = status,
                EstimateDays = est,
                ActualDays = actual,
                OwnerId = owner?.Id,
                Priority = priority,
                Release = release,
                LastActivityAt = now.AddDays(-lastActivityDaysAgo),
                StartedAt = status is WorkItemStatus.InProgress or WorkItemStatus.InReview or WorkItemStatus.Blocked or WorkItemStatus.Done
                    ? now.AddDays(-lastActivityDaysAgo - (actual ?? est ?? 3) * 1.4) : null,
                CompletedAt = status == WorkItemStatus.Done ? now.AddDays(-lastActivityDaysAgo) : null
            };
            foreach (var d in deps) w.Dependencies.Add(new WorkItemDependency { WorkItemId = w.Id, DependsOnId = items[d].Id });
            items[key] = w;
            db.Add(w);
            return w;
        }

        // History (done) - gives the simulator real estimate-vs-actual ratios.
        W("GST-101", "Project skeleton and CI pipeline", WorkRole.DevOps, WorkItemStatus.Done, 3, kabir, 2, 55, actual: 4);
        W("GST-102", "Sign-in with Entra ID", WorkRole.Backend, WorkItemStatus.Done, 4, rohan, 2, 50, actual: 5);
        W("GST-103", "GSTIN validation API", WorkRole.Backend, WorkItemStatus.Done, 3, arjun, 2, 46, actual: 4.5);
        W("GST-104", "Invoice upload UI", WorkRole.Frontend, WorkItemStatus.Done, 5, priya, 2, 44, actual: 6);
        W("GST-105", "Invoice parser for JSON and Excel", WorkRole.Backend, WorkItemStatus.Done, 5, meera, 1, 40, actual: 7);
        W("GST-106", "Dashboard shell", WorkRole.Frontend, WorkItemStatus.Done, 3, sneha, 3, 38, actual: 3);
        W("GST-107", "Test harness and fixtures", WorkRole.QA, WorkItemStatus.Done, 2, vikram, 3, 36, actual: 3);
        W("GST-108", "GSTR-2B download", WorkRole.Backend, WorkItemStatus.Done, 4, rohan, 1, 33, actual: 6);
        W("GST-109", "Vendor master UI", WorkRole.Frontend, WorkItemStatus.Done, 4, sneha, 3, 30, actual: 5);
        W("GST-110", "Database schema and migrations", WorkRole.Backend, WorkItemStatus.Done, 2, meera, 2, 28, actual: 2.5);
        W("GST-111", "Smoke test suite", WorkRole.QA, WorkItemStatus.Done, 3, vikram, 3, 25, actual: 3.5);
        W("GST-112", "Staging environment", WorkRole.DevOps, WorkItemStatus.Done, 3, kabir, 2, 22, actual: 5);

        // Open work for v1.
        const string ac = "Given valid input, the API returns results within 2s; errors are logged with correlation id.";
        W("GST-140", "GSTN API client with retry", WorkRole.Backend, WorkItemStatus.InProgress, 4, rohan, 1, 1, ac: ac);
        W("GST-142", "ITC matching engine", WorkRole.Backend, WorkItemStatus.InProgress, 8, meera, 1, 3, ac: ac, deps: "GST-140");
        W("GST-143", "Mismatch report API", WorkRole.Backend, WorkItemStatus.Todo, 5, null, 1, 6, ac: ac, deps: "GST-142");
        W("GST-144", "Reconciliation results UI", WorkRole.Frontend, WorkItemStatus.Todo, 6, priya, 1, 4, ac: "Shows matched, partial and missing invoices with filters.", deps: "GST-143");
        W("GST-145", "Vendor follow-up emails", WorkRole.Backend, WorkItemStatus.Todo, null, null, 2, 9, deps: "GST-143");
        W("GST-146", "Filing summary page", WorkRole.Frontend, WorkItemStatus.Todo, 4, sneha, 2, 5, ac: "Summary totals match GSTR-3B fields.", deps: "GST-144");
        W("GST-147", "E2E test suite for reconciliation", WorkRole.QA, WorkItemStatus.Todo, 5, vikram, 2, 5, ac: "Covers happy path and 5 mismatch types.", deps: ["GST-144", "GST-145"]);
        W("GST-148", "Production deployment and monitoring", WorkRole.DevOps, WorkItemStatus.Todo, 3, kabir, 2, 7, ac: "Dashboards and alerts for API errors.", deps: "GST-147");
        W("GST-88", "Excel export", WorkRole.Frontend, WorkItemStatus.InProgress, 5, sneha, 3, 21);
        W("GST-149", "API Setu integration", WorkRole.Backend, WorkItemStatus.Todo, 4, arjun, 2, 8, ac: ac);
        W("GST-150", "Role-based access control", WorkRole.Backend, WorkItemStatus.InReview, 3, rohan, 2, 12, ac: ac);
        W("GST-151", "Audit log", WorkRole.Backend, WorkItemStatus.Todo, 3, meera, 3, 10);
        W("GST-152", "Performance tuning for 1M invoices", WorkRole.Backend, WorkItemStatus.Backlog, 6, null, 4, 30, release: "v2");
        W("GST-153", "Mobile layout", WorkRole.Frontend, WorkItemStatus.Todo, 4, null, 4, 30, release: "v2");

        // Code activity: GST-142's PR merged yesterday, but the ticket still says In progress.
        db.Add(new CodeEvent
        {
            ProjectId = project.Id,
            Type = CodeEventType.PullRequestMerged,
            ExternalId = "pr-217-merged",
            Title = "GST-142 ITC matching engine with fuzzy invoice match",
            Author = "meera-iyer",
            Branch = "feature/GST-142-itc-matching",
            TicketRefs = "GST-142",
            OccurredAt = now.AddDays(-1),
            Url = "https://example.com/pr/217"
        });
        db.Add(new CodeEvent
        {
            ProjectId = project.Id,
            Type = CodeEventType.Commit,
            ExternalId = "a1b2c3d",
            Title = "GST-140 add exponential backoff for GSTN calls",
            Author = "rohan-mehta",
            Branch = "feature/GST-140-retry",
            TicketRefs = "GST-140",
            OccurredAt = now.AddHours(-20)
        });

        await db.SaveChangesAsync(ct);
        return true;
    }
}
