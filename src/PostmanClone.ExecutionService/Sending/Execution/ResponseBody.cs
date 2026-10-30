using System.Buffers;
using System.IO.Compression;
using System.Text;
using PostmanClone.ExecutionService.Api;
using PostmanClone.ExecutionService.Sending.Assembly;

namespace PostmanClone.ExecutionService.Sending.Execution;

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
    /// Text is masked and sent as UTF-8; anything else, or text that is not valid UTF-8, is sent as base64. Masking
    /// applies to text only: a secret inside binary bytes is not visible as text and is not searched for.
    /// </summary>
    public static ResponseBodyDto Describe(CapturedBody captured, string? contentType, SecretMasker masker)
    {
        if (IsText(contentType) && TryDecodeUtf8(captured.Bytes, out var text))
        {
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

    private static bool IsText(string? contentType)
    {
        if (contentType is null)
        {
            return false;
        }

        var media = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return media.StartsWith("text/", StringComparison.Ordinal)
            || media is "application/json" or "application/xml" or "application/javascript" or "application/x-www-form-urlencoded" or "application/graphql"
            || media.EndsWith("+json", StringComparison.Ordinal)
            || media.EndsWith("+xml", StringComparison.Ordinal);
    }

    private static bool TryDecodeUtf8(byte[] bytes, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }
}
