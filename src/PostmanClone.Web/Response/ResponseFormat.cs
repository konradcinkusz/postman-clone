using System.Globalization;

namespace PostmanClone.Web.Response;

/// <summary>How a status, a time or a size is written on the page. Invariant culture, so a test reads the same text anywhere.</summary>
public static class ResponseFormat
{
    /// <summary>Milliseconds below a second, seconds above it.</summary>
    public static string Milliseconds(int milliseconds) => milliseconds < 1000
        ? string.Create(CultureInfo.InvariantCulture, $"{milliseconds} ms")
        : string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 1000.0:0.00} s");

    /// <summary>Bytes, kilobytes or megabytes with one decimal, as the page shows a size.</summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        if (bytes < 1024 * 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.0} KB");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.0} MB");
    }

    /// <summary>The status line as the page shows it: the code and the reason phrase, when the server sent one.</summary>
    public static string StatusLine(int statusCode, string reasonPhrase) =>
        string.IsNullOrWhiteSpace(reasonPhrase)
            ? statusCode.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{statusCode} {reasonPhrase}");

    public static StatusFamily Family(int statusCode) => statusCode switch
    {
        0 => StatusFamily.None,
        >= 100 and < 200 => StatusFamily.Informational,
        >= 200 and < 300 => StatusFamily.Success,
        >= 300 and < 400 => StatusFamily.Redirect,
        >= 400 and < 500 => StatusFamily.ClientError,
        >= 500 and < 600 => StatusFamily.ServerError,
        _ => StatusFamily.Unknown,
    };

    /// <summary>The badge's class for a status family. The text is the status line; colour is never the only signal.</summary>
    public static string BadgeClass(StatusFamily family) => family switch
    {
        StatusFamily.Success => "badge badge-success",
        StatusFamily.Informational or StatusFamily.Redirect => "badge badge-accent",
        StatusFamily.ClientError or StatusFamily.ServerError => "badge badge-danger",
        _ => "badge",
    };
}

public enum StatusFamily
{
    None,
    Informational,
    Success,
    Redirect,
    ClientError,
    ServerError,
    Unknown,
}
