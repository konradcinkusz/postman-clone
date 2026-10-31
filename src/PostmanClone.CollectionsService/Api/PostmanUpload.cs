using System.Text.Json;
using PostmanClone.CollectionsService.Interop.Postman;
using PostmanClone.CollectionsService.Services;

namespace PostmanClone.CollectionsService.Api;

/// <summary>
/// Reads the file of an import: a multipart form with a <c>file</c> part, or a raw JSON body. The size is checked before
/// the body is read, and again while it is read, so a body over the limit is refused without being buffered whole.
/// Any other content type is refused (ADR-0016).
/// </summary>
internal static class PostmanUpload
{
    private const int ReadChunkBytes = 81920;

    public static async Task<Outcome<ReadOnlyMemory<byte>>> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var contentType = request.ContentType ?? string.Empty;
        var envelope = PostmanImportLimits.MaxFileBytes + PostmanImportLimits.MaxMultipartEnvelopeBytes;
        try
        {
            if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                if (request.ContentLength is { } declared && declared > envelope)
                {
                    return TooLarge();
                }

                var form = await request.ReadFormAsync(cancellationToken);
                var file = form.Files.GetFile("file");
                if (file is null)
                {
                    return ImportRefusalFor("The multipart form needs a file part named file.");
                }

                if (file.Length > PostmanImportLimits.MaxFileBytes)
                {
                    return TooLarge();
                }

                await using var stream = file.OpenReadStream();
                return await ReadCappedAsync(stream, cancellationToken);
            }

            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                if (request.ContentLength is { } declared && declared > PostmanImportLimits.MaxFileBytes)
                {
                    return TooLarge();
                }

                return await ReadCappedAsync(request.Body, cancellationToken);
            }

            return ImportRefusalFor("Send the collection as application/json or as multipart/form-data.");
        }
        catch (InvalidDataException exception) when (exception.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            return TooLarge();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }
        catch (InvalidDataException)
        {
            return ImportRefusalFor("The multipart form is malformed.");
        }
    }

    private static async Task<Outcome<ReadOnlyMemory<byte>>> ReadCappedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > PostmanImportLimits.MaxFileBytes)
            {
                return TooLarge();
            }

            buffer.Write(chunk, 0, read);
        }

        return Outcome<ReadOnlyMemory<byte>>.Ok(buffer.ToArray());
    }

    private static Failure TooLarge() => ImportRefusal.TooLarge(
        $"A collection file to import is at most {PostmanImportLimits.MaxFileBytes} bytes.").Failure;

    private static Failure ImportRefusalFor(string message) => ImportRefusal.Invalid("$", message).Failure;
}
