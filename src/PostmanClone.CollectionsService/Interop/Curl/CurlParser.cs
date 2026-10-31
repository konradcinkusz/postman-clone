using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Interop.Curl;

/// <summary>What a cURL command imports: the request, the name it is saved under and what was not carried over (WP17).</summary>
public sealed record CurlImport(RequestDefinition Request, string Name, IReadOnlyList<ImportWarning> Warnings);

/// <summary>The outcome of a parse: the import, or the reason the text is not a cURL command at all.</summary>
public sealed record CurlParseResult(CurlImport? Import, string? Error)
{
    public static CurlParseResult Accepted(CurlImport import) => new(import, null);

    public static CurlParseResult Refused(string error) => new(null, error);
}

/// <summary>
/// Reads a pasted cURL command into a <c>RequestDefinition</c> (WP17). Nothing is executed and nothing is fetched: the
/// words come from <see cref="CurlTokenizer"/> and each option sets one part of the request. A literal password is never
/// kept: it becomes <c>{{password}}</c> with a <c>secret_not_imported</c> warning (ADR-0006). Every option that is not
/// carried over is named in a warning, so nothing is dropped silently.
/// The result is not validated here: the request service runs <see cref="RequestDefinitionValidator"/> on it, as on
/// every other write, so an imported request is judged the same way as a built one.
/// </summary>
public static class CurlParser
{
    public const string PasswordVariable = "{{password}}";

    public const int MaxNameLength = 200;

    public const string SecretNotImported = "secret_not_imported";

    public const string UnsupportedOption = "unsupported_option";

    public const string OptionIgnored = "option_ignored";

    public const string FileNotImported = "file_not_imported";

    public const string InvalidArgument = "invalid_argument";

    public const string SensitiveValueKept = "sensitive_value_kept";

    public const string UnsupportedBody = "unsupported_body";

    public const string ExtraArgumentIgnored = "extra_argument_ignored";

    private const string EmptyCommand = "The command is empty.";

    private const string NotCurl = "The command must start with curl.";

    private const string NoUrl = "The command has no URL.";

    private static readonly string[] SensitiveNameParts = ["token", "secret", "password", "apikey", "api-key", "api_key"];

    private static readonly string[] SensitiveHeaderNames = ["authorization", "proxy-authorization", "cookie", "set-cookie"];

    /// <summary>What an option does to the request being built.</summary>
    private enum Kind
    {
        Request,
        Header,
        DataFile,
        DataRaw,
        DataUrlEncode,
        Form,
        FormString,
        User,
        Cookie,
        Get,
        Head,
        Url,
        Insecure,
        Silent,
        Ignored,
    }

    private readonly record struct Option(Kind Kind, bool TakesValue);

    private static readonly Dictionary<string, Option> LongOptions = new(StringComparer.Ordinal)
    {
        ["request"] = new(Kind.Request, true),
        ["header"] = new(Kind.Header, true),
        ["data"] = new(Kind.DataFile, true),
        ["data-ascii"] = new(Kind.DataFile, true),
        ["data-binary"] = new(Kind.DataFile, true),
        ["data-raw"] = new(Kind.DataRaw, true),
        ["data-urlencode"] = new(Kind.DataUrlEncode, true),
        ["form"] = new(Kind.Form, true),
        ["form-string"] = new(Kind.FormString, true),
        ["user"] = new(Kind.User, true),
        ["cookie"] = new(Kind.Cookie, true),
        ["get"] = new(Kind.Get, false),
        ["head"] = new(Kind.Head, false),
        ["url"] = new(Kind.Url, true),
        ["insecure"] = new(Kind.Insecure, false),
        ["location"] = new(Kind.Silent, false),
        ["compressed"] = new(Kind.Silent, false),
        ["silent"] = new(Kind.Silent, false),
        ["show-error"] = new(Kind.Silent, false),
        ["verbose"] = new(Kind.Silent, false),
        ["include"] = new(Kind.Silent, false),
        ["fail"] = new(Kind.Ignored, false),
        ["user-agent"] = new(Kind.Ignored, true),
        ["referer"] = new(Kind.Ignored, true),
        ["output"] = new(Kind.Ignored, true),
        ["max-time"] = new(Kind.Ignored, true),
        ["max-redirs"] = new(Kind.Ignored, true),
        ["connect-timeout"] = new(Kind.Ignored, true),
        ["retry"] = new(Kind.Ignored, true),
        ["proxy"] = new(Kind.Ignored, true),
        ["cacert"] = new(Kind.Ignored, true),
        ["cert"] = new(Kind.Ignored, true),
        ["key"] = new(Kind.Ignored, true),
        ["cookie-jar"] = new(Kind.Ignored, true),
        ["write-out"] = new(Kind.Ignored, true),
        ["upload-file"] = new(Kind.Ignored, true),
        ["resolve"] = new(Kind.Ignored, true),
    };

