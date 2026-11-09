using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Veyra.Core.Security;

namespace Veyra.Access.Users;

/// <summary>
/// Creates and changes users. Administrators reach it through the users endpoints; every user reaches
/// <see cref="ChangePasswordAsync"/> for their own password.
/// </summary>
/// <param name="store">Where users are stored.</param>
/// <param name="hasher">The PBKDF2 password hasher from ASP.NET Core Identity.</param>
/// <param name="policy">The password rules.</param>
/// <param name="clock">The clock for creation times.</param>
/// <param name="logger">Receives the security events of ADR 0008.</param>
internal sealed partial class UserService(
    UserStore store,
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

    /// <summary>
    /// Creates a local user.
    /// </summary>
    /// <param name="email">The email address the person signs in with.</param>
    /// <param name="displayName">The name shown in the UI.</param>
    /// <param name="role">What the user may do.</param>
    /// <param name="password">The initial password.</param>
    /// <param name="cancellationToken">Cancels the creation.</param>
    /// <returns>The new user.</returns>
    /// <exception cref="AccessValidationException">The email or password is not acceptable.</exception>
    /// <exception cref="AccessConflictException">The email is already taken.</exception>
    public async Task<AccessUser> CreateAsync(
        string email,
        string displayName,
        VeyraRole role,
        string password,
        CancellationToken cancellationToken)
    {
        var user = NewUser(email, displayName, role, password, policy, hasher, clock);

        await store.InsertAsync(user, cancellationToken);

        LogUserCreated(user.Id, user.Role);

        return user;
    }

    /// <summary>
    /// Changes a user's role, disabled flag, or both. Refuses to remove the last active administrator, because nobody
    /// could manage Veyra afterwards.
    /// </summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="role">The new role, or <see langword="null"/> to keep it.</param>
    /// <param name="disabled">The new disabled flag, or <see langword="null"/> to keep it.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The changed user, or <see langword="null"/> when no user has that ID.</returns>
    /// <exception cref="AccessConflictException">The change would leave no active administrator.</exception>
    public async Task<AccessUser?> UpdateAsync(
        Guid id,
        VeyraRole? role,
        bool? disabled,
        CancellationToken cancellationToken)
    {
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

        await store.UpdateAsync(id, role, disabled, cancellationToken);

        var updated = user with { Role = role ?? user.Role, Disabled = disabled ?? user.Disabled };

        if (updated.Role != user.Role)
        {
            LogRoleChanged(id, user.Role, updated.Role);
        }

        if (updated.Disabled != user.Disabled)
        {
            LogDisabledChanged(id, updated.Disabled);
        }

        return updated;
    }

    /// <summary>
    /// Changes a user's own password after checking the current one.
    /// </summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="currentPassword">The password the user has now.</param>
    /// <param name="newPassword">The password to set.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns><see langword="true"/> when the password was changed; <see langword="false"/> when the current one
    /// was wrong.</returns>
    /// <exception cref="AccessValidationException">The new password is not acceptable.</exception>
    public async Task<bool> ChangePasswordAsync(
        Guid id,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (policy.Check(newPassword) is { } problem)
        {
            throw new AccessValidationException(problem);
        }

        var user = await store.FindByIdAsync(id, cancellationToken);

        if (user?.PasswordHash is null
            || hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword)
                == PasswordVerificationResult.Failed)
        {
            return false;
        }

        await store.SetPasswordHashAsync(id, hasher.HashPassword(user, newPassword), cancellationToken);

        LogPasswordChanged(id);

        return true;
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
}
