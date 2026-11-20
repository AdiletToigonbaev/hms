using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace Hms.ArchitectureTests;

/// <summary>
/// Правила зависимостей модульного монолита (docs/architecture/system-architecture.md §4).
/// Модульные сборки (Hms.Modules.{Module}.{Layer}) появляются начиная со Sprint 1 и
/// проверяются автоматически: тест находит их в выходном каталоге по имени.
/// </summary>
public partial class DependencyRulesTests
{
    private static readonly Assembly SharedKernel = typeof(Hms.SharedKernel.Result).Assembly;
    private static readonly Assembly Platform = typeof(Hms.Platform.Time.IHotelClock).Assembly;
    private static readonly Assembly Persistence = typeof(Hms.Persistence.HmsDbContext).Assembly;

    private static readonly string[] InfrastructureNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Microsoft.AspNetCore",
        "Serilog",
    ];

    [Fact]
    public void SharedKernel_HasNoDependencies_OnInfrastructureOrOtherProjects()
    {
        var result = Types.InAssembly(SharedKernel)
            .ShouldNot()
            .HaveDependencyOnAny([.. InfrastructureNamespaces, "Hms.Platform", "Hms.Persistence", "Hms.Api", "Hms.Modules"])
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Platform_DoesNotDependOn_ApiPersistenceOrModules()
    {
        var result = Types.InAssembly(Platform)
            .ShouldNot()
            .HaveDependencyOnAny("Hms.Api", "Hms.Persistence", "Hms.Modules")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Persistence_DoesNotDependOn_Api()
    {
        var result = Types.InAssembly(Persistence)
            .ShouldNot()
            .HaveDependencyOn("Hms.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Modules_DependOnOtherModules_OnlyThroughContracts()
    {
        foreach (var module in LoadModuleAssemblies())
        {
            var forbidden = LoadModuleAssemblies()
                .Where(other => other.Module != module.Module && other.Layer != "Contracts")
                .Select(other => other.Assembly.GetName().Name!)
                .ToArray();

            if (forbidden.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(module.Assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
            result.IsSuccessful.Should().BeTrue($"модуль {module.Module} обращается к чужому модулю не через Contracts. {Describe(result)}");
        }
    }

    [Fact]
    public void ModuleDomainAndContracts_DoNotDependOn_Infrastructure()
    {
        foreach (var module in LoadModuleAssemblies().Where(m => m.Layer is "Domain" or "Contracts"))
        {
            var result = Types.InAssembly(module.Assembly).ShouldNot().HaveDependencyOnAny(InfrastructureNamespaces).GetResult();
            result.IsSuccessful.Should().BeTrue($"{module.Assembly.GetName().Name}: {Describe(result)}");
        }
    }

    [Fact]
    public void ModuleAssemblies_AreDiscovered_SoRulesAreNotVacuous()
    {
        var found = LoadModuleAssemblies().Select(m => $"{m.Module}.{m.Layer}").ToList();

        found.Should().Contain(["Identity.Domain", "Identity.Application", "Identity.Infrastructure", "Identity.Contracts", "Audit.Contracts", "Audit.Infrastructure",
            "Hotel.Domain", "Hotel.Application", "Hotel.Infrastructure", "Hotel.Contracts", "Rooms.Domain", "Rooms.Application", "Rooms.Infrastructure", "Rooms.Contracts"]);
    }

    [Fact]
    public void ModuleApplication_DoesNotDependOn_InfrastructureFrameworks()
    {
        foreach (var module in LoadModuleAssemblies().Where(m => m.Layer == "Application"))
        {
            var result = Types.InAssembly(module.Assembly)
                .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore")
                .GetResult();
            result.IsSuccessful.Should().BeTrue($"{module.Assembly.GetName().Name}: {Describe(result)}");
        }
    }

    [Fact]
    public void ModuleAssemblyNameParser_RecognisesConvention()
    {
        // Проверяет, что правила выше действительно увидят модульные сборки, когда они появятся.
        ParseModuleName("Hms.Modules.Bookings.Domain").Should().Be(("Bookings", "Domain"));
        ParseModuleName("Hms.Modules.Finance.Contracts").Should().Be(("Finance", "Contracts"));
        ParseModuleName("Hms.Platform").Should().BeNull();
    }

    private static (string Module, string Layer)? ParseModuleName(string assemblyName)
    {
        var match = ModuleNameRegex().Match(assemblyName);
        return match.Success ? (match.Groups["module"].Value, match.Groups["layer"].Value) : null;
    }

    private static List<(string Module, string Layer, Assembly Assembly)> LoadModuleAssemblies() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "Hms.Modules.*.dll")
            .Select(path => (Path: path, Parsed: ParseModuleName(Path.GetFileNameWithoutExtension(path))))
            .Where(x => x.Parsed is not null)
            .Select(x => (x.Parsed!.Value.Module, x.Parsed.Value.Layer, Assembly.LoadFrom(x.Path)))
            .ToList();

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful ? string.Empty : "Нарушители: " + string.Join(", ", result.FailingTypeNames ?? []);

    [GeneratedRegex(@"^Hms\.Modules\.(?<module>[A-Za-z]+)\.(?<layer>Domain|Application|Infrastructure|Contracts|Api)$")]
    private static partial Regex ModuleNameRegex();
}