    private static readonly Dictionary<char, Option> ShortOptions = new()
    {
        ['X'] = new(Kind.Request, true),
        ['H'] = new(Kind.Header, true),
        ['d'] = new(Kind.DataFile, true),
        ['F'] = new(Kind.Form, true),
        ['u'] = new(Kind.User, true),
        ['b'] = new(Kind.Cookie, true),
        ['G'] = new(Kind.Get, false),
        ['I'] = new(Kind.Head, false),
        ['k'] = new(Kind.Insecure, false),
        ['L'] = new(Kind.Silent, false),
        ['s'] = new(Kind.Silent, false),
        ['S'] = new(Kind.Silent, false),
        ['v'] = new(Kind.Silent, false),
        ['i'] = new(Kind.Silent, false),
        ['f'] = new(Kind.Ignored, false),
        ['A'] = new(Kind.Ignored, true),
        ['e'] = new(Kind.Ignored, true),
        ['o'] = new(Kind.Ignored, true),
        ['m'] = new(Kind.Ignored, true),
        ['x'] = new(Kind.Ignored, true),
        ['c'] = new(Kind.Ignored, true),
        ['w'] = new(Kind.Ignored, true),
        ['T'] = new(Kind.Ignored, true),
    };

    public static CurlParseResult Parse(string command)
    {
        var tokens = CurlTokenizer.Tokenize(command);
        if (tokens.Error is not null)
        {
            return CurlParseResult.Refused(tokens.Error);
        }

        if (tokens.Words.Count == 0)
        {
            return CurlParseResult.Refused(EmptyCommand);
        }

        if (tokens.Words[0] is not ("curl" or "curl.exe"))
        {
            return CurlParseResult.Refused(NotCurl);
        }

        return new Importer(tokens.Words, tokens.Warnings.ToList()).Run();
    }

    /// <summary>Whether a header or query parameter with this name carries a credential (the masking names of CONTRACTS §4).</summary>
    public static bool IsSensitiveName(string name)
    {
        var lower = name.ToLowerInvariant();
        return SensitiveHeaderNames.Contains(lower, StringComparer.Ordinal)
            || SensitiveNameParts.Any(part => lower.Contains(part, StringComparison.Ordinal));
    }

    /// <summary>
    /// Reads the words after <c>curl</c>. Options are read left to right, so a later option overrides an earlier one
    /// (as it does in cURL), and the first URL wins.
    /// </summary>
    private sealed class Importer(IReadOnlyList<string> words, List<ImportWarning> warnings)
    {
        private readonly List<KeyValue> _headers = [];
        private readonly List<string> _dataPieces = [];
        private readonly List<KeyValue> _formFields = [];
        private RequestAuth _auth = new("none", null, null, null, null, null, null);
        private string? _method;
        private string? _url;
        private int _urlAt;
        private bool _head;
        private bool _get;
        private bool _dataSeen;
        private bool _formSeen;
        private int? _bodyAt;

        public CurlParseResult Run()
        {
            var endOfOptions = false;
            for (var index = 1; index < words.Count; index++)
            {
                var word = words[index];
                if (endOfOptions || word.Length < 2 || word[0] != '-')
                {
                    SetUrl(word, index);
                }
                else if (word == "--")
                {
                    endOfOptions = true;
                }
                else
                {
                    index = word.StartsWith("--", StringComparison.Ordinal) ? LongOption(index) : ShortOptionCluster(index);
                }
            }

            return Finish();
        }

        /// <summary>A <c>--name</c> option, with its value after <c>=</c> or in the next word. Returns the last word it used.</summary>
        private int LongOption(int at)
        {
            var word = words[at];
            var equals = word.IndexOf('=');
            var name = equals < 0 ? word : word[..equals];
            var inline = equals < 0 ? null : word[(equals + 1)..];

            if (!LongOptions.TryGetValue(name[2..], out var option))
            {
                Unsupported(name, at);
                return at;
            }

            if (!option.TakesValue)
            {
                if (inline is null)
                {
                    Apply(option, name, null, at);
                }
                else
                {
                    Unsupported(name, at);
                }

                return at;
            }

            if (inline is not null)
            {
                Apply(option, name, inline, at);
                return at;
            }

            if (at + 1 >= words.Count)
            {
                Warn(InvalidArgument, $"{name} needs a value, and none was given.", at);
                return at;
            }

            Apply(option, name, words[at + 1], at);
            return at + 1;
        }

