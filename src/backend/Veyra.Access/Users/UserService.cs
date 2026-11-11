using System.Globalization;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Veyra.Access.History;
using Veyra.Access.Sessions;
using Veyra.Core.Security;

namespace Veyra.Access.Users;

/// <summary>
/// Creates and changes local users (ADR 0008), and records each change with who made it (ADR 0011).
/// </summary>
/// <param name="store">Where users are stored.</param>
/// <param name="events">Where changes to accounts are recorded.</param>
/// <param name="sessions">The sessions that changes end.</param>
/// <param name="hasher">The PBKDF2 password hasher from ASP.NET Core Identity.</param>
/// <param name="policy">The password rules.</param>
/// <param name="clock">The clock for creation and change times.</param>
/// <param name="logger">Receives the security events of ADR 0008.</param>
internal sealed partial class UserService(
    UserStore store,
    UserEventStore events,
    SessionStore sessions,
    IPasswordHasher<AccessUser> hasher,
    PasswordPolicy policy,
    TimeProvider clock,
    ILogger<UserService> logger)
{
    /// <summary>Lists all users.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The users, ordered by email.</returns>
    public Task<IReadOnlyList<AccessUser>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    /// <summary>Creates a local user.</summary>
    /// <param name="email">The email address.</param>
    /// <param name="displayName">The display name; the email is used when it is empty.</param>
    /// <param name="role">The role.</param>
    /// <param name="password">The first password.</param>
    /// <param name="team">The team, or <see langword="null"/>.</param>
    /// <param name="actorId">The Admin who adds the user, or <see langword="null"/> when Veyra does.</param>
    /// <param name="cancellationToken">Cancels the creation.</param>
    /// <returns>The new user.</returns>
    /// <exception cref="AccessValidationException">The email, password, or team is not acceptable.</exception>
    /// <exception cref="AccessConflictException">A user with this email already exists.</exception>
    public async Task<AccessUser> CreateAsync(
        string email,
        string displayName,
        VeyraRole role,
        string password,
        string? team,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var user = NewUser(email, displayName, role, password, policy, hasher, clock) with { Team = CleanTeam(team) };

        await store.InsertAsync(user, cancellationToken);
        await events.RecordAsync(
            user.Id,
            user.CreatedAt,
            UserEventKind.Added,
            actorId,
            role.ToString(),
            cancellationToken);

        LogUserCreated(user.Id, user.Role);

        return user;
    }

    /// <summary>
    /// Changes a user's role, team, or whether they can sign in. Switching someone off ends their sessions.
    /// </summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="role">The new role, or <see langword="null"/> to keep it.</param>
    /// <param name="disabled">The new disabled flag, or <see langword="null"/> to keep it.</param>
    /// <param name="team">The new team, an empty string to remove it, or <see langword="null"/> to keep it.</param>
    /// <param name="actorId">The Admin who makes the change.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The changed user, or <see langword="null"/> when no user has that ID.</returns>
    /// <exception cref="AccessConflictException">The change would leave no active administrator.</exception>
    /// <exception cref="AccessValidationException">The team is longer than allowed.</exception>
    public async Task<AccessUser?> UpdateAsync(
        Guid id,
        VeyraRole? role,
        bool? disabled,
        string? team,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var newTeam = team is null ? null : CleanTeam(team) ?? string.Empty;
        var user = await store.FindByIdAsync(id, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var isActiveAdmin = user.Role == VeyraRole.Admin && !user.Disabled;
        var staysActiveAdmin = (role ?? user.Role) == VeyraRole.Admin && !(disabled ?? user.Disabled);

        if (isActiveAdmin && !staysActiveAdmin && await store.CountActiveAdminsAsync(cancellationToken) <= 1)
        {
            throw new AccessConflictException("Veyra needs at least one active administrator.");
        }

        await store.UpdateAsync(id, role, disabled, newTeam, cancellationToken);

        var updated = user with
        {
            Role = role ?? user.Role,
            Disabled = disabled ?? user.Disabled,
            Team = newTeam is null ? user.Team : newTeam.Length == 0 ? null : newTeam,
        };
        var now = clock.GetUtcNow();

        if (updated.Role != user.Role)
        {
            await events.RecordAsync(
                id,
                now,
                UserEventKind.RoleChanged,
                actorId,
                $"{user.Role} {updated.Role}",
                cancellationToken);
            LogRoleChanged(id, user.Role, updated.Role);
        }

        if (updated.Team != user.Team)
        {
            await events.RecordAsync(
                id,
                now,
                UserEventKind.TeamChanged,
                actorId,
                updated.Team ?? string.Empty,
                cancellationToken);
        }

        if (updated.Disabled != user.Disabled)
        {
            var kind = updated.Disabled ? UserEventKind.SwitchedOff : UserEventKind.SwitchedOn;
            await events.RecordAsync(id, now, kind, actorId, null, cancellationToken);
            LogDisabledChanged(id, updated.Disabled);

            if (updated.Disabled)
            {
                await sessions.EndAllAsync(id, null, now, cancellationToken);
            }
        }

        return updated;
    }

    /// <summary>
    /// Changes a user's own password after checking the current one, and signs them out on every other browser.
    /// </summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="currentPassword">The password the user has now.</param>
    /// <param name="newPassword">The password to set.</param>
    /// <param name="currentSessionId">
    /// The session making the change, which stays; <see langword="null"/> ends all.
    /// </param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns><see langword="true"/> when the password was changed; <see langword="false"/> when the current one
    /// was wrong.</returns>
    /// <exception cref="AccessValidationException">The new password is not acceptable.</exception>
    public async Task<bool> ChangePasswordAsync(
        Guid id,
        string currentPassword,
        string newPassword,
        string? currentSessionId,
        CancellationToken cancellationToken)
    {
        if (policy.Check(newPassword) is { } problem)
        {
            throw new AccessValidationException(problem);
        }

        var user = await store.FindByIdAsync(id, cancellationToken);

        if (user is null || !PasswordMatches(user, currentPassword))
        {
            return false;
        }

        var now = clock.GetUtcNow();

        await store.SetPasswordHashAsync(id, hasher.HashPassword(user, newPassword), cancellationToken);
        await sessions.EndAllAsync(id, currentSessionId, now, cancellationToken);
        await events.RecordAsync(id, now, UserEventKind.PasswordChanged, id, null, cancellationToken);

        LogPasswordChanged(id);

        return true;
    }

    /// <summary>
    /// Sets a new password for someone else, for a forgotten password or a lock. It lifts the lock and signs the
    /// person out everywhere.
    /// </summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="newPassword">The password to set.</param>
    /// <param name="actorId">The Admin who resets it.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns><see langword="true"/> when the user exists.</returns>
    /// <exception cref="AccessValidationException">The new password is not acceptable.</exception>
    public async Task<bool> ResetPasswordAsync(
        Guid id,
        string newPassword,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        if (policy.Check(newPassword) is { } problem)
        {
            throw new AccessValidationException(problem);
        }

        var user = await store.FindByIdAsync(id, cancellationToken);

        if (user is null)
        {
            return false;
        }

        var now = clock.GetUtcNow();

        await store.SetPasswordHashAsync(id, hasher.HashPassword(user, newPassword), cancellationToken);
        await sessions.EndAllAsync(id, null, now, cancellationToken);
        await events.RecordAsync(id, now, UserEventKind.PasswordReset, actorId, null, cancellationToken);

        LogPasswordReset(id, actorId);

        return true;
    }

    /// <summary>
    /// Checks a user's password, for changes that need it again, such as switching off a second factor.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="password">The password as typed.</param>
    /// <returns><see langword="true"/> when it is right.</returns>
    public bool PasswordMatches(AccessUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.PasswordHash is not null
            && hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
    }

    /// <summary>
    /// Validates the input and builds a new local user with a hashed password. Shared with first-run setup.
    /// </summary>
    /// <param name="email">The email address.</param>
    /// <param name="displayName">The display name; the email is used when it is empty.</param>
    /// <param name="role">The role.</param>
    /// <param name="password">The password in plain text.</param>
    /// <param name="policy">The password rules.</param>
    /// <param name="hasher">The password hasher.</param>
    /// <param name="clock">The clock for the creation time.</param>
    /// <returns>The user, not yet stored.</returns>
    /// <exception cref="AccessValidationException">The email or password is not acceptable.</exception>
    internal static AccessUser NewUser(
        string email,
        string displayName,
        VeyraRole role,
        string password,
        PasswordPolicy policy,
        IPasswordHasher<AccessUser> hasher,
        TimeProvider clock)
    {
        var trimmedEmail = email.Trim();

        // MailAddress also accepts display-name forms such as "Ana <ana@example.com>", so the parsed address must
        // equal the whole input.
        if (trimmedEmail.Length > 254
            || !MailAddress.TryCreate(trimmedEmail, out var parsed)
            || parsed.Address != trimmedEmail)
        {
            throw new AccessValidationException("The email address is not valid.");
        }

        if (policy.Check(password) is { } problem)
        {
            throw new AccessValidationException(problem);
        }

        var user = new AccessUser(
            Id: Guid.CreateVersion7(),
            Email: trimmedEmail,
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? trimmedEmail : displayName.Trim(),
            Role: role,
            Disabled: false,
            PasswordHash: null,
            FailedSignIns: 0,
            LockedUntil: null,
            CreatedAt: clock.GetUtcNow(),
            LastSignInAt: null);

        return user with { PasswordHash = hasher.HashPassword(user, password) };
    }

    // An empty team is no team. Longer than the limit is refused rather than cut, so nobody loses part of a name.
    private static string? CleanTeam(string? team)
    {
        var trimmed = team?.Trim();

        if (trimmed is { Length: > AccessUser.MaxTeamLength })
        {
            throw new AccessValidationException(string.Format(
                CultureInfo.InvariantCulture,
                "The team can have at most {0} characters.",
                AccessUser.MaxTeamLength));
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: user {UserId} created with role {Role}")]
    private partial void LogUserCreated(Guid userId, VeyraRole role);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Security: user {UserId} role changed from {OldRole} to {NewRole}")]
    private partial void LogRoleChanged(Guid userId, VeyraRole oldRole, VeyraRole newRole);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: user {UserId} disabled set to {Disabled}")]
    private partial void LogDisabledChanged(Guid userId, bool disabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Security: user {UserId} changed their password")]
    private partial void LogPasswordChanged(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: password of user {UserId} reset by {ActorId}")]
    private partial void LogPasswordReset(Guid userId, Guid actorId);
}
