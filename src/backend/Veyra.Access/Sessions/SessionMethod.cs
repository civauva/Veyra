namespace Veyra.Access.Sessions;

/// <summary>How a session was signed in, as stored in <c>access.session.method</c>.</summary>
public enum SessionMethod
{
    /// <summary>A password alone.</summary>
    Password = 1,

    /// <summary>A password and a code from the authenticator app.</summary>
    Code = 2,

    /// <summary>A password and a recovery code.</summary>
    RecoveryCode = 3,

    /// <summary>An OpenID Connect identity provider.</summary>
    Provider = 4,
}
