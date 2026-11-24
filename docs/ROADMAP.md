# Roadmap

Each phase ends with its tests passing and one or more small commits. The owner opens each phase.

| Phase | Content | Done when |
|---|---|---|
| 0. Design | This bundle; the owner answers the open questions (CLAUDE.md) for the first sections. | Fields, naming and e-mail rules written in `docs/SPEC.md`. |
| 1. Core | Solution and projects; reused pieces from Rackside (Translator, SecretMasker, AppSettings, tests of texts, Snapshot); naming and sequential, e-mail / UPN / CN builders, normalization; plans with validation; profile difference and protected-group check; health rules with `IClock`. | Unit tests and snapshots of plans and of the health report from synthetic objects. |
| 2. Directory | `IDirectory`, LDAP implementation (bind, paging, FILETIME / SID / UAC conversion, create, modify, move, membership, per-DC reads); `FakeDirectory` in the tests. | Operations-level contract tests run the same against `FakeDirectory` (and a lab DC when available). |
| 3. Operations | Create user, create computer, offboarding, profile assign / change, health report, with `IOperatorUi`, verification, evidence folder and log. | Every security rule has a test that proves it (outside OU, protected group, collision race, no encryption, adminCount...). |
| 4. Records and Excel | HTML records; Excel and CSV export of the report. | Snapshots of the records; the xlsx opens and has the expected sheets / columns (read back in the test). |
| 5. App | WinForms pages with the Rackside UI kit; connection card (domain, DC, encryption). | Starts on Windows 11, DPI scaling, both languages; checked by the owner. |
| 6. Packaging | Version, single-file publish, README for building and signing. | `Deckside-<version>-win-x64.zip` built from a tagged commit. |

Later: user batch create from CSV, HR list for offboarding, reverting an offboarding from its record,
profiles stored in AD.