        /// <summary>A <c>-abc</c> cluster: each letter is an option, and an option that takes a value ends the cluster.</summary>
        private int ShortOptionCluster(int at)
        {
            var word = words[at];
            for (var position = 1; position < word.Length; position++)
            {
                var letter = word[position];
                var name = "-" + letter;
                if (!ShortOptions.TryGetValue(letter, out var option))
                {
                    Unsupported(name, at);
                    continue;
                }

                if (!option.TakesValue)
                {
                    Apply(option, name, null, at);
                    continue;
                }

                var rest = word[(position + 1)..];
                if (rest.Length > 0)
                {
                    Apply(option, name, rest, at);
                    return at;
                }

                if (at + 1 >= words.Count)
                {
                    Warn(InvalidArgument, $"{name} needs a value, and none was given.", at);
                    return at;
                }

                Apply(option, name, words[at + 1], at);
                return at + 1;
            }

            return at;
        }

        private void Apply(Option option, string name, string? value, int at)
        {
            var text = value ?? string.Empty;
            switch (option.Kind)
            {
                case Kind.Request:
                    _method = text.ToUpperInvariant();
                    break;
                case Kind.Header:
                    AddHeader(text, at);
                    break;
                case Kind.DataFile:
                    AddData(text, at, fileReference: text.StartsWith('@'));
                    break;
                case Kind.DataRaw:
                    AddData(text, at, fileReference: false);
                    break;
                case Kind.DataUrlEncode:
                    AddUrlEncodedData(text, at);
                    break;
                case Kind.Form:
                    AddFormField(text, at, literal: false);
                    break;
                case Kind.FormString:
                    AddFormField(text, at, literal: true);
                    break;
                case Kind.User:
                    SetBasicAuth(text, at);
                    break;
                case Kind.Cookie:
                    AddCookie(text, at);
                    break;
                case Kind.Get:
                    _get = true;
                    break;
                case Kind.Head:
                    _head = true;
                    break;
                case Kind.Url:
                    SetUrl(text, at);
                    break;
                case Kind.Insecure:
                    Warn(OptionIgnored, $"{name} was ignored: certificate checks are not part of an imported request.", at);
                    break;
                case Kind.Silent:
                    break;
                case Kind.Ignored:
                    Warn(OptionIgnored, $"{name} is not imported; the request is sent without it.", at);
                    break;
            }
        }

        private void AddHeader(string text, int at)
        {
            if (!TryParseHeader(text, out var name, out var value, out var problem))
            {
                Warn(InvalidArgument, problem, at);
                return;
            }

            AddHeaderRow(name, value, at);
        }

        private void AddCookie(string text, int at)
        {
            if (!text.Contains('='))
            {
                Warn(FileNotImported, "A cookie file was not read; only cookie strings such as name=value are imported.", at);
                return;
            }

            AddHeaderRow("Cookie", text.Trim(), at);
        }

        private void AddHeaderRow(string name, string value, int at)
        {
            if (IsSensitiveName(name) && value.Length > 0 && !value.Contains("{{", StringComparison.Ordinal))
            {
                Warn(SensitiveValueKept, $"The {name} value was kept as written. A literal credential belongs in a variable.", at);
            }

            _headers.Add(new KeyValue(name, value));
        }

        /// <summary>Sends a raw body: <c>-d</c>, <c>--data-raw</c> and friends. A file reference is reported and its part left out.</summary>
        private void AddData(string text, int at, bool fileReference)
        {
            _dataSeen = true;
            _bodyAt ??= at;
            if (fileReference)
            {
                Warn(FileNotImported, "A file reference in the data was not read; that part of the body is missing.", at);
                return;
            }

            _dataPieces.Add(text);
        }

