namespace PostmanClone.Web.Auth;

/// <summary>
/// The messages the sign-in and sign-up pages show for the codes the endpoints redirect with. The text never says which
/// of "no such account" and "wrong password" happened (IDENTITY-AND-ACCOUNTS §5), and a code is the only thing a URL carries.
/// </summary>
public static class AuthMessages
{
    /// <summary>The message for a code, or null when there is none. <c>IsError</c> picks the alert colour.</summary>
    public static (string Text, bool IsError)? For(string? error, string? notice) => error switch
    {
        "invalid" => ("Email or password is not right.", true),
        "unavailable" => ("The sign-in service could not be reached. Try again shortly.", true),
        "two-factor" => ("Two-factor sign-in is not supported yet.", true),
        "details" => ("Enter an email and a password, and accept both documents.", true),
        "rejected" => ("The account could not be created. Check the details and try again.", true),
        "ended" => ("Your session has ended. Sign in again.", true),
        "name" => ("Enter a workspace name of up to 100 characters.", true),
        "create" => ("The workspace could not be created. Try again shortly.", true),
        _ => notice == "verify" ? ("Check your email to confirm the address, then sign in.", false) : null,
    };
}
