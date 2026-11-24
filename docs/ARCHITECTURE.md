# Architecture (draft)

`Deckside.slnx` (.NET 10). Each layer only depends on the ones above it; the window holds no logic. Same
layout as Rackside, so the operations can be tested without Windows and without a real directory.

| Project | What it holds |
|---|---|
| `src/Deckside.Core` | Pure logic, no I/O. `Naming/`: sequential `sAMAccountName` (find the highest, next one, length limits), e-mail / UPN / CN / displayName builders and text normalization, computer names. `Plans/`: what a create, offboarding or profile change will write (attribute list, target OU, groups to add / remove), with validation. `Health/`: the rules of the report from raw attribute values and an `IClock`. `Profiles/`: templates, managed groups, difference between current and target groups, protected-group check. `Directory/`: the `DirectoryObject` model (DN, class, attributes) and `DistinguishedName` helpers (parent OU, "is inside"). `Security/SecretMasker`, `Settings/AppSettings` + `DataPaths`, `Localization/Translator` + `es.tsv`, `AppInfo`. |
| `src/Deckside.Directory` | `GuardedDirectory`: the last gate, every write checked against the master OU and a deny-list of security attributes / `userAccountControl` flags. `IDirectory` (search with paging, read one object, create, modify, move, add / remove group member, list DCs, read from a given DC) and its implementation over LDAP (`System.DirectoryServices.Protocols`): bind with Negotiate (Kerberos) and signing + sealing, or LDAPS. Converts AD types (FILETIME, generalized time, SID, GUID, `userAccountControl`). `FakeDirectory` lives in the tests. |
| `src/Deckside.Records` | Printable HTML records (same style as Rackside): user created, computer created, offboarding, profile change, report summary. |
| `src/Deckside.Reports` | Excel export (`ClosedXML`, MIT) and CSV of the health report. |
| `src/Deckside.Operations` | One class per section (`CreateUserOperation`, `CreateComputerOperation`, `OffboardingOperation`, `HealthReportOperation`, `ProfileOperation`): plan, confirmations through `IOperatorUi`, execution, verification, evidence folder, record and log. Cancellable between objects. |
| `src/Deckside.App` | WinForms (`net10.0-windows`): `Ui/` reused from Rackside (theme, controls, dialogs, sidebar, Stop) and one page per section in `Pages/`. |
| `tests/Deckside.Tests` | xUnit: Core rules, snapshots, operations against `FakeDirectory`, texts, settings. |

## Why LDAP (`System.DirectoryServices.Protocols`)

- One API for everything (paged search, attribute-level modify, move, constructed attributes), explicit
  control of signing / sealing / LDAPS and of which DC is asked (needed for `lastLogon` and `badPwdCount`).
- It also runs on Linux, so the Directory layer can be tested against a lab Samba AD DC if one is
  available (`Category=Integration`, skipped otherwise).
- `System.DirectoryServices.AccountManagement` is simpler but Windows-only and hides details the report
  needs; it is not used.

## Active Directory details that bite

- **Uniqueness is domain-wide**: `sAMAccountName` and UPN must be unique in the domain (UPN in the forest),
  so the next sequential is searched in the whole domain, not in the OU. Between the plan and the write
  another admin can take the number: check again right before creating and take the next one.
- `sAMAccountName`: max 20 characters (users); computers = NetBIOS name (max 15) + `$`. Invalid
  characters: `" / \ [ ] : ; | = , + * ? < >`.
- Creating a user over LDAP: add it disabled (`userAccountControl` = 514), set `unicodePwd` (quoted
  password in UTF-16LE; requires an encrypted connection), `pwdLastSet` = 0 (must change), then enable
  (512). If any step fails the account stays disabled and the record says so.
- Pre-staged computer: `objectClass` computer, `sAMAccountName` `NAME$`, `userAccountControl` 4128
  (workstation trust + password not required, as the console does); join permission is an ACL (TBD).
- Groups: membership is written on the group (`member`), so the operator needs rights on each group. The
  primary group (`primaryGroupID`, usually Domain Users) does not appear in `memberOf` and is never touched.
- Protected objects: `adminCount = 1` (AdminSDHolder). Protected groups are identified by well-known SID /
  RID (512, 518, 519, 544, 548, 549, 550, 551, 520, 526, 527, 516, 521, DnsAdmins by name lookup...).
- Times: `lastLogonTimestamp`, `pwdLastSet`, `accountExpires`, `lockoutTime`, `badPasswordTime` are
  FILETIME (100 ns since 1601, UTC); `accountExpires` 0 or 0x7FFFFFFFFFFFFFFF = never.
- `lastLogonTimestamp` is replicated but only updated when older than ~14 days
  (`ms-DS-Logon-Time-Sync-Interval`): good enough for "inactive 30+ days", not for "logged on today".
  `lastLogon`, `badPwdCount` and `badPasswordTime` are per DC (not replicated); the PDC emulator sees most
  bad passwords. Asking every DC is optional and slower.
- Password expiry: use the constructed `msDS-UserPasswordExpiryTimeComputed` (honors fine-grained
  policies) and `msDS-User-Account-Control-Computed` (locked out 0x10, password expired 0x800000); they
  must be requested explicitly.
- Searches use paging (1000 per page) and only request the attributes they need.

## Data files (next to the exe, or in `DataFolder`)

- `Settings/settings.json` (UTF-8 without BOM). Keys: `Language`, `DataFolder`, `Server` (IP or host),
  `Port`, `UseLdaps`, `Domain` (empty = from the master OU), `MasterOU`, `UsersOU`, `ComputersOU`, `DisabledOU`,
  `Naming` {`SamPattern`, `ComputerPattern`, `MailPattern`, `MailDomain`, `UpnSuffix`, `CnPattern`},
  `Report` {`InactiveDays`, `ExpiryWarningDays`, `BadPasswordThreshold`, `QueryAllDCs`},
  `DefaultComputerGroups` (DNs), `ProfilesFile`. Invalid value => default + warning in Settings.
- `Profiles/profiles.json`: `[{ "Name": "...", "Applies": "User|Computer", "Groups": ["<DN>", ...] }]`
  (TBD: or stored in AD).
- Evidence: `log.txt`, `plan.txt`, `before.json` / `after.json` (attributes, without secrets),
  `Report_*.html`; health report: `health_<date>.xlsx` and `.csv`.

## Texts and language

As in Rackside: the code is written in English, `Translator.T` returns Spanish from
`src/Deckside.Core/Localization/es.tsv`, the window looks first in `src/Deckside.App/Resources/ui.es.tsv`;
tests require a translation for every text and no unused entries. Internal states are English codes.
