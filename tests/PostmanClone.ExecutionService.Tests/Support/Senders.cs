using PostmanClone.ExecutionService.Options;
using PostmanClone.Sending.Ssrf;

namespace PostmanClone.ExecutionService.Tests.Support;

/// <summary>
/// Builds the shared sender's URL checks from the service's options, the way ExecutionService wires them: the SSRF rules
/// (no own listener) and the configured caps. The tests that used to take the options object use these.
/// </summary>
internal static class Senders
{
    public static TargetValidator Validator(ExecutionOptions options) =>
        new(options.UrlMaxLength, new OutboundRules(options.Ssrf));

    public static RedirectPlanner Planner(ExecutionOptions options) =>
        new(Validator(options), options.MaxRedirects);
}
