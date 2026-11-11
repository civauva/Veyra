using System.ComponentModel.DataAnnotations;
using Veyra.Access.History;
using Veyra.Access.SecondFactor;
using Veyra.Access.Sessions;
using Veyra.Core.Security;

namespace Veyra.Access.Api;

/// <summary>The password was right, and another step comes before the session starts (ADR 0011).</summary>
/// <param name="Next"><c>code</c> for a code from the app or a recovery code; <c>setup</c> to set up a second
/// factor that an Admin requires.</param>
public sealed record SignInNextResponse(string Next);

/// <summary>A code from the authenticator app, or a recovery code.</summary>
/// <param name="Code">Six digits, or a recovery code such as <c>K7QF-29XM</c>. Spaces and dashes are ignored.</param>
public sealed record SecondFactorCodeRequest([Required, StringLength(32)] string Code);

/// <summary>What a person needs to add Veyra to their authenticator app. Shown once.</summary>
/// <param name="Secret">The secret in Base32, for typing it in by hand.</param>
/// <param name="Uri">The <c>otpauth://</c> address for the QR code.</param>
public sealed record SecondFactorSetupResponse(string Secret, string Uri);

/// <summary>Ten single-use recovery codes. Shown once.</summary>
/// <param name="RecoveryCodes">The codes.</param>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

/// <summary>A second factor was set up during sign-in: the session started, and its recovery codes.</summary>
/// <param name="Session">The signed-in user.</param>
/// <param name="RecoveryCodes">The codes, shown once.</param>
public sealed record SetupConfirmedResponse(SessionResponse Session, IReadOnlyList<string> RecoveryCodes);

/// <summary>The current password, for changes that should not be made by someone at an unlocked screen.</summary>
/// <param name="Password">The password the person has now.</param>
public sealed record PasswordConfirmationRequest([Required] string Password);

/// <summary>A new password an Admin sets for someone.</summary>
/// <param name="Password">At least 12 characters.</param>
public sealed record ResetPasswordRequest([Required] string Password);

/// <summary>How many sessions were ended.</summary>
/// <param name="Ended">The number.</param>
public sealed record EndedSessionsResponse(int Ended);

/// <summary>Who must sign in with a second factor.</summary>
/// <param name="RequiredFor"><c>Nobody</c>, <c>Admins</c>, or <c>Everyone</c>.</param>
public sealed record SecondFactorPolicyRequest(SecondFactorPolicy RequiredFor);

/// <summary>Who must sign in with a second factor, and who would still have to set one up.</summary>
/// <param name="RequiredFor"><c>Nobody</c>, <c>Admins</c>, or <c>Everyone</c>.</param>
public sealed record SecondFactorPolicyResponse(SecondFactorPolicy RequiredFor);

/// <summary>A switched-on second factor.</summary>
/// <param name="Since">When it was switched on.</param>
/// <param name="RecoveryCodesLeft">Recovery codes not used yet.</param>
public sealed record SecondFactorInfoResponse(DateTimeOffset Since, int RecoveryCodesLeft);

/// <summary>One signed-in browser.</summary>
/// <param name="Id">The session ID, for ending it.</param>
/// <param name="Browser">The browser and system, for example "Firefox on Ubuntu".</param>
/// <param name="Address">The address it was signed in from.</param>
/// <param name="StartedAt">When it was signed in.</param>
/// <param name="LastActiveAt">The last request, to within five minutes.</param>
/// <param name="Method">How it was signed in.</param>
/// <param name="Current">Whether it is the session making this request.</param>
public sealed record SessionInfoResponse(
    string Id,
    string Browser,
    string? Address,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActiveAt,
    SessionMethod Method,
    bool Current);

/// <summary>One sign-in attempt.</summary>
/// <param name="At">When it happened.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Address">The client address.</param>
/// <param name="Browser">The browser and system.</param>
/// <param name="NewAddress">Whether the account had not signed in from this address within the kept history.</param>
public sealed record SignInAttemptResponse(
    DateTimeOffset At,
    SignInOutcome Outcome,
    string? Address,
    string Browser,
    bool NewAddress);

/// <summary>One change to an account.</summary>
/// <param name="At">When it happened.</param>
/// <param name="Kind">What changed.</param>
/// <param name="By">The name of who changed it, or <see langword="null"/> when Veyra did.</param>
/// <param name="Detail">What it changed to, depending on the kind.</param>
public sealed record UserEventResponse(DateTimeOffset At, UserEventKind Kind, string? By, string? Detail);

/// <summary>One person in full, as the person panel of the Users screen shows them.</summary>
/// <param name="User">The account, as the list shows it.</param>
/// <param name="AddedBy">The name of the Admin who added the account, or <see langword="null"/>.</param>
/// <param name="SecondFactor">The second factor when it is on, or <see langword="null"/>.</param>
/// <param name="SecondFactorRequired">Whether the policy requires one of this person.</param>
/// <param name="Sessions">Active sessions, most recently active first.</param>
/// <param name="SignIns">The latest 50 sign-in attempts, newest first.</param>
/// <param name="SignedInLast30Days">Successful sign-ins within 30 days.</param>
/// <param name="FailedLast30Days">Failed attempts within 30 days.</param>
/// <param name="Events">Changes to the account, newest first.</param>
public sealed record UserDetailResponse(
    UserResponse User,
    string? AddedBy,
    SecondFactorInfoResponse? SecondFactor,
    bool SecondFactorRequired,
    IReadOnlyList<SessionInfoResponse> Sessions,
    IReadOnlyList<SignInAttemptResponse> SignIns,
    int SignedInLast30Days,
    int FailedLast30Days,
    IReadOnlyList<UserEventResponse> Events);

/// <summary>The signed-in person's own account, for the Account page.</summary>
/// <param name="Id">The person's ID.</param>
/// <param name="Email">The email address.</param>
/// <param name="DisplayName">The name shown in the UI.</param>
/// <param name="Role">What the person may do.</param>
/// <param name="Team">The team an Admin set, or <see langword="null"/>.</param>
/// <param name="CreatedAt">When the account was added.</param>
/// <param name="LastSignInAt">When the person last signed in.</param>
/// <param name="HasPassword">Whether the account signs in with a local password, so it can change it.</param>
/// <param name="SecondFactor">The second factor when it is on, or <see langword="null"/>.</param>
/// <param name="SecondFactorRequired">Whether an Admin requires one of this person.</param>
/// <param name="Sessions">Active sessions, the current one marked.</param>
/// <param name="SignIns">The latest sign-in attempts, newest first.</param>
public sealed record AccountResponse(
    Guid Id,
    string Email,
    string DisplayName,
    VeyraRole Role,
    string? Team,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt,
    bool HasPassword,
    SecondFactorInfoResponse? SecondFactor,
    bool SecondFactorRequired,
    IReadOnlyList<SessionInfoResponse> Sessions,
    IReadOnlyList<SignInAttemptResponse> SignIns);
