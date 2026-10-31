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

/// <summary>The body of <c>POST /api/v1/organizations/{id}/invite</c>. The role is Owner, Admin or Member (docs/AUTH.md).</summary>
public sealed record InviteMemberRequest(string Email, string Role);

/// <summary>The body of <c>POST /api/v1/organizations/invitations/accept</c>.</summary>
public sealed record AcceptInvitationRequest(string Token);

/// <summary>An organization and its members, from <c>GET /api/v1/organizations/{id}</c>. Only the fields the page shows.</summary>
public sealed record OrganizationDetail(string Id, string Name, List<OrganizationMember> Members);

public sealed record OrganizationMember(string UserId, string Email, string? UserName, string Role);

/// <summary>A pending invitation from <c>GET /api/v1/organizations/{id}/invitations</c>. authservice's answer carries the token too; the Web drops it.</summary>
public sealed record InvitationSummary(string Email, string Role, string EmailStatus);

/// <summary>The answer to <c>POST /api/v1/organizations/invitations/accept</c>: the organization the invitation names.</summary>
public sealed record AcceptedInvitation(string? Message, string OrganizationId);

/// <summary>authservice's failure body for the organization calls: <c>{ "error": "..." }</c>. Other failure shapes read as no text.</summary>
public sealed record AuthErrorBody(string? Error);
