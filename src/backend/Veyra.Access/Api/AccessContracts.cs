using System.ComponentModel.DataAnnotations;
using Veyra.Core.Security;

namespace Veyra.Access.Api;

/// <summary>Whether first-run setup is still needed.</summary>
/// <param name="Required"><see langword="true"/> while no user exists.</param>
public sealed record SetupStatusResponse(bool Required);

/// <summary>Creates the first administrator with the token from the setup link.</summary>
/// <param name="Token">The token from the link in Veyra's log.</param>
/// <param name="Email">The administrator's email address.</param>
/// <param name="DisplayName">The name shown in the UI.</param>
/// <param name="Password">At least 12 characters.</param>
public sealed record SetupRequest(
    [Required] string Token,
    [Required, StringLength(254)] string Email,
    [StringLength(200)] string DisplayName,
    [Required] string Password);

/// <summary>Signs in with a local account.</summary>
/// <param name="Email">The email address.</param>
/// <param name="Password">The password.</param>
public sealed record SignInRequest([Required] string Email, [Required] string Password);

/// <summary>The signed-in user.</summary>
/// <param name="Id">The user's ID.</param>
/// <param name="Email">The email address.</param>
/// <param name="DisplayName">The name shown in the UI.</param>
/// <param name="Role">What the user may do.</param>
public sealed record SessionResponse(Guid Id, string Email, string DisplayName, VeyraRole Role);

/// <summary>The token to send in the antiforgery header on requests that change state.</summary>
/// <param name="Token">The request token. Fetch a new one after signing in.</param>
public sealed record AntiforgeryResponse(string Token);

/// <summary>Changes the signed-in user's own password.</summary>
/// <param name="CurrentPassword">The password the user has now.</param>
/// <param name="NewPassword">At least 12 characters.</param>
public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required] string NewPassword);

/// <summary>A user, as administrators see it.</summary>
/// <param name="Id">The user's ID.</param>
/// <param name="Email">The email address.</param>
/// <param name="DisplayName">The name shown in the UI.</param>
/// <param name="Role">What the user may do.</param>
/// <param name="Disabled">Whether the user is blocked from signing in.</param>
/// <param name="CreatedAt">When the user was created.</param>
/// <param name="LastSignInAt">When the user last signed in, or <see langword="null"/> if never.</param>
/// <param name="Team">The team an Admin set, or <see langword="null"/>.</param>
/// <param name="LockedUntil">When a lock after wrong attempts ends, or <see langword="null"/> when not locked.</param>
/// <param name="SecondFactorSince">When the second factor was switched on, or <see langword="null"/>.</param>
/// <param name="ActiveSessions">How many browsers are signed in now.</param>
/// <param name="LastActiveAt">The newest activity of any session, or <see langword="null"/>.</param>
public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    VeyraRole Role,
    bool Disabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt,
    string? Team,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? SecondFactorSince,
    int ActiveSessions,
    DateTimeOffset? LastActiveAt);

/// <summary>Creates a local user.</summary>
/// <param name="Email">The email address the person signs in with.</param>
/// <param name="DisplayName">The name shown in the UI.</param>
/// <param name="Role">What the user may do.</param>
/// <param name="Password">The initial password, at least 12 characters.</param>
/// <param name="Team">The team, or <see langword="null"/>.</param>
public sealed record CreateUserRequest(
    [Required, StringLength(254)] string Email,
    [StringLength(200)] string DisplayName,
    VeyraRole Role,
    [Required] string Password,
    [StringLength(100)] string? Team = null);

/// <summary>Changes a user's role, team, or disabled flag. Fields left out stay as they are.</summary>
/// <param name="Role">The new role.</param>
/// <param name="Disabled">Whether the user is blocked from signing in. Switching off ends every session.</param>
/// <param name="Team">The new team; an empty string removes it.</param>
public sealed record UpdateUserRequest(VeyraRole? Role, bool? Disabled, [StringLength(100)] string? Team = null);

/// <summary>An ingestion key, without its secret.</summary>
/// <param name="Id">The key's ID.</param>
/// <param name="Name">What the key is for.</param>
/// <param name="DisplayPrefix">The start of the key, for example <c>vyk_AbCdEfGh</c>.</param>
/// <param name="Environments">The environments the key may send data for; <see langword="null"/> means all.</param>
/// <param name="CreatedAt">When the key was created.</param>
/// <param name="ExpiresAt">When the key stops working by itself.</param>
/// <param name="RevokedAt">When the key was revoked.</param>
/// <param name="LastUsedAt">When a sender last presented the key.</param>
public sealed record IngestionKeyResponse(
    Guid Id,
    string Name,
    string DisplayPrefix,
    IReadOnlyList<string>? Environments,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>Creates an ingestion key.</summary>
/// <param name="Name">What the key is for, for example <c>web-01 collector</c>.</param>
/// <param name="Environments">The environments the key may send data for; leave out for all.</param>
/// <param name="ExpiresAt">When the key stops working by itself; leave out for never.</param>
public sealed record CreateIngestionKeyRequest(
    [Required, StringLength(200)] string Name,
    IReadOnlyList<string>? Environments,
    DateTimeOffset? ExpiresAt);

/// <summary>A new ingestion key with its secret. The secret is never shown again.</summary>
/// <param name="Id">The key's ID.</param>
/// <param name="Name">What the key is for.</param>
/// <param name="DisplayPrefix">The start of the key, for example <c>vyk_AbCdEfGh</c>.</param>
/// <param name="Environments">The environments the key may send data for; <see langword="null"/> means all.</param>
/// <param name="ExpiresAt">When the key stops working by itself.</param>
/// <param name="Secret">The full key to configure in the sender.</param>
public sealed record CreatedIngestionKeyResponse(
    Guid Id,
    string Name,
    string DisplayPrefix,
    IReadOnlyList<string>? Environments,
    DateTimeOffset? ExpiresAt,
    string Secret);
