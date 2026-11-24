using Deckside.Core.Directory;
using Deckside.Core.Naming;
using Deckside.Core.Profiles;

namespace Deckside.Core.Plans;

/// <summary>What the operator typed in the "Users: create" form.</summary>
public sealed record CreateUserRequest
{
    public required string GivenNames { get; init; }

    public required string Surname1 { get; init; }

    public string Surname2 { get; init; } = "";

    public string EmployeeId { get; init; } = "";

    public string Department { get; init; } = "";

    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    public string Ticket { get; init; } = "";

    /// <summary>Other attributes from the form; only those in <see cref="CreateUserPlanner.AllowedExtraAttributes"/>.</summary>
    public IReadOnlyDictionary<string, string> Extra { get; init; } = new Dictionary<string, string>();

    /// <summary>User profiles applied right after creating.</summary>
    public IReadOnlyList<string> Profiles { get; init; } = [];

    /// <summary>OU where it is created; null = the users OU of the settings.</summary>
    public string? TargetOu { get; init; }
}

/// <summary>Exactly what "Users: create" will write. Built without changing anything.</summary>
public sealed record CreateUserPlan
{
    public required CreateUserRequest Request { get; init; }

    public string TargetOu { get; init; } = "";

    public string Dn { get; init; } = "";

    public string Cn { get; init; } = "";

    public string SamAccountName { get; init; } = "";

    public string UserPrincipalName { get; init; } = "";

    public string Mail { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public SequentialProposal? Sequence { get; init; }

    public MailProposal? MailProposal { get; init; }

    /// <summary>Attributes of the new object, in the order they are written (created disabled: 514).</summary>
    public IReadOnlyList<AttributeChange> Attributes { get; init; } = [];

    public IReadOnlyList<string> GroupsToAdd { get; init; } = [];

    public IReadOnlyList<PlanIssue> Issues { get; init; } = [];

    public bool CanExecute => Issues.All(i => i.Severity != IssueSeverity.Error);
}

public static class CreateUserPlanner
{
    /// <summary>
    /// Attributes the form may fill besides the built ones. An allow-list on purpose: attributes such as
    /// <c>userAccountControl</c>, <c>servicePrincipalName</c>, <c>scriptPath</c>, <c>altSecurityIdentities</c>
    /// or <c>msDS-KeyCredentialLink</c> change security and are never written from a form.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedExtraAttributes =
    [
        "company", "employeeNumber", "employeeType", "facsimileTelephoneNumber", "ipPhone", "l", "manager", "mobile",
        "physicalDeliveryOfficeName", "postalCode", "st", "streetAddress", "telephoneNumber",
    ];

