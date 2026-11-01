using System.IO.Compression;
using System.Text;
using System.Net.Http.Headers;
using PostmanClone.ExecutionService.Api;
using PostmanClone.Sending.Contracts;
using PostmanClone.Sending.Assembly;
using PostmanClone.Sending.Execution;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Execution;

/// <summary>The capped body read, in memory. No socket: the streams below count what the reader pulls from them.</summary>
public sealed class ResponseBodyTests
{
    private const long Cap = 5 * 1024 * 1024;


    [Fact]
    public void A_body_cut_inside_a_secret_shows_none_of_the_secret()
    {
        // The secret is 12 characters; the cut leaves its first 5 at the end of the captured text (OI-12).
        const string secret = "s3cr3t-value"; // gitleaks:allow (test fixture, not a credential)
        var masker = SecretMasker.For([new ResolvedValue(secret, IsSecret: true)]);
        var text = "header ok " + secret[..5];
        var captured = new CapturedBody(Encoding.UTF8.GetBytes(text), Encoding.UTF8.GetByteCount(text) + 1000, Truncated: true);

        var body = ResponseBody.Describe(captured, "text/plain", masker);

        Assert.Equal("utf8", body.Encoding);
        Assert.Equal("header ok ", body.Content);
        Assert.DoesNotContain(secret[..5], body.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_body_cut_inside_a_multi_byte_character_is_still_text_and_still_masked()
    {
        const string secret = "tok-42-private"; // gitleaks:allow (test fixture, not a credential)
        var masker = SecretMasker.For([new ResolvedValue(secret, IsSecret: true)]);
        var bytes = Encoding.UTF8.GetBytes("x " + secret + " café");
        // The cut falls after the first byte of the final "é", so the captured bytes end inside a two-byte character.
        var captured = new CapturedBody(bytes[..^1], bytes.Length + 1000, Truncated: true);

        var body = ResponseBody.Describe(captured, "text/plain; charset=utf-8", masker);

        Assert.Equal("utf8", body.Encoding);
        Assert.DoesNotContain("tok-42", body.Content, StringComparison.Ordinal);
        Assert.Equal("x •••••• caf", body.Content);
    }

    [Fact]
    public void A_truncated_binary_body_is_still_base64()
    {
        var captured = new CapturedBody([0x00, 0xFF, 0x10, 0x80], 4000, Truncated: true);

        var body = ResponseBody.Describe(captured, "application/octet-stream", NoSecrets);

        Assert.Equal("base64", body.Encoding);
    }

    private static readonly SecretMasker NoSecrets = SecretMasker.For([]);

    [Fact]
    public async Task A_body_of_exactly_the_cap_is_kept_whole_and_not_truncated()
    {
        var content = new ByteArrayContent(new byte[Cap]);

        var captured = await ResponseBody.ReadAsync(content, Cap, TestContext.Current.CancellationToken);

        Assert.Equal(Cap, captured.SizeBytes);
        Assert.False(captured.Truncated);
    }

    [Fact]
    public async Task A_body_one_byte_past_the_cap_is_cut_at_the_cap_and_marked_truncated()
    {
        var content = new ByteArrayContent(new byte[Cap + 1]);

        var captured = await ResponseBody.ReadAsync(content, Cap, TestContext.Current.CancellationToken);

        Assert.Equal(Cap, captured.SizeBytes);
        Assert.True(captured.Truncated);
    }

    [Fact]
    public async Task An_endless_body_is_read_only_to_the_cap_plus_one_chunk()
    {
        var source = new EndlessStream();
        var content = new StreamContent(source);

        var captured = await ResponseBody.ReadAsync(content, Cap, TestContext.Current.CancellationToken);

        // The reader holds the cap and reads at most one more chunk (64 KB) before it stops; it never reads on.
        Assert.Equal(Cap, captured.SizeBytes);
        Assert.True(captured.Truncated);
        Assert.InRange(source.BytesProduced, Cap, Cap + 64 * 1024 + 1);
    }

    [Fact]
    public async Task A_gzip_bomb_is_stopped_at_the_cap_on_the_decoded_bytes()
    {
        // 64 MB of zeros compresses to a few tens of kilobytes. The cap is on what the caller receives, not on the wire.
        var compressed = Gzip(new byte[64 * 1024 * 1024]);
        Assert.True(compressed.Length < 1024 * 1024, $"The fixture should be small on the wire, was {compressed.Length} bytes.");
        var content = new ByteArrayContent(compressed);
        content.Headers.ContentEncoding.Add("gzip");

        var captured = await ResponseBody.ReadAsync(content, Cap, TestContext.Current.CancellationToken);

        Assert.Equal(Cap, captured.SizeBytes);
        Assert.True(captured.Truncated);
    }

    [Fact]
    public async Task An_unknown_or_double_encoding_is_passed_through_as_received()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentEncoding.Add("gzip");
        content.Headers.ContentEncoding.Add("br");

        var captured = await ResponseBody.ReadAsync(content, Cap, TestContext.Current.CancellationToken);

        Assert.Equal(bytes, captured.Bytes);
    }

    [Fact]
    public void Text_that_is_valid_UTF8_is_sent_as_utf8_and_masked()
    {
        var masker = SecretMasker.For([new ResolvedValue("hunter2-secret", IsSecret: true)]);
        var captured = new CapturedBody("echo hunter2-secret"u8.ToArray(), 19, Truncated: false);

        var body = ResponseBody.Describe(captured, "text/plain; charset=utf-8", masker);

        Assert.Equal("utf8", body.Encoding);
        Assert.Equal("echo ••••••", body.Content);
    }

    [Fact]
    public void A_body_with_no_declared_type_is_text_when_it_is_valid_UTF8()
    {
        var body = ResponseBody.Describe(new CapturedBody("plain words"u8.ToArray(), 11, Truncated: false), contentType: null, NoSecrets);

        Assert.Equal(("utf8", "plain words"), (body.Encoding, body.Content));
    }

    [Fact]
    public void Binary_and_invalid_UTF8_are_sent_as_base64()
    {
        var binary = new CapturedBody([0x00, 0xFF, 0x10], 3, Truncated: false);
        var invalidText = new CapturedBody([0xC3, 0x28], 2, Truncated: false);

        var fromBinary = ResponseBody.Describe(binary, "application/octet-stream", NoSecrets);
        var fromInvalid = ResponseBody.Describe(invalidText, "text/plain", NoSecrets);

        Assert.Equal(("base64", "AP8Q"), (fromBinary.Encoding, fromBinary.Content));
        Assert.Equal("base64", fromInvalid.Encoding);
    }

    private static byte[] Gzip(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(raw);
        }

        return output.ToArray();
    }

    /// <summary>Produces bytes forever and counts them, so a test can show how far the reader went.</summary>
    private sealed class EndlessStream : Stream
    {
        public long BytesProduced { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => BytesProduced;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            BytesProduced += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
