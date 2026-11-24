# Deckside: functional specification (draft)

Desktop tool for Windows that manages users and computers of an Active Directory domain inside specific
organizational units (OUs). Every write is previewed, confirmed, logged and recorded. Fields marked
**TBD** are defined by the owner before the phase that implements them.

## Common flow (every section)

1. **Connect** (first window, at every start): the server the requests go to (IP address preferred, or
   host name; port; LDAPS or signing + sealing) and the account: typed for this session only, or the
   operator's Windows session. The app shows the server, the DC, the account the server sees, the
   encryption and the master OU. An unencrypted connection is refused.
2. **Search / plan**: read the directory without changing anything and build a visible plan.
3. **Confirm**: dialog with the exact changes and specific warnings ("Cancel" is the default button).
4. **Execute** with a live log, progress and a Stop button (a batch stops between objects, never in the
   middle of one).
5. **Verify** by reading the object back, and write a printable HTML record in one evidence folder per
   operation: `USER_<sam>_<date>`, `COMPUTER_<name>_<date>`, `OFFBOARD_<date>_<ticket>`,
   `PROFILE_<sam>_<date>`, `REPORT_<date>`.

## 1. Users: create

- Form with the fields of the new user (**TBD**: list, required ones, fixed lists). Minimum: given name,
  surname(s), employee ID, department / title, ticket.
- **Pre-Windows 2000 name (`sAMAccountName`)**: the app looks for the highest existing sequential that
  matches the configured pattern (**TBD**: prefix, digits) **in the whole domain** and proposes the next
  one. It shows which account had the last number. Max 20 characters.
- **E-mail** built from given name and surname with the configured pattern (**TBD**), normalized (no
  accents, `ñ` -> `n`, no spaces, lowercase), unique in the domain (`mail` and `proxyAddresses`); on
  collision it applies the configured fallback (**TBD**) and says so.
- **UPN**, **CN** and **displayName** from their patterns (**TBD**).
- Initial password: generated or typed (**TBD**), shown once, "must change at next logon".
- Created in the users OU from the settings. Optional: apply a profile right after creating (section 5).
- Batch mode (later phase): the same from a CSV, with a preview of every row and its proposed names.

## 2. Computers: create (pre-stage)

- Name (**TBD**: pattern, max 15 characters), description, ticket.
- Created in the computers OU from the settings, with the security groups chosen from a list (defaults
  from the settings, or a computer profile) **added at creation**.
- Optional (**TBD**): who may join it to the domain (user or group).
- Unique name in the domain (`sAMAccountName` = `NAME$`, `dNSHostName`, CN).

## 3. Offboarding: is the employee still active?

- Search field (**TBD**: employee ID, name or account). Shows the matching accounts with status: enabled /
  disabled, locked, last logon, password age, OU, groups.
- **TBD**: whether "still active" is decided only from AD or against an HR list (CSV import).
- Action "Disable and move": disables the account, moves it to the disabled OU, writes the reason / ticket /
  date in `description` (**TBD**: format), optionally removes managed groups and saves the previous state
  in the record so it can be reverted. Never deletes.
- Batch: several accounts at once, one confirmation with the list.

## 4. Health report (users OU and computers OU, separately)

For each object in the configured OUs:

| Check | Users | Computers | Source |
|---|---|---|---|
| Disabled | yes | yes | `userAccountControl` |
| Account expired / expiring in N days | yes | - | `accountExpires` |
| Password expired / expiring in N days | yes | - | `msDS-UserPasswordExpiryTimeComputed`, `msDS-User-Account-Control-Computed` |
| Password never expires / not required | yes | - | `userAccountControl` |
| Password age | yes | yes (machine password, ~30 days) | `pwdLastSet` |
| Inactive N days / never logged on | yes | yes | `lastLogonTimestamp` (replicated, up to ~14 days late) and optionally `lastLogon` from every DC |
| Locked out | yes | - | `lockoutTime`, `msDS-User-Account-Control-Computed` |
| Bad password attempts | yes | - | `badPwdCount`, `badPasswordTime` (not replicated: PDC emulator, optionally every DC) |
| Created / changed | yes | yes | `whenCreated`, `whenChanged` |
| Operating system | - | yes | `operatingSystem`, `operatingSystemVersion` |

