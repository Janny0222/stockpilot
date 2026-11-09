using System.Reflection;
using System.Text.RegularExpressions;

namespace StockPilot.UnitTests.Architecture;

/// <summary>
/// Enforces the Clean Architecture dependency rule and our tenancy guard-rails automatically,
/// so they survive the next refactor (and the next contributor).
/// </summary>
public sealed partial class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(StockPilot.Domain.Common.Entity).Assembly;
    private static readonly Assembly Application = typeof(StockPilot.Application.DependencyInjection).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_but_the_base_library()
    {
        var references = Domain.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        references.ShouldAllBe(name => name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard");
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_web_frameworks()
    {
        var references = Application.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        references.ShouldNotContain(n => n.StartsWith("StockPilot.Infrastructure", StringComparison.Ordinal));
        references.ShouldNotContain(n => n.StartsWith("StockPilot.API", StringComparison.Ordinal));
        references.ShouldNotContain(n => n.StartsWith("Npgsql", StringComparison.Ordinal));
        references.ShouldNotContain(n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    /// <summary>
    /// IgnoreQueryFilters() switches off tenant isolation for a query. It is only legitimate where the
    /// caller has no tenant yet (auth flows), acts platform-wide, or loads a user by a token-verified ID.
    /// Any new usage must be added here deliberately, which makes it visible in code review.
    /// </summary>
    [Fact]
    public void IgnoreQueryFilters_is_only_used_in_approved_files()
    {
        string[] allowed =
        [
            @"Application\Features\Auth\",
            @"Application\Features\Platform\",
            @"Infrastructure\Identity\PermissionAndSessionServices.cs",
            @"Infrastructure\Persistence\Seed\", // startup seeding: server-controlled, no user input
        ];

        var offenders = Directory
            .EnumerateFiles(Path.Combine(FindBackendRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => IgnoreQueryFiltersCall().IsMatch(File.ReadAllText(f)))
            .Select(f => f.Replace('/', '\\'))
            .Where(f => !allowed.Any(a => f.Contains(a, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_use_case_handler_lives_in_a_feature_folder()
    {
        var handlers = Application.GetTypes().Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal) && t.IsClass);

        handlers.ShouldAllBe(t => t.Namespace!.StartsWith("StockPilot.Application.Features.", StringComparison.Ordinal));
    }

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StockPilot.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate backend root (StockPilot.slnx).");
    }

    [GeneratedRegex(@"\.IgnoreQueryFilters\(")]
    private static partial Regex IgnoreQueryFiltersCall();
}
