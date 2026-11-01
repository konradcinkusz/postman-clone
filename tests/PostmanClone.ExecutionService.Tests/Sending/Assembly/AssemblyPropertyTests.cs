using System.Text;
using PostmanClone.Sending.Assembly;
using Xunit;
using static PostmanClone.ExecutionService.Tests.Sending.Assembly.AssemblyFixtures;

namespace PostmanClone.ExecutionService.Tests.Sending.Assembly;

/// <summary>
/// The property the sender's history and logs depend on: for any request built from secrets, the masked form of what was
/// assembled holds no secret value. The inputs come from a seeded generator, so a failure is reproducible.
/// </summary>
public sealed class AssemblyPropertyTests
{
    private const int Cases = 400;

    /// <summary>Whole runes only: a lone surrogate is not well-formed text and is refused by design.</summary>
    private static readonly string[] Pieces =
    [
        "a", "b", "c", "x", "Z", "0", "9", " ", "&", "=", "?", "#", "%", "+", "/", "-", ".", "_", "~", "!",
        "é", "中", "🙂",
    ];

    [Fact]
    public void For_seeded_random_requests_the_masked_form_never_contains_a_secret_value()
    {
        var random = new Random(4242);
        var assembler = Assembler(new SeededRandomBytes(4242));

        for (var index = 0; index < Cases; index++)
        {
            var secrets = Enumerable.Range(0, 3).Select(_ => RandomSecret(random)).ToArray();
            var variables = Variables(
                ("s0", secrets[0], true),
                ("s1", secrets[1], true),
                ("s2", secrets[2], true),
                ("plain", "p", false));

            var request = assembler.Assemble(RandomDefinition(random), variables);

            var visible = new List<string> { request.Masker.MaskUrl(request.Url) };
            visible.AddRange(request.Masker.MaskHeaders(request.Headers).SelectMany(header => new[] { header.Name, header.Value }));
            if (request.Body is { } body)
            {
                visible.Add(request.Masker.MaskText(Encoding.UTF8.GetString(body)));
            }

            foreach (var secret in secrets)
            {
                foreach (var text in visible)
                {
                    Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
                }
            }
        }
    }

    private static string RandomSecret(Random random)
    {
        var length = random.Next(1, 11);
        var builder = new StringBuilder();
        for (var index = 0; index < length; index++)
        {
            builder.Append(Pieces[random.Next(Pieces.Length)]);
        }

        return builder.ToString();
    }

    /// <summary>A random but valid request. The URL always starts with a fixed origin so a secret cannot empty it.</summary>
    private static RequestDefinition RandomDefinition(Random random)
    {
        var url = random.Next(2) == 0
            ? "https://example.test/{{s0}}/x?k={{s1}}&plain={{plain}}"
            : "https://example.test/item?{{s2}}=1&q={{s0}}";

        var parameters = new[] { Row("q", "{{s1}}"), Row("r", "{{s2}}") };
        var headers = new[] { Row("X-Echo", "{{s2}}"), Row("X-Plain", "{{s0}}"), Row("Accept", "{{s1}}") };

        var body = random.Next(3) switch
        {
            0 => new RequestBody(BodyType.Raw, RawLanguage.Text, "{{s0}} and {{s2}}", []),
            1 => new RequestBody(BodyType.FormUrlEncoded, null, null, [Row("f", "{{s1}}")]),
            _ => new RequestBody(BodyType.FormData, null, null, [Row("f", "{{s2}}")]),
        };

        var auth = random.Next(4) switch
        {
            0 => new RequestAuth(AuthType.Bearer, "{{s1}}", null, null, null, null, null),
            1 => new RequestAuth(AuthType.Basic, null, "{{s0}}", "{{s2}}", null, null, null),
            2 => new RequestAuth(AuthType.ApiKey, null, null, null, "X-Auth", "{{s1}}", ApiKeyLocation.Header),
            _ => new RequestAuth(AuthType.ApiKey, null, null, null, "access_key", "{{s2}}", ApiKeyLocation.Query),
        };

        return new RequestDefinition(
            random.Next(2) == 0 ? "GET" : "POST",
            url,
            random.Next(2) == 0 ? parameters : [],
            headers,
            body,
            auth);
    }
}
