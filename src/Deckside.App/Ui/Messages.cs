using Deckside.Core.Settings;

namespace Deckside.App.Ui;

/// <summary>Human text for the English codes of Core and Directory.</summary>
internal static class Messages
{
    public static string ForCode(string code) => code switch
    {
        "INVALID_SERVER" => Text.S("Type the IP address or the name of a domain controller."),
        "CREDENTIALS_REQUIRED" => Text.S("Type the account and the password."),
        "INVALID_CREDENTIALS" => Text.S("The account or the password is not correct."),
        "NOT_ENCRYPTED" => Text.S("The connection is not encrypted, so it was refused."),
        "CONNECTION_FAILED" => Text.S("Could not connect to the server."),
        "MASTER_OU_NOT_CONFIGURED" => Text.S("The master OU is not configured: nothing can be changed until it is set in Settings."),
        "MASTER_OU_NOT_IN_DOMAIN" => Text.S("The master OU does not belong to the connected domain: nothing can be changed."),
        "MASTER_OU_NOT_FOUND" => Text.S("The master OU does not exist in the directory: nothing can be changed."),
        "OUTSIDE_MASTER_OU" => Text.S("Outside the master OU: Deckside does not change it."),
        "ATTRIBUTE_FORBIDDEN" => Text.S("Deckside never changes that attribute."),
        "UAC_FORBIDDEN" => Text.S("Those account flags are not allowed."),
        "LDAP_NOSUCHOBJECT" => Text.S("The object or the OU does not exist."),
        "LDAP_INSUFFICIENTACCESSRIGHTS" => Text.S("Your account does not have permission for this."),
        "LDAP_SIZELIMITEXCEEDED" => Text.S("Too many results; narrow the search."),
        _ => code,
    };

    public static string ForWarning(SettingsWarning w)
    {
        var what = w.Code switch
        {
            "OUTSIDE_MASTER_OU" => Text.S("is outside the master OU, it was cleared"),
            "MASTER_OU_NOT_CONFIGURED" => Text.S("needs a master OU first, it was cleared"),
            "OU_NOT_IN_DOMAIN" => Text.S("is not in the domain"),
            "DISABLED_OU_INSIDE_USERS_OU" => Text.S("is inside the users OU (disabled users would stay in scope)"),
            "INVALID_VALUE" or "INVALID_TYPE" or "INVALID_ITEM" or "OUT_OF_RANGE" => Text.S("is not valid, the default is used"),
            "UNKNOWN_KEY" => Text.S("is unknown and ignored"),
            "INVALID_JSON" => Text.S("is not valid JSON, defaults are used"),
            _ => w.Code,
        };
        var key = w.Key.Length == 0 ? "settings.json" : w.Key;
        return w.Value is null ? key + " " + what : key + " " + what + ": " + w.Value;
    }

    public static string Encryption(string code) => code switch
    {
        "LDAPS" => "LDAPS",
        "SIGN_SEAL" => Text.S("signing and sealing"),
        _ => Text.S("none"),
    };
}
