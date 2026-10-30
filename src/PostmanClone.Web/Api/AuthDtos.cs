namespace PostmanClone.Web.Api;

// Copies of authservice's request and response shapes (v0.3.4, docs/AUTH.md). The Web cannot reference authservice's
// types, so these are written by hand from its source and pinned by AuthDtoTests against the JSON it sends.

public sealed record LoginRequest(string Email, string Password);

public sealed record RegisterRequest(string Email, string Password, string AcceptedTermsVersion, string AcceptedPrivacyVersion);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record CreateOrganizationRequest(string Name);

/// <summary>The token pair. <c>expiresIn</c> is seconds (an integer, not a TimeSpan).</summary>
public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType);

public sealed record ConsentVersionsResponse(string Terms, string Privacy, string Cookies);

/// <summary>The profile from <c>GET /api/v1/auth/me</c>. Only the fields the Web reads are copied.</summary>
public sealed record UserInfoResponse(string Id, string Email, List<UserOrganization> Organizations);

public sealed record UserOrganization(string Id, string Name, string? ImageUrl, string Role);
