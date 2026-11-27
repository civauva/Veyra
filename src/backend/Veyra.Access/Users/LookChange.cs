namespace Veyra.Access.Users;

/// <summary>What became of a person's request to pick a look of the app (ADR 0021).</summary>
internal enum LookChange
{
    /// <summary>The look, or its clearing, is kept on the account.</summary>
    Kept,

    /// <summary>The value is not one of the <see cref="Looks"/>.</summary>
    UnknownLook,

    /// <summary>An Admin has set one look for everyone, so nobody picks their own.</summary>
    NotAllowed,

    /// <summary>The account no longer exists.</summary>
    NoAccount,
}
