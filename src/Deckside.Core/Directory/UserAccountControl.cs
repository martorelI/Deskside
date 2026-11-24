namespace Deckside.Core.Directory;

/// <summary>Flags of <c>userAccountControl</c> and of the constructed <c>msDS-User-Account-Control-Computed</c>.</summary>
[Flags]
public enum UserAccountControl
{
    None = 0,
    Script = 0x1,
    AccountDisable = 0x2,
    HomeDirRequired = 0x8,
    Lockout = 0x10,
    PasswordNotRequired = 0x20,
    PasswordCantChange = 0x40,
    EncryptedTextPasswordAllowed = 0x80,
    NormalAccount = 0x200,
    WorkstationTrustAccount = 0x1000,
    ServerTrustAccount = 0x2000,
    DontExpirePassword = 0x10000,
    SmartcardRequired = 0x40000,
    TrustedForDelegation = 0x80000,
    NotDelegated = 0x100000,
    UseDesKeyOnly = 0x200000,
    DontRequirePreauth = 0x400000,
    PasswordExpired = 0x800000,
    TrustedToAuthForDelegation = 0x1000000,
}

public static class AccountControlValues
{
    /// <summary>A new user is created disabled (514), gets its password, then is enabled (512).</summary>
    public const int NewUserDisabled = (int)(UserAccountControl.NormalAccount | UserAccountControl.AccountDisable);

    public const int UserEnabled = (int)UserAccountControl.NormalAccount;

    /// <summary>Pre-staged workstation, as the console creates it (4128 = workstation trust + password not required).</summary>
    public const int PreStagedComputer = (int)(UserAccountControl.WorkstationTrustAccount | UserAccountControl.PasswordNotRequired);
}
