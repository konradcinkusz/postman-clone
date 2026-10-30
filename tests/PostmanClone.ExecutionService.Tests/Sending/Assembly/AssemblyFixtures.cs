using PostmanClone.ExecutionService.Options;
using PostmanClone.ExecutionService.Sending.Assembly;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Assembly;

/// <summary>Builders for the assembly tests. Variables are given as (name, value, isSecret) tuples.</summary>
internal static class AssemblyFixtures
{
    public static RequestAssembler Assembler(IRandomBytes? random = null, Action<ExecutionOptions>? configure = null)
    {
        var options = new ExecutionOptions();
        configure?.Invoke(options);
        return new RequestAssembler(options, random ?? new SeededRandomBytes(1));
    }

    public static IReadOnlyDictionary<string, ResolvedValue> Variables(params (string Name, string Value, bool Secret)[] variables) =>
        variables.ToDictionary(variable => variable.Name, variable => new ResolvedValue(variable.Value, variable.Secret), StringComparer.Ordinal);

    public static KeyValueRow Row(string key, string value, bool enabled = true) => new(key, value, enabled);

    public static RequestBody NoBody => new(BodyType.None, null, null, []);

    public static RequestAuth NoAuth => new(AuthType.None, null, null, null, null, null, null);

    /// <summary>A GET with no rows, no body and no auth. Use a <c>with</c> expression to change one part.</summary>
    public static RequestDefinition Get(string url) => new("GET", url, [], [], NoBody, NoAuth);

    public static RequestAssemblyException Refused(Action action) => Assert.Throws<RequestAssemblyException>(action);
}

/// <summary>A deterministic byte source for multipart boundaries. The seed is a number, never a date.</summary>
internal sealed class SeededRandomBytes(int seed) : IRandomBytes
{
    private readonly Random _random = new(seed);

    public void Fill(Span<byte> destination) => _random.NextBytes(destination);
}
