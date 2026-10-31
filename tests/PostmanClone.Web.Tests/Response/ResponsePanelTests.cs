using System.Diagnostics;
using System.Text;
using Bunit;
using PostmanClone.Web.Api;
using PostmanClone.Web.Components.Response;
using PostmanClone.Web.Response;
using static PostmanClone.Web.Tests.Response.ResponseViewTests;

namespace PostmanClone.Web.Tests.Response;

/// <summary>
/// The response panel, rendered (WP20 acceptance, bUnit). The response is untrusted, so the markup it produces must hold the text
/// encoded and no element a response could make. The states show their words; a failure is an alert; a large body draws in chunks
/// within a generous budget.
/// </summary>
public sealed class ResponsePanelTests
{
    private static SendView Shown(ExecutionResultDto result) => new(SendPhase.Shown, Guid.NewGuid(), ResponseView.From(result), null);

    [Fact]
    public void Markup_in_a_body_is_written_as_encoded_text_and_no_element_is_created()
    {
        using var context = new BunitContext();
        var body = "<img src=x onerror=alert(1)></script><script>alert(1)</script>";

        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, Shown(Result(response: ResponseOf(body: body, contentType: "text/html")))));

        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", cut.Markup);
        Assert.Contains("&lt;/script&gt;", cut.Markup);
        Assert.DoesNotContain("<img src=x", cut.Markup);
        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public void Markup_in_a_header_value_is_written_as_encoded_text()
    {
        using var context = new BunitContext();
        var headers = new List<HeaderLine> { new("X-Echo", "<b onmouseover=alert(1)>hi</b>") };
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, Shown(Result(response: ResponseOf(headers: headers)))));

        cut.Find("[data-testid='response-tab-headers']").Click();

        Assert.Contains("&lt;b onmouseover=alert(1)&gt;hi&lt;/b&gt;", cut.Markup);
        Assert.Empty(cut.FindAll("b"));
        Assert.Equal("<b onmouseover=alert(1)>hi</b>", cut.Find("[data-testid='response-headers'] td").TextContent);
    }

    [Fact]
    public void A_refusal_names_its_category_in_an_alert()
    {
        using var context = new BunitContext();
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, Shown(Result("blocked", code: "ssrf_blocked_address", message: "Target blocked."))));

        var outcome = cut.Find("[data-testid='response-outcome']");
        Assert.Equal("alert", outcome.GetAttribute("role"));
        Assert.Contains("Request blocked", outcome.TextContent);
        Assert.Contains("private, loopback or link-local", outcome.TextContent);
        Assert.Contains("Target blocked.", outcome.TextContent);
        Assert.Contains("ssrf_blocked_address", outcome.TextContent);
        Assert.Empty(cut.FindAll("[data-testid='response-status']"));
    }

    [Fact]
    public void A_send_that_produced_no_answer_is_an_alert_with_its_words()
    {
        using var context = new BunitContext();
        var view = new SendView(SendPhase.Failed, Guid.NewGuid(), null, "The sender could not be reached, or the environments service failed. Try again in a moment.");

        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, view));

        var error = cut.Find("[data-testid='response-error']");
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Contains("could not be reached", error.TextContent);
    }

    [Fact]
    public void While_sending_the_panel_says_so_and_shows_no_result()
    {
        using var context = new BunitContext();
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, new SendView(SendPhase.Sending, Guid.NewGuid(), null, null)));

        Assert.Equal("Sending…", cut.Find("[data-testid='response-sending']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='response-body']"));
    }

    [Fact]
    public void A_cancelled_send_is_a_status_not_an_alert()
    {
        using var context = new BunitContext();
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, new SendView(SendPhase.Cancelled, Guid.NewGuid(), null, ResponseMessages.Cancelled)));

        var status = cut.Find("[data-testid='response-cancelled']");
        Assert.Equal(ResponseMessages.Cancelled, status.TextContent);
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact]
    public void A_json_body_is_drawn_with_token_spans_one_line_per_member()
    {
        using var context = new BunitContext();
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, Shown(Result(response: ResponseOf(body: "{\"name\":\"x\",\"count\":2}")))));

        var lines = cut.FindAll("[data-testid='response-body'] .response-line");
        Assert.Equal(4, lines.Count);
        Assert.Equal(2, cut.FindAll("[data-testid='response-body'] .json-key").Count);
        Assert.Equal("\"name\"", cut.FindAll("[data-testid='response-body'] .json-key")[0].TextContent);
        Assert.Equal("2", cut.Find("[data-testid='response-body'] .json-number").TextContent);
    }

    [Fact]
    public void A_status_is_badged_by_its_family_and_carries_its_text()
    {
        using var context = new BunitContext();
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, Shown(Result(response: ResponseOf(status: 500)))));

        var badge = cut.Find("[data-testid='response-status']");
        Assert.Equal("500 OK", badge.TextContent);
        Assert.Contains("badge-danger", badge.ClassName);
    }

    [Fact]
    public void A_binary_body_is_a_size_note_and_its_download_is_disabled()
    {
        using var context = new BunitContext();
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, Shown(Result(response: ResponseOf(body: "AAEC", contentType: "image/png", encoding: "base64")))));

        Assert.Contains("4 bytes, base64 hidden", cut.Find("[data-testid='response-binary']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='response-body']"));
        Assert.True(cut.Find("[data-testid='response-download']").HasAttribute("disabled"));
    }

    [Fact]
    public void A_truncated_body_shows_its_marker()
    {
        using var context = new BunitContext();
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, Shown(Result(response: ResponseOf(body: "{}", truncated: true)))));

        Assert.Contains("cut at the size limit", cut.Find("[data-testid='response-truncated']").TextContent);
    }

    [Fact]
    public void Preview_is_disabled_and_says_why()
    {
        using var context = new BunitContext();
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, Shown(Result(response: ResponseOf(body: "<p>x</p>", contentType: "text/html")))));

        Assert.True(cut.Find("[data-testid='response-mode-preview']").HasAttribute("disabled"));
        Assert.Contains("never rendered here", cut.Find("[data-testid='response-preview-note']").TextContent);
    }

    [Fact]
    public void An_unknown_outcome_is_named_and_the_panel_does_not_claim_success()
    {
        using var context = new BunitContext();
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, Shown(Result("futureOutcome"))));

        Assert.Contains("Unexpected result", cut.Find("[data-testid='response-outcome']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='response-status']"));
    }

    [Fact]
    public void Unresolved_variables_are_listed_with_their_braces()
    {
        using var context = new BunitContext();
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, Shown(Result(response: ResponseOf(), unresolved: ["base", "token"]))));

        var list = cut.Find("[data-testid='response-unresolved']").TextContent;
        Assert.Contains("{{base}}", list);
        Assert.Contains("{{token}}", list);
    }

    [Fact]
    public void A_five_megabyte_truncated_body_draws_in_chunks_within_the_budget()
    {
        using var context = new BunitContext();
        var builder = new StringBuilder("[");
        for (var i = 0; builder.Length < 5 * 1024 * 1024; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"id\":").Append(i).Append(",\"name\":\"item\"}");
        }

        builder.Append(']');
        var body = builder.ToString();
        var started = Stopwatch.GetTimestamp();

        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, Shown(Result(response: ResponseOf(body: body, truncated: true)))));

        var elapsed = Stopwatch.GetElapsedTime(started);
        Assert.Equal(ResponsePresentation.LinesPerChunk, cut.FindAll("[data-testid='response-body'] .response-line").Count);
        Assert.Single(cut.FindAll("[data-testid='response-truncated']"));
        Assert.True(elapsed < TimeSpan.FromSeconds(60), $"Rendering took {elapsed}.");

        cut.Find("[data-testid='response-body-more']").Click();
        Assert.Equal(ResponsePresentation.LinesPerChunk * 2, cut.FindAll("[data-testid='response-body'] .response-line").Count);
    }

    [Fact]
    public void A_new_send_starts_on_the_body_tab_in_the_pretty_view()
    {
        using var context = new BunitContext();
        var first = Shown(Result(response: ResponseOf(body: "{\"a\":1}")));
        var cut = context.Render<ResponsePanel>(p => p.Add(c => c.View, first));
        cut.Find("[data-testid='response-tab-timing']").Click();
        Assert.Empty(cut.FindAll("[data-testid='response-body']"));

        cut.Render(p => p.Add(c => c.View, Shown(Result(response: ResponseOf(body: "{\"b\":2}")))));

        Assert.Single(cut.FindAll("[data-testid='response-body']"));
    }
}
