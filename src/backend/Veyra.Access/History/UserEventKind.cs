namespace Veyra.Access.History;

/// <summary>What changed on an account, as stored in <c>access.user_event.kind</c>.</summary>
public enum UserEventKind
{
    /// <summary>The account was created. Detail: the role.</summary>
    Added = 1,

    /// <summary>The role changed. Detail: the old and the new role, separated by a space.</summary>
    RoleChanged = 2,

    /// <summary>The account was switched off.</summary>
    SwitchedOff = 3,

    /// <summary>The account was switched on again.</summary>
    SwitchedOn = 4,

    /// <summary>The person changed their own password.</summary>
    PasswordChanged = 5,

    /// <summary>An Admin set a new password.</summary>
    PasswordReset = 6,

    /// <summary>The person switched their second factor on.</summary>
    SecondFactorOn = 7,

    /// <summary>The person switched their second factor off.</summary>
    SecondFactorOff = 8,

    /// <summary>An Admin, or the server's way back in, removed the second factor.</summary>
    SecondFactorRemoved = 9,

    /// <summary>A new set of recovery codes replaced the old one.</summary>
    RecoveryCodesReplaced = 10,

    /// <summary>Sessions were ended by hand. Detail: how many.</summary>
    SessionsEnded = 11,

    /// <summary>The team changed. Detail: the new team, empty when it was removed.</summary>
    TeamChanged = 12,
}
