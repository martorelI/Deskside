using System.DirectoryServices.Protocols;
using System.Runtime.Versioning;
using System.Text;
using Deckside.Core.Directory;
using Deckside.Core.Plans;
using Deckside.Core.Settings;

namespace Deckside.Directory;

/// <summary>
/// <see cref="IDirectory"/> over LDAP (<c>System.DirectoryServices.Protocols</c>). Binds with Negotiate
/// (Kerberos, or NTLM when the server is given by IP) and requires encryption: LDAPS, or signing + sealing
/// on 389. A connection that ends up unencrypted is closed and refused.
/// Always used through <see cref="GuardedDirectory"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LdapDirectory : IDirectory
{
    private const int PageSize = 1000;
    private readonly LdapConnection connection;

    private LdapDirectory(LdapConnection connection, ConnectionInfo info)
    {
        this.connection = connection;
        Info = info;
    }

    public ConnectionInfo Info { get; }

    public static LdapDirectory Connect(ConnectionOptions options)
    {
        if (!ServerAddress.IsValid(options.Server))
        {
            throw new DirectoryException("INVALID_SERVER", options.Server);
        }

        if (!options.UseCurrentWindowsSession && (options.UserName.Trim().Length == 0 || options.Password.Length == 0))
        {
            throw new DirectoryException("CREDENTIALS_REQUIRED", options.Server);
        }

        var port = ServerAddress.EffectivePort(options.Port, options.UseLdaps);
        var identifier = new LdapDirectoryIdentifier(options.Server, port, fullyQualifiedDnsHostName: false, connectionless: false);
        var connection = new LdapConnection(identifier) { AuthType = AuthType.Negotiate, Timeout = options.Timeout };
        try
        {
            var credential = options.ToCredential();
            if (credential is not null)
            {
                connection.Credential = credential;
            }

            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            if (options.UseLdaps)
            {
                // The server certificate is validated by Windows; it is never skipped. With an IP address the
                // certificate must include that IP, otherwise use the host name or sealing on 389.
                connection.SessionOptions.SecureSocketLayer = true;
            }
            else
            {
                connection.SessionOptions.Signing = true;
                connection.SessionOptions.Sealing = true;
            }

            connection.Bind();

            var encrypted = options.UseLdaps ? connection.SessionOptions.SecureSocketLayer : connection.SessionOptions.Sealing;
            if (!encrypted)
            {
                throw new DirectoryException("NOT_ENCRYPTED", options.Server);
            }

            var root = ReadRootDse(connection);
            var info = new ConnectionInfo
            {
                Server = options.Server,
                Port = port,
                DcHostName = root.Get("dnsHostName") ?? "",
                DefaultNamingContext = root.Get("defaultNamingContext") ?? "",
                Encryption = options.UseLdaps ? "LDAPS" : "SIGN_SEAL",
                BoundAs = WhoAmI(connection),
                UsesCurrentWindowsSession = options.UseCurrentWindowsSession,
            };
            return new LdapDirectory(connection, info);
        }
        catch (LdapException ex)
        {
            connection.Dispose();
            throw new DirectoryException(ex.ErrorCode == 49 ? "INVALID_CREDENTIALS" : "CONNECTION_FAILED", ex.Message, ex);
        }
        catch (DirectoryOperationException ex)
        {
            connection.Dispose();
            throw new DirectoryException("CONNECTION_FAILED", ex.Message, ex);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public DirectoryObject? Get(string dn, IReadOnlyList<string> attributes)
    {
        try
        {
            var response = (SearchResponse)connection.SendRequest(new SearchRequest(dn, "(objectClass=*)", SearchScope.Base, [.. attributes]));
            return response.Entries.Count == 0 ? null : Convert(response.Entries[0]);
        }
        catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.NoSuchObject)
        {
            return null;
        }
    }

    public IReadOnlyList<DirectoryObject> Search(string baseDn, string filter, SearchDepth depth, IReadOnlyList<string> attributes, CancellationToken cancellation = default)
    {
        var scope = depth switch
        {
            SearchDepth.Base => SearchScope.Base,
            SearchDepth.OneLevel => SearchScope.OneLevel,
            _ => SearchScope.Subtree,
        };
        var request = new SearchRequest(baseDn, filter, scope, [.. attributes]);
        var page = new PageResultRequestControl(PageSize);
        request.Controls.Add(page);
        var result = new List<DirectoryObject>();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var response = (SearchResponse)Send(request);
            foreach (SearchResultEntry entry in response.Entries)
            {
                result.Add(Convert(entry));
            }

            var cookie = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault()?.Cookie;
            if (cookie is null || cookie.Length == 0)
            {
                return result;
            }

            page.Cookie = cookie;
        }
    }

    public void Add(string dn, IReadOnlyList<AttributeChange> attributes)
    {
        var request = new AddRequest(dn);
        foreach (var a in attributes)
        {
            request.Attributes.Add(new DirectoryAttribute(a.Name, [.. a.After.Cast<object>()]));
        }

        Send(request);
    }

    public void Modify(string dn, IReadOnlyList<AttributeModification> changes)
    {
        var request = new ModifyRequest(dn);
        foreach (var c in changes)
        {
            var modification = new DirectoryAttributeModification
            {
                Name = c.Name,
                Operation = c.Kind switch
                {
                    ModificationKind.Add => DirectoryAttributeOperation.Add,
                    ModificationKind.Delete => DirectoryAttributeOperation.Delete,
                    _ => DirectoryAttributeOperation.Replace,
                },
            };
            foreach (var v in c.Values)
            {
                modification.Add(v);
            }

            request.Modifications.Add(modification);
        }

        Send(request);
    }

    public void Move(string dn, string newParentDn)
    {
        var rdn = DistinguishedName.FirstRdn(dn).ToString();
        Send(new ModifyDNRequest(dn, newParentDn, rdn) { DeleteOldRdn = true });
    }

    public void AddMember(string groupDn, string memberDn) =>
        Send(new ModifyRequest(groupDn, DirectoryAttributeOperation.Add, "member", memberDn));

    public void RemoveMember(string groupDn, string memberDn) =>
        Send(new ModifyRequest(groupDn, DirectoryAttributeOperation.Delete, "member", memberDn));

    public void SetPassword(string dn, string password)
    {
        if (Info.Encryption.Length == 0)
        {
            throw new DirectoryException("NOT_ENCRYPTED", Info.Server);
        }

        var value = Encoding.Unicode.GetBytes("\"" + password + "\"");
        try
        {
            Send(new ModifyRequest(dn, DirectoryAttributeOperation.Replace, "unicodePwd", value));
        }
        finally
        {
            Array.Clear(value);
        }
    }

    public void Dispose() => connection.Dispose();

    private DirectoryResponse Send(DirectoryRequest request)
    {
        try
        {
            return connection.SendRequest(request);
        }
        catch (DirectoryOperationException ex)
        {
            // The message of the server never contains the password (unicodePwd is not echoed back).
            throw new DirectoryException("LDAP_" + (ex.Response?.ResultCode.ToString() ?? "ERROR").ToUpperInvariant(), ex.Message, ex);
        }
        catch (LdapException ex)
        {
            throw new DirectoryException("CONNECTION_FAILED", ex.Message, ex);
        }
    }

    private static DirectoryObject ReadRootDse(LdapConnection connection)
    {
        var response = (SearchResponse)connection.SendRequest(new SearchRequest("", "(objectClass=*)", SearchScope.Base,
            "defaultNamingContext", "dnsHostName", "serverName", "domainControllerFunctionality"));
        var entry = response.Entries[0];
        var attributes = new List<KeyValuePair<string, IReadOnlyList<string>>>();
        foreach (string name in entry.Attributes.AttributeNames)
        {
            attributes.Add(new(name, [.. (string[])entry.Attributes[name].GetValues(typeof(string))]));
        }

        // rootDSE has no DN; give it a placeholder so DirectoryObject accepts it.
        return new DirectoryObject("CN=RootDSE", attributes);
    }

    private static string WhoAmI(LdapConnection connection)
    {
        try
        {
            var response = (ExtendedResponse)connection.SendRequest(new ExtendedRequest("1.3.6.1.4.1.4203.1.11.3"));
            var text = response.ResponseValue is null ? "" : Encoding.UTF8.GetString(response.ResponseValue);
            return text.StartsWith("u:", StringComparison.Ordinal) ? text[2..] : text;
        }
        catch (DirectoryOperationException)
        {
            return "";
        }
    }

    private static DirectoryObject Convert(SearchResultEntry entry)
    {
        var attributes = new List<KeyValuePair<string, IReadOnlyList<string>>>();
        foreach (string name in entry.Attributes.AttributeNames)
        {
            var attribute = entry.Attributes[name];
            IReadOnlyList<string> values = BinaryValues.IsBinary(name)
                ? [.. ((byte[][])attribute.GetValues(typeof(byte[]))).Select(b => BinaryValues.ToText(name, b))]
                : [.. (string[])attribute.GetValues(typeof(string))];
            attributes.Add(new(name, values));
        }

        return new DirectoryObject(entry.DistinguishedName, attributes);
    }
}
