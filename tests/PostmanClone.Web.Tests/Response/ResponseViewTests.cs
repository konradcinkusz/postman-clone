using PostmanClone.Web.Api;
using PostmanClone.Web.Components.Response;
using PostmanClone.Web.Response;

namespace PostmanClone.Web.Tests.Response;

/// <summary>
/// The response model and its words (WP20): each refusal gets its own explanation, an unknown code is named as unknown rather than
/// called "failed", the formats are exact, and the body views are chosen from the content type. Pure logic, no rendering.
/// </summary>
public sealed class ResponseViewTests
{
    public static ResponseDto ResponseOf(int status = 200, string body = "{}", string? contentType = "application/json", string encoding = "utf8", bool truncated = false, List<HeaderLine>? headers = null) =>
        new(status, "OK", "HTTP/1.1", headers ?? [], new ResponseBodyDto(encoding, body, body.Length, truncated, contentType));

    public static ExecutionResultDto Result(string outcome = "completed", ResponseDto? response = null, string? code = null, string? message = null, List<string>? unresolved = null) =>
        new(outcome, code, message, response, new ExecutionTiming(120, 4, 9, 30, 2), "https://api.example.test/items", [], unresolved ?? [], null);

    [Theory]
    [InlineData("ssrf_blocked_address", "Request blocked", "private, loopback or link-local")]
    [InlineData("ssrf_blocked_scheme", "Request blocked", "only sends http and https")]
    [InlineData("ssrf_blocked_port", "Request blocked", "database or a mail service")]
    [InlineData("dns_failed", "Request blocked", "could not be resolved")]
    [InlineData("too_many_redirects", "Request blocked", "longer than the sender follows")]
    [InlineData("invalid_url", "Request blocked", "not one the sender can send")]
    public void Each_refusal_code_has_its_own_explanation(string code, string heading, string words)
    {
        var view = ResponseView.From(Result("blocked", code: code, message: "Blocked."));

        Assert.Equal(OutcomeKind.Blocked, view.Outcome);
        Assert.Equal(heading, view.Heading);
        Assert.Contains(words, view.Explanation);
        Assert.Equal("Blocked.", view.ServiceMessage);
    }

    [Fact]
    public void A_refusal_with_an_unknown_code_is_still_a_refusal_not_a_generic_failure()
    {
        var view = ResponseView.From(Result("blocked", code: "something_new"));

        Assert.Equal("Request blocked", view.Heading);
        Assert.Equal("The sender refused this request.", view.Explanation);
    }

    [Theory]
    [InlineData("timeout", null, "Timed out")]
    [InlineData("networkError", "network_error", "Network error")]
    [InlineData("networkError", "invalid_body_encoding", "Network error")]
    [InlineData("invalidRequest", "invalid_request", "Request not valid")]
    [InlineData("tooLarge", null, "Response too large")]
    public void Each_failure_outcome_is_named(string outcome, string? code, string heading)
    {
        var view = ResponseView.From(Result(outcome, code: code));

        Assert.Equal(heading, view.Heading);
        Assert.NotEqual("failed", view.Heading.ToLowerInvariant());
    }

    [Fact]
    public void An_unknown_outcome_is_named_as_unknown_and_never_shown_as_success()
    {
        var view = ResponseView.From(Result("somethingElse"));

        Assert.Equal(OutcomeKind.Unknown, view.Outcome);
        Assert.Equal("Unexpected result", view.Heading);
        Assert.NotEqual(OutcomeKind.Completed, view.Outcome);
    }

    [Fact]
    public void A_completed_send_has_no_failure_heading()
    {
        var view = ResponseView.From(Result(response: ResponseOf(status: 404)));

        Assert.Equal(OutcomeKind.Completed, view.Outcome);
        Assert.Equal("Response received", view.Heading);
        Assert.Equal("404 OK", view.StatusText);
        Assert.Equal(StatusFamily.ClientError, view.Family);
        Assert.Equal("badge badge-danger", view.BadgeClass);
    }

    [Theory]
    [InlineData(200, StatusFamily.Success, "badge badge-success")]
    [InlineData(301, StatusFamily.Redirect, "badge badge-accent")]
    [InlineData(503, StatusFamily.ServerError, "badge badge-danger")]
    [InlineData(102, StatusFamily.Informational, "badge badge-accent")]
    [InlineData(999, StatusFamily.Unknown, "badge")]
    [InlineData(0, StatusFamily.None, "badge")]
    public void Status_families_map_to_their_badge_class(int status, StatusFamily family, string badge)
    {
        Assert.Equal(family, ResponseFormat.Family(status));
        Assert.Equal(badge, ResponseFormat.BadgeClass(family));
    }

