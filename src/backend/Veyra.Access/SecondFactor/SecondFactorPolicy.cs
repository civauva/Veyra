namespace Veyra.Access.SecondFactor;

/// <summary>Who must sign in with a second factor, chosen by an Admin (ADR 0011).</summary>
public enum SecondFactorPolicy
{
    /// <summary>Nobody must; everyone may.</summary>
    Nobody = 0,

    /// <summary>Admins must.</summary>
    Admins = 1,

    /// <summary>Everyone with a local password must.</summary>
    Everyone = 2,
}
