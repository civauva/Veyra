namespace Veyra.Access.Users;

/// <summary>Which people the Users screen lists, one chip each (VEY-67).</summary>
public enum PeopleShow
{
    /// <summary>Everyone.</summary>
    Everyone,

    /// <summary>People without a second factor.</summary>
    NoFactor,

    /// <summary>People locked out after too many wrong passwords, until the lock ends.</summary>
    Locked,

    /// <summary>People switched off, who cannot sign in.</summary>
    Off,
}