    public static CreateUserPlan Plan(CreateUserRequest request, IEnumerable<ExistingName> existingSequence, PlanningContext ctx)
    {
        var t = ctx.T;
        var s = ctx.Settings;
        var issues = new IssueList();
        var name = new PersonName(request.GivenNames, request.Surname1, request.Surname2);

        // --- where ---
        var targetOu = request.TargetOu ?? s.UsersOU;
        if (!ctx.Scope.IsConfigured(ScopeArea.Users))
        {
            issues.Error("OU_NOT_CONFIGURED", t.T("The users OU is not configured (or it is outside the master OU)."));
        }
        else if (!ctx.Scope.IsValidTarget(ScopeArea.Users, targetOu))
        {
            issues.Error("OUTSIDE_SCOPE", t.T("The target OU is outside the users OU: {0}", targetOu));
        }

        if (!AccountNames.IsValidDnsDomain(ctx.MailDomain) || !AccountNames.IsValidDnsDomain(ctx.UpnSuffix))
        {
            issues.Error("DOMAIN_NOT_CONFIGURED", t.T("The domain (or the e-mail domain / UPN suffix) is not configured."));
        }

        // --- the person ---
        if (name.GivenNames.Length == 0 || name.Surname1.Length == 0)
        {
            issues.Error("NAME_REQUIRED", t.T("Given name and first surname are required."));
        }

        foreach (var (field, value) in new[] { ("givenName", name.GivenNames), ("sn", name.Surname1), ("sn2", name.Surname2) })
        {
            if (value.Length > 0 && !NameNormalizer.IsValidPersonName(value))
            {
                issues.Error("NAME_INVALID_CHARACTERS", t.T("The name has characters that are not letters: {0}", value));
            }

            if (value.Length > AccountNames.MaxCnLength)
            {
                issues.Error("NAME_TOO_LONG", t.T("{0} is longer than {1} characters.", field, AccountNames.MaxCnLength));
            }
        }

        if (s.RequireTicket && string.IsNullOrWhiteSpace(request.Ticket))
        {
            issues.Error("TICKET_REQUIRED", t.T("The ticket is required."));
        }

        foreach (var key in request.Extra.Keys)
        {
            if (!AllowedExtraAttributes.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                issues.Error("ATTRIBUTE_NOT_ALLOWED", t.T("Deckside does not write the attribute {0}.", key));
            }
        }

        if (request.Extra.TryGetValue("manager", out var manager) && manager.Length > 0 && !DistinguishedName.IsValid(manager))
        {
            issues.Error("MANAGER_INVALID", t.T("The manager must be the DN of a user: {0}", manager));
        }

        if (issues.HasErrors)
        {
            return new CreateUserPlan { Request = request, TargetOu = targetOu, Issues = issues.Items };
        }

        // --- sAMAccountName: next sequential in the whole domain ---
        SequentialProposal? sequence = null;
        try
        {
            sequence = Sequential.Next(SequencePattern.Parse(s.Naming.SamPattern), s.Naming.SamFirstNumber, existingSequence, ctx.Checker.IsSamAccountNameTaken);
            if (sequence.SkippedTaken > 0)
            {
                issues.Warning("SEQUENCE_SKIPPED", t.T("{0} numbers were already taken and were skipped.", sequence.SkippedTaken));
            }

            if (AccountNames.UserSamProblems(sequence.Name).Count > 0)
            {
                issues.Error("SAM_INVALID", t.T("Invalid account name: {0}", sequence.Name));
            }
        }
        catch (NamingException ex) when (ex.Code == "SEQUENCE_EXHAUSTED")
        {
            issues.Error("SEQUENCE_EXHAUSTED", t.T("There are no more numbers in the pattern {0}.", s.Naming.SamPattern));
        }

        // --- e-mail ---
        MailProposal? mail = null;
        try
        {
            mail = UserNameBuilder.BuildMail(name, request.EmployeeId, s.Naming.MailPattern, s.Naming.MailFallbacks, ctx.MailDomain, ctx.Checker.IsMailTaken);
            if (mail.UsedFallback)
            {
                issues.Warning("MAIL_FALLBACK", t.T("{0} was taken; using {1}.", string.Join(", ", mail.TakenCandidates), mail.Address));
            }
        }
        catch (NamingException ex)
        {
            issues.Error(ex.Code, t.T("No e-mail address could be built: {0}", ex.Detail));
        }

        if (sequence is null || mail is null)
        {
            return new CreateUserPlan { Request = request, TargetOu = targetOu, Sequence = sequence, MailProposal = mail, Issues = issues.Items };
        }

        // --- UPN, CN, displayName, sn ---
        var sam = sequence.Name;
        var upn = UserNameBuilder.BuildUpn(s.Naming.UpnPattern, sam, mail.LocalPart, ctx.MailDomain, ctx.UpnSuffix);
        if (ctx.Checker.IsUpnTaken(upn))
        {
            issues.Error("UPN_TAKEN", t.T("The UPN is already used: {0}", upn));
        }

        var cn = UserNameBuilder.BuildDisplay(s.Naming.CnPattern, name, request.EmployeeId);
        if (ctx.Checker.IsCnTaken(cn))
        {
            var withSam = cn + " (" + sam + ")";
            issues.Warning("CN_FALLBACK", t.T("The name {0} already exists in the domain; using {1}.", cn, withSam));
            cn = withSam;
        }

        if (cn.Length > AccountNames.MaxCnLength)
        {
            issues.Error("CN_TOO_LONG", t.T("{0} is longer than {1} characters.", "CN", AccountNames.MaxCnLength));
        }

        var dn = DistinguishedName.Child("CN", cn, targetOu);
        if (ctx.Checker.IsDnTaken(dn))
        {
            issues.Error("DN_TAKEN", t.T("An object already exists with the DN {0}", dn));
        }

        var displayName = UserNameBuilder.BuildDisplay(s.Naming.DisplayNamePattern, name, request.EmployeeId);
        var sn = UserNameBuilder.BuildDisplay(s.Naming.SurnamePattern, name, request.EmployeeId);

        // --- groups from profiles ---
        var groups = ctx.CheckGroupsToAdd(ctx.GroupsOfProfiles(request.Profiles, ProfileTarget.User, issues), issues);

        // --- attributes, in write order ---
        var attributes = new List<AttributeChange>
        {
            AttributeChange.Set("objectClass", "user"),
            AttributeChange.Set("cn", cn),
            AttributeChange.Set("sAMAccountName", sam),
            AttributeChange.Set("userPrincipalName", upn),
            AttributeChange.Set("givenName", name.GivenNames),
            AttributeChange.Set("sn", sn),
            AttributeChange.Set("displayName", displayName),
            AttributeChange.Set("mail", mail.Address),
        };
        if (s.Naming.SetProxyAddresses)
        {
            attributes.Add(AttributeChange.Set("proxyAddresses", "SMTP:" + mail.Address));
        }

        void Optional(string attribute, string value)
        {
            var clean = NameNormalizer.Clean(value);
            if (clean.Length > 0)
            {
                attributes.Add(AttributeChange.Set(attribute, clean));
            }
        }

        Optional("employeeID", request.EmployeeId);
        Optional("department", request.Department);
        Optional("title", request.Title);
        Optional("description", request.Description);
        foreach (var (key, value) in request.Extra.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var canonical = AllowedExtraAttributes.First(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
            Optional(canonical, value);
        }

        attributes.Add(AttributeChange.Set("userAccountControl", AccountControlValues.NewUserDisabled.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        if (sequence.Previous is not null)
        {
            issues.Info("SEQUENCE_PREVIOUS", t.T("The last number was {0} ({1}).", sequence.Previous.Name, sequence.Previous.Dn));
        }

        return new CreateUserPlan
        {
            Request = request,
            TargetOu = DistinguishedName.Normalize(targetOu),
            Dn = dn,
            Cn = cn,
            SamAccountName = sam,
            UserPrincipalName = upn,
            Mail = mail.Address,
            DisplayName = displayName,
            Sequence = sequence,
            MailProposal = mail,
            Attributes = attributes,
            GroupsToAdd = groups,
            Issues = issues.Items,
        };
    }
}
