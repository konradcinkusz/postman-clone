using PostmanClone.CollectionsService.Interop.Curl;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Interop.Curl;

/// <summary>
/// The importer reads untrusted text, so it must answer every input: an import or a refusal, never an exception. The
/// inputs come from a fixed seed, so a failure reproduces; the seed names no date and depends on no clock.
/// </summary>
public sealed class CurlFuzzTests
{
    private const int Seed = 20_260_101;

    private const int Rounds = 4000;

    /// <summary>The characters that matter to a shell, a URL, a header and a number: quotes, escapes, separators and operators.</summary>
    private const string Alphabet = "curlX-HdFuI \t\r\n'\"\\$`^(){}[]|;&<>#?=:@%/.~!*+,0123456789aAzZ_\u00e9\U0001F600";

    public static IEnumerable<object[]> Seeds() => [[Seed], [Seed + 1], [Seed + 2]];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Random_commands_are_answered_and_never_throw(int seed)
    {
        var random = new Random(seed);
        for (var round = 0; round < Rounds; round++)
        {
            var command = Noise(random, length: random.Next(0, 160));

            var result = CurlParser.Parse(command);

            if (result.Import is { } import)
            {
                Assert.NotNull(import.Request.Method);
                Assert.NotNull(import.Request.Url);
                Assert.False(string.IsNullOrWhiteSpace(import.Name));
                Assert.DoesNotContain('\0', import.Request.Url);
                Assert.All(import.Request.Headers ?? [], row => Assert.DoesNotContain('\0', row.Value));
            }
            else
            {
                Assert.False(string.IsNullOrEmpty(result.Error), $"A refusal must say why: {command}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Random_text_after_curl_is_answered_and_never_throws(int seed)
    {
        var random = new Random(seed);
        for (var round = 0; round < Rounds; round++)
        {
            var command = "curl " + Noise(random, length: random.Next(0, 120));

            var result = CurlParser.Parse(command);

            Assert.True(result.Import is not null || !string.IsNullOrEmpty(result.Error));
        }
    }

    [Fact]
    public void An_accepted_header_name_is_always_a_token_and_a_value_has_no_line_break()
    {
        var random = new Random(Seed);
        var accepted = 0;
        for (var round = 0; round < Rounds; round++)
        {
            // A header written as Name: value, with the value quoted by the same code the export uses, so every
            // input is a well-formed word and the question is only what the importer does with its contents.
            var header = "X-" + Noise(random, length: random.Next(0, 12)) + ": " + Noise(random, length: random.Next(0, 40));
            var command = "curl https://api.example.invalid/ -H " + CurlQuoting.Quote(header);

            if (CurlParser.Parse(command).Import is not { } import)
            {
                continue;
            }

            accepted++;
            foreach (var row in import.Request.Headers ?? [])
            {
                Assert.False(row.Key.Length == 0);
                Assert.True(row.Key.All(character => char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character)), row.Key);
                Assert.DoesNotContain('\r', row.Value);
                Assert.DoesNotContain('\n', row.Value);
            }
        }

        Assert.True(accepted > 0, "The seeded inputs should include some accepted header rows.");
    }

    private static string Noise(Random random, int length)
    {
        var characters = new char[length];
        for (var index = 0; index < length; index++)
        {
            characters[index] = Alphabet[random.Next(Alphabet.Length)];
        }

        return new string(characters);
    }
}
