using System.Reflection;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Kernel;

/// <summary>The kernel stays plumbing: no domain types, no PostmanClone dependency, no inheritance chains (P2, P9, P10).</summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Kernel = typeof(Extensions).Assembly;

    [Fact]
    public void Kernel_references_no_other_PostmanClone_assembly()
    {
        var referenced = Kernel.GetReferencedAssemblies()
            .Where(name => name.Name is not null && name.Name.StartsWith("PostmanClone", StringComparison.Ordinal))
            .Select(name => name.Name)
            .ToList();

        Assert.Empty(referenced);
    }

    [Fact]
    public void Kernel_declares_no_entities_DTOs_or_enums()
    {
        var offenders = Kernel.GetTypes()
            .Where(type => type.IsEnum || type.Name.EndsWith("Dto", StringComparison.Ordinal) || type.Name.EndsWith("Entity", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_kernel_type_derives_from_a_non_framework_base_class()
    {
        var offenders = Kernel.GetTypes()
            .Where(type => type.BaseType is { } baseType && baseType != typeof(object) && !IsFrameworkAssembly(baseType.Assembly))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool IsFrameworkAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? string.Empty;
        return name.StartsWith("System", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || name == "netstandard";
    }
}