        /// <summary>
        /// <c>--data-urlencode</c>: <c>content</c>, <c>=content</c> or <c>name=content</c>, with the content percent-encoded.
        /// A file reference (<c>@file</c>, <c>name@file</c>) is reported.
        /// </summary>
        private void AddUrlEncodedData(string text, int at)
        {
            _dataSeen = true;
            _bodyAt ??= at;
            var equals = text.IndexOf('=');
            var sign = text.IndexOf('@');
            if (sign >= 0 && (equals < 0 || sign < equals))
            {
                Warn(FileNotImported, "A file reference in the data was not read; that part of the body is missing.", at);
                return;
            }

            if (equals < 0)
            {
                _dataPieces.Add(CurlQuoting.Escape(text));
                return;
            }

            var name = text[..equals];
            var content = CurlQuoting.Escape(text[(equals + 1)..]);
            _dataPieces.Add(name.Length == 0 ? content : name + "=" + content);
        }

        /// <summary>A form text field, <c>name=value</c>. A file in a field (<c>@file</c>, <c>&lt;file</c>) is reported; <c>;type=...</c> is dropped.</summary>
        private void AddFormField(string text, int at, bool literal)
        {
            _formSeen = true;
            _bodyAt ??= at;
            var equals = text.IndexOf('=');
            if (equals <= 0)
            {
                Warn(InvalidArgument, "A form field needs a name and a value, written as name=value.", at);
                return;
            }

            var name = text[..equals];
            var value = text[(equals + 1)..];
            if (!literal)
            {
                if (value.StartsWith('@') || value.StartsWith('<'))
                {
                    Warn(FileNotImported, "A file in a form field was not read; the field was not imported.", at);
                    return;
                }

                var semicolon = value.IndexOf(';');
                if (semicolon >= 0)
                {
                    value = value[..semicolon];
                }
            }

            _formFields.Add(new KeyValue(name, value));
        }

        /// <summary>
        /// <c>-u user:password</c>. The username is kept. A literal password is replaced by <c>{{password}}</c> and reported;
        /// a password that is already a variable reference, or empty, is kept as written.
        /// </summary>
        private void SetBasicAuth(string text, int at)
        {
            var colon = text.IndexOf(':');
            var username = colon < 0 ? text : text[..colon];
            string? password = colon < 0 ? null : text[(colon + 1)..];
            if (password is { Length: > 0 } && !RequestDefinitionValidator.IsSecretReference(password))
            {
                password = PasswordVariable;
                Warn(SecretNotImported, "The password was not stored. It was replaced by the variable {{password}}; define it in an environment.", at);
            }

            _auth = new RequestAuth("basic", null, username, password, null, null, null);
        }

        private void SetUrl(string text, int at)
        {
            if (_url is not null)
            {
                Warn(ExtraArgumentIgnored, "Only the first URL is imported; this one was ignored.", at);
                return;
            }

            _url = text;
            _urlAt = at;
        }

        private CurlParseResult Finish()
        {
            if (_url is null)
            {
                return CurlParseResult.Refused(NoUrl);
            }

            var url = _url;
            if (_get && _dataPieces.Count > 0)
            {
                url = CurlQuoting.AppendQuery(url, string.Join('&', _dataPieces));
                _dataPieces.Clear();
            }

            url = ReplaceUrlPassword(url, _urlAt);
            WarnSensitiveQuery(url, _urlAt);

            var method = _method ?? InferredMethod();
            var request = new RequestDefinition(method, url, [], _headers.ToList(), BodyOf(), _auth);
            return CurlParseResult.Accepted(new CurlImport(request, NameFor(method, url), warnings));
        }

        /// <summary>cURL's rule: <c>-I</c> is HEAD, data or a form is POST, anything else GET. <c>-G</c> moves data to the query, so it is GET.</summary>
        private string InferredMethod()
        {
            if (_head)
            {
                return "HEAD";
            }

            return !_get && (_dataSeen || _formSeen) ? "POST" : "GET";
        }

        private RequestBody BodyOf()
        {
            if (_formFields.Count > 0)
            {
                if (_dataSeen && !_get)
                {
                    Warn(UnsupportedBody, "Data and form fields were both given; only the form fields were imported.", _bodyAt ?? 0);
                }

                return new RequestBody("formData", null, null, _formFields.ToList());
            }

            if (!_get && _dataSeen)
            {
                return new RequestBody("raw", LanguageOf(_headers), string.Join('&', _dataPieces), []);
            }

            return new RequestBody("none", null, null, []);
        }

        /// <summary>The raw body's language from the last <c>Content-Type</c> header; text when there is none.</summary>
        private static string LanguageOf(IReadOnlyList<KeyValue> headers)
        {
            var contentType = headers
                .LastOrDefault(header => string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))?
                .Value.ToLowerInvariant() ?? string.Empty;