- Thresholds in the settings (**TBD**: days of inactivity, warning days before expiry, bad attempts).
- On screen: one grid per OU with filters ("only with problems", by check), counters per check.
- **Export to Excel** (`.xlsx`): a summary sheet, a sheet for users and one for computers, with filters,
  frozen header, dates as real dates and a column per check. Also exported as CSV.
- Read only: the report never changes anything. From a row the operator can jump to Offboarding or
  Profiles.

## 5. Profiles: assign / change

- A **profile** is a named template with a list of security groups (for users or for computers), e.g.
  "Accounting", "Warehouse", "Kiosk PC". **TBD**: where they are stored and who edits them.
- "Assign / change profile" on an existing user or computer: the app computes the difference between the
  groups it has and the ones of the new profile, **only among managed groups** (groups that belong to any
  profile), shows it (add / remove / keep) and applies it after confirmation. Other groups are untouched.
- Can be applied to several objects at once.
- A profile with a protected group (security rule 3) is rejected when it is loaded.

## 6. Settings

**Master OU** (`MasterOU`): the limit of everything. The users, computers and disabled OUs, the default
groups and the groups of the profiles must be inside it (one outside is cleared with a warning). Every
write is checked against it twice: by the planners and by `GuardedDirectory` right before the request is
sent (objects, moves, both ends of a group membership). Without a master OU, or if it is not in the
connected domain or does not exist, nothing can be written; reads still work.

`Settings/settings.json` next to the exe (same model as Rackside: invalid values use the default and are
reported). Language, data folder, domain / DC, the three OUs, naming patterns, e-mail domain, report
thresholds, default computer groups, profiles file. Credentials are never saved.

## 7. About

Version, support, author, trademark notice (Microsoft, Windows, Active Directory) and the warning that the
data folders contain personal data.

## Naming patterns (phase 1: provisional defaults, to be confirmed by the owner)

Everything below is a setting (`Naming` in `settings.json`); the defaults only exist so the code and tests
can run. Patterns are literal text with `{token}` placeholders.

| Setting | Default | Tokens |
|---|---|---|
| `SamPattern` / `SamFirstNumber` | `U{seq:5}` / 1 | exactly one `{seq:N}` (N digits, zero padded); prefix may not end in a digit |
| `ComputerPattern` / `ComputerFirstNumber` | `PC{seq:5}` / 1 | same; max 15 characters |
| `MailPattern` | `{given1}.{surname1}` | `{given}` (all given names), `{given1}` (first one), `{g}`, `{surname1}`, `{surname2}`, `{s1}`, `{s2}` (initials), `{employeeId}` |
| `MailFallbacks` | `{given1}.{surname1}{s2}`, `{given1}.{surname1}{n}` | the same plus `{n}` = 2..99 |
| `UpnPattern` | `{sam}@{upnSuffix}` | `{sam}`, `{mailLocal}`, `{mailDomain}`, `{upnSuffix}` |
| `CnPattern`, `DisplayNamePattern`, `SurnamePattern` (`sn`) | `{Given} {Surnames}`, `{Given} {Surnames}`, `{Surnames}` | as typed: `{Given}`, `{Given1}`, `{Surname1}`, `{Surname2}`, `{Surnames}`, `{EmployeeId}` |

- Normalization for e-mail: accents removed (`ñ` → `n`, `ü` → `u`), lower case, and spaces, apostrophes and
  hyphens dropped (`De la Peña` → `delapena`, `María-José` → `mariajose`). Empty tokens do not leave double
  separators.
- The next sequential is the highest number of the pattern in the whole domain + 1 (holes are never
  reused); only names with exactly N digits belong to the sequence.
- CN taken anywhere in the domain → `CN (sam)`. E-mail taken in `mail` or `proxyAddresses` of any object
  (contacts included) → next fallback.
- Extra form attributes are an allow-list (`CreateUserPlanner.AllowedExtraAttributes`); anything that
  changes security (`userAccountControl`, `servicePrincipalName`, `scriptPath`...) is never written from a form.
- Offboarding `description`: `Disabled {date} | {ticket} | {reason} | {operator}` (`{previous}` = old value).
