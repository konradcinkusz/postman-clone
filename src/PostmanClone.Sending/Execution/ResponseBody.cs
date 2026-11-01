using System.Buffers;
using System.IO.Compression;
using System.Text;
using PostmanClone.Sending.Contracts;
using PostmanClone.Sending.Assembly;

namespace PostmanClone.Sending.Execution;

/// <summary>The decoded bytes kept from a response, and whether the read stopped at the cap.</summary>
public sealed record CapturedBody(byte[] Bytes, long SizeBytes, bool Truncated);

/// <summary>
/// Reads a response body up to a cap and describes it for the caller (ADR-0014). The handler does not decompress
/// (ADR-0011), so this reader decodes <c>Content-Encoding</c> itself and counts the decoded bytes: a gzip bomb stops at
/// the cap however small its compressed form is. At most one chunk and the cap itself are held in memory.
/// </summary>
public static class ResponseBody
{
    private const int ChunkBytes = 64 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task<CapturedBody> ReadAsync(HttpContent content, long capBytes, CancellationToken cancellationToken)
    {
        await using var raw = await content.ReadAsStreamAsync(cancellationToken);
        await using var source = Decode(content.Headers.ContentEncoding, raw);

        using var output = new MemoryStream((int)Math.Min(capBytes, ChunkBytes));
        var chunk = ArrayPool<byte>.Shared.Rent(ChunkBytes);
        try
        {
            while (true)
            {
                var room = capBytes - output.Length;
                if (room <= 0)
                {
                    // One byte decides between "exactly the cap" and "cut off". The rest of the body is not read.
                    var probe = await source.ReadAsync(chunk.AsMemory(0, 1), cancellationToken);
                    return new CapturedBody(output.ToArray(), output.Length, probe > 0);
                }

                var read = await source.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, room)), cancellationToken);
                if (read == 0)
                {
                    return new CapturedBody(output.ToArray(), output.Length, false);
                }

                output.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    /// <summary>
    /// Bytes are text when the type is a text type, or when no type is declared, and they are valid UTF-8 (strict decoding).
    /// Text is masked and sent as UTF-8; anything else is sent as base64. Masking applies to text only: a secret inside
    /// binary bytes is not visible as text and is not searched for.
    /// </summary>
    public static ResponseBodyDto Describe(CapturedBody captured, string? contentType, SecretMasker masker)
    {
        // With no declared type the bytes decide: a plain-text answer from a server that omits the header is still text.
        if ((contentType is null || IsText(contentType)) && TryDecodeUtf8(captured, out var text))
        {
            // A body cut at the read cap can end inside a secret; the beginning of that secret would be visible (OI-12).
            if (captured.Truncated)
            {
                text = masker.TrimPartialSecretAtEnd(text);
            }

            return new ResponseBodyDto("utf8", masker.MaskText(text), captured.SizeBytes, captured.Truncated, contentType);
        }

        return new ResponseBodyDto("base64", Convert.ToBase64String(captured.Bytes), captured.SizeBytes, captured.Truncated, contentType);
    }

    private static Stream Decode(ICollection<string> encodings, Stream raw)
    {
        // Several codings, or none, are passed through as received: the sender does not guess the order.
        if (encodings.Count != 1)
        {
            return raw;
        }

        return encodings.Single().ToLowerInvariant() switch
        {
            "gzip" or "x-gzip" => new GZipStream(raw, CompressionMode.Decompress, leaveOpen: true),
            "deflate" => new ZLibStream(raw, CompressionMode.Decompress, leaveOpen: true),
            "br" => new BrotliStream(raw, CompressionMode.Decompress, leaveOpen: true),
            _ => raw,
        };
    }

    private static bool IsText(string contentType)
    {
        var media = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return media.StartsWith("text/", StringComparison.Ordinal)
            || media is "application/json" or "application/xml" or "application/javascript" or "application/x-www-form-urlencoded" or "application/graphql"
            || media.EndsWith("+json", StringComparison.Ordinal)
            || media.EndsWith("+xml", StringComparison.Ordinal);
    }

    /// <summary>
    /// Strict UTF-8. A body cut at the read cap can end inside a multi-byte character: that incomplete tail is set aside
    /// (at most three bytes) rather than turning the whole text into base64, which would show it unmasked (OI-12).
    /// </summary>
    private static bool TryDecodeUtf8(CapturedBody captured, out string text)
    {
        var bytes = captured.Bytes;
        var trailing = captured.Truncated ? 3 : 0;
        for (var cut = 0; cut <= trailing && cut <= bytes.Length; cut++)
        {
            try
            {
                text = StrictUtf8.GetString(bytes, 0, bytes.Length - cut);
                return true;
            }
            catch (DecoderFallbackException)
            {
                // Try again with one byte fewer: only an incomplete sequence at the cut can be fixed this way.
            }
        }

        text = string.Empty;
        return false;
    }
}