            if (contentType.Contains("json", StringComparison.Ordinal))
            {
                return "json";
            }

            if (contentType.Contains("xml", StringComparison.Ordinal))
            {
                return "xml";
            }

            if (contentType.Contains("html", StringComparison.Ordinal))
            {
                return "html";
            }

            return contentType.Contains("javascript", StringComparison.Ordinal) ? "javascript" : "text";
        }

        /// <summary>
        /// A password written into the URL (<c>https://user:secret@host</c>) is replaced by <c>{{password}}</c> with the same
        /// warning as <c>-u</c>. Only the password is changed; the rest of the URL is kept.
        /// </summary>
        private string ReplaceUrlPassword(string url, int at)
        {
            var schemeAt = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeAt < 0)
            {
                return url;
            }

            var start = schemeAt + 3;
            var end = url.IndexOfAny(['/', '?', '#'], start);
            if (end < 0)
            {
                end = url.Length;
            }

            var authority = url[start..end];
            var atSign = authority.LastIndexOf('@');
            if (atSign < 0)
            {
                return url;
            }

            var userinfo = authority[..atSign];
            var colon = userinfo.IndexOf(':');
            if (colon < 0)
            {
                return url;
            }

            var password = userinfo[(colon + 1)..];
            if (password.Length == 0 || RequestDefinitionValidator.IsSecretReference(password))
            {
                return url;
            }

            Warn(SecretNotImported, "The password in the URL was not stored. It was replaced by the variable {{password}}; define it in an environment.", at);
            return url[..start] + userinfo[..colon] + ":" + PasswordVariable + authority[atSign..] + url[end..];
        }

        private void WarnSensitiveQuery(string url, int at)
        {
            var question = url.IndexOf('?');
            if (question < 0)
            {
                return;
            }

            var fragment = url.IndexOf('#', question);
            var query = fragment < 0 ? url[(question + 1)..] : url[(question + 1)..fragment];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                var name = pair[..equals];
                var value = pair[(equals + 1)..];
                if (IsSensitiveName(name) && value.Length > 0 && !value.Contains("{{", StringComparison.Ordinal))
                {
                    Warn(SensitiveValueKept, "A credential in the query was kept as written. Replace it with a variable.", at);
                }
            }
        }

        /// <summary>The name a saved request gets: its method and URL without the query, or the method alone when that is empty.</summary>
        private static string NameFor(string method, string url)
        {
            var cut = url.IndexOfAny(['?', '#']);
            var target = cut < 0 ? url : url[..cut];
            var clean = new string(target.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
            var name = clean.Length == 0 ? method : $"{method} {clean}";
            if (name.Trim().Length == 0)
            {
                return "Imported request";
            }

            return name.Length <= MaxNameLength ? name : name[..MaxNameLength];
        }

        /// <summary>
        /// Splits <c>Name: value</c>. A bare <c>Name;</c> is a header with an empty value. <c>Name:</c> with nothing after it
        /// removes a header in cURL, so it is refused rather than sent as an empty one. A line break in a value is refused,
        /// because it would start a second header on the wire.
        /// </summary>
        private static bool TryParseHeader(string text, out string name, out string value, out string problem)
        {
            var colon = text.IndexOf(':');
            if (colon < 0)
            {
                if (!text.EndsWith(';'))
                {
                    name = string.Empty;
                    value = string.Empty;
                    problem = "A header needs a name and a value, written as Name: value.";
                    return false;
                }

                name = text[..^1].Trim();
                value = string.Empty;
            }
            else
            {
                name = text[..colon].Trim();
                value = text[(colon + 1)..].Trim(' ', '\t');
                if (value.Length == 0)
                {
                    problem = "A header with an empty value removes a header in cURL, so it was not imported.";
                    return false;
                }
            }

            if (name.Length == 0 || !name.All(IsTokenCharacter))
            {
                problem = "A header name may contain only letters, digits and !#$%&'*+-.^_`|~.";
                return false;
            }

            if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
            {
                problem = "A header value cannot contain a line break.";
                return false;
            }

            problem = string.Empty;
            return true;
        }

        private static bool IsTokenCharacter(char character) =>
            char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character);

        private void Unsupported(string name, int at) =>
            Warn(UnsupportedOption, $"{name} is not supported and was not imported.", at);

        /// <summary>A warning about the word at <paramref name="at"/>. The message names the option, never a value.</summary>
        private void Warn(string code, string message, int at) =>
            warnings.Add(new ImportWarning(code, message, $"command[{at}]"));
    }
}