    [Theory]
    [InlineData(0, "0 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(1000, "1.00 s")]
    [InlineData(1500, "1.50 s")]
    public void Times_are_written_in_milliseconds_then_seconds(int milliseconds, string text)
    {
        Assert.Equal(text, ResponseFormat.Milliseconds(milliseconds));
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    public void Sizes_are_written_in_bytes_kilobytes_then_megabytes(long bytes, string text)
    {
        Assert.Equal(text, ResponseFormat.Bytes(bytes));
    }

    [Fact]
    public void A_status_line_without_a_reason_phrase_is_the_code_alone()
    {
        Assert.Equal("204", ResponseFormat.StatusLine(204, string.Empty));
        Assert.Equal("204 No Content", ResponseFormat.StatusLine(204, "No Content"));
    }

    [Fact]
    public void A_json_body_is_pretty_and_a_plain_text_body_is_not()
    {
        var json = ResponseView.From(Result(response: ResponseOf(body: "{\"a\":1}", contentType: "application/problem+json")));
        var text = ResponseView.From(Result(response: ResponseOf(body: "{\"a\":1}", contentType: "text/plain")));

        Assert.True(json.Body!.LooksJson);
        Assert.True(json.PrettyIsJson);
        Assert.False(text.Body!.LooksJson);
    }

    [Fact]
    public void With_no_content_type_a_body_that_starts_like_json_is_read_as_json()
    {
        var view = ResponseView.From(Result(response: ResponseOf(body: "  [1,2]", contentType: null)));

        Assert.True(view.Body!.LooksJson);
        Assert.True(view.PrettyIsJson);
    }

    [Fact]
    public void A_json_declared_body_that_is_not_json_falls_back_to_raw_and_says_so()
    {
        var view = ResponseView.From(Result(response: ResponseOf(body: "not json at all", contentType: "application/json")));

        Assert.False(view.PrettyIsJson);
        var lines = view.Lines(BodyMode.Pretty);
        Assert.Equal("not json at all", lines[0][0].Text);
    }

    [Fact]
    public void Preview_has_no_lines_because_html_is_never_rendered()
    {
        var view = ResponseView.From(Result(response: ResponseOf(body: "<html><script>x()</script></html>", contentType: "text/html")));

        Assert.Empty(view.Lines(BodyMode.Preview));
    }

    [Fact]
    public void A_binary_body_has_no_text_lines_and_its_size_is_shown()
    {
        var view = ResponseView.From(Result(response: ResponseOf(body: "AAEC", contentType: "application/octet-stream", encoding: "base64")));

        Assert.True(view.Body!.IsBinary);
        Assert.Empty(view.Lines(BodyMode.Raw));
        Assert.Equal("4 B", view.SizeText);
    }

    [Fact]
    public void A_refusal_without_a_response_has_no_body_and_no_status()
    {
        var view = ResponseView.From(Result("blocked", code: "dns_failed"));

        Assert.False(view.HasResponse);
        Assert.Null(view.Body);
        Assert.Equal(string.Empty, view.StatusText);
        Assert.Empty(view.Lines(BodyMode.Pretty));
    }

    [Fact]
    public void A_zero_phase_timing_reads_as_not_measured()
    {
        Assert.Equal("not measured", ResponsePresentation.Phase(0));
        Assert.Equal("30 ms", ResponsePresentation.Phase(30));
    }

    [Fact]
    public void Lines_show_in_chunks_and_the_rest_wait_behind_show_more()
    {
        Assert.Equal(ResponsePresentation.LinesPerChunk, ResponsePresentation.LinesShown(1, 10_000));
        Assert.Equal(10_000, ResponsePresentation.LinesShown(100, 10_000));
        Assert.Equal(3, ResponsePresentation.LinesShown(1, 3));
        Assert.Equal("{{api_key}}", ResponsePresentation.VariableText("api_key"));
    }

    [Fact]
    public void A_truncated_body_keeps_its_marker()
    {
        var view = ResponseView.From(Result(response: ResponseOf(truncated: true)));

        Assert.True(view.Body!.Truncated);
    }
}
