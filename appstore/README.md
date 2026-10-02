# Publishing Dyncamelo on the Autodesk App Store

This folder holds everything the store needs that can be prepared without an Autodesk account. What only the publisher can do is in [Your part](#your-part) below.

| File | What it is |
|---|---|
| `listing.md` | Every field of the submission form, ready to paste (title, descriptions, categories, screenshots with texts, quick start, EULA note) |
| `help/index.html` | The quick-start page that ships in the package (`HelpFile` in PackageContents.xml); self-contained, no network |
| `assets/` | Logo in 80, 120 and 256 pixels, the `.ico` the package manifest points at, and `screenshots/` |
| `publisher.json` | The publisher facts the package needs (name, support email, the constant upgrade code) |
| `../tools/build_store_package.py` | Builds the store package from the signed release files |
| `../PRIVACY.md` | The privacy policy (also inside the app: Help > Privacy Policy) |

## What the store requires, and where this repository stands

Sources are Autodesk's Navisworks publisher guidelines (aps.autodesk.com/app-store/publisher-center/navisworks), the PackageContents.xml attribute table, the Desktop App Submission Process Overview, the Publisher FAQ and the Getting Started Guide, as read on 2026-10-02. Autodesk changes these pages; read them again before you submit.

| Requirement | Status |
|---|---|
| Bundle folder with `PackageContents.xml` and `Contents/v21`, `v22`, `v23` (2024, 2025, 2026) | Done: the builder lays the signed release out this way |
| One build per Navisworks release (the .NET API works within one major version) | Done (three builds) |
| PackageContents.xml attributes a download needs: SchemaVersion, AppVersion, Author, Name, Description, Icon, HelpFile, ProductCode, UpgradeCode; CompanyDetails Name and Email | Done, except the Email (yours) |
| Works straight after install, no manual copying or registration | Done: the bundle needs nothing else |
| Bundle name unique among Autodesk bundles | `Dyncamelo.bundle` |
| Each DLL name unique within a Navisworks session | **Open**, see [DLL names](#dll-names) |
| F1 opens your own help | In the editor F1 opens Dyncamelo's shortcut sheet (not Navisworks' help); the Help menu opens the online guide. Check it in Navisworks (QA checklist) |
| Privacy policy: link on the listing **and** the text inside the app; says what is collected, shared, kept, how to withdraw consent | Done: `PRIVACY.md`, Help > Privacy Policy, link in `listing.md` |
| No data gathered without permission; telemetry disclosed | Done: none; the one network request (update check) is disclosed and can be switched off. A store install never makes it |
| Must not crash or slow Navisworks, alter Autodesk behaviour, use undocumented API calls | **Open**, see [Review risks](#review-risks) |
| Help page ("Quick Start") | Done: `help/index.html`; the form also asks for its content as fields (`listing.md`) |
| Logo 80 x 80 recommended, screenshots up to 10 (2000 x 2000, 20 MB, each with a text), long description up to 4000 characters, up to 4 categories per product | Done for logo and text; screenshots from Navisworks are yours |
| Installer | Autodesk builds the MSI from the package ("standard installer"); you may supply your own MSI or a merge module instead. Not needed |
| Code signing | Optional ("recommended"). The DLLs in the package are signed when the release workflow has a certificate |
| Licence | Autodesk's standard EULA applies; extra conditions go in the description (done). Dyncamelo's licence is PolyForm Noncommercial (free for personal and other noncommercial use, commercial use needs a licence from BIMCamel). **Ask Autodesk first**, see [The licence and the store](#the-licence-and-the-store) |
| Support | You must support every Navisworks release you tick. The form asks which ones you tested |

## The package

`python3 tools/build_store_package.py --staging staging/Dyncamelo.bundle --version v0.45.1 --out store-package` turns the staged release bundle into:

```
Dyncamelo.bundle/
  PackageContents.xml
  Contents/
    v21/   Dyncamelo.App.dll, the other DLLs, en-US/, Resources/, Samples/, distribution.txt   (Navisworks 2024)
    v22/   the same, built for 2025
    v23/   the same, built for 2026
    Resources/   Dyncamelo.ico, Help/index.html, PRIVACY.md, LICENSE, THIRD-PARTY-NOTICES.md
```

The release workflow runs it on every release and on every dry run and keeps the result as the build artifact **appstore-package** (Actions > the run > Artifacts, kept 30 days): the zip, and a `submission` folder with the listing, icons, screenshots, the privacy policy and `package-report.txt` (checksums, product code, problems found). It is **not** attached to the GitHub release.

The package differs from the GitHub bundle in four ways: the folder layout and manifest above; `distribution.txt` in each release folder, which makes Dyncamelo skip its GitHub update check (the store delivers updates, and a second copy installed from GitHub would load next to the store's); the help page, icon and policy in `Contents/Resources`; and a product code derived from the version while the upgrade code stays constant.

A package built while `supportEmail` is empty is named `...-DRAFT.zip`. `--submission` makes the build fail until the email is set.

## Your part

1. **Autodesk account and publisher profile** at apps.autodesk.com (Publisher Corner). Accept the Publisher Agreement. A free app needs no PayPal account. Check which publisher name and legal details the profile asks for; the package says "BIMCamel".
2. **Support email**: put an address you read into `appstore/publisher.json` (`supportEmail`). The store requires it in the package and the listing. It is also shown in the help page.
3. **Ask Autodesk first** (appsubmissions@autodesk.com): whether a free listing under a noncommercial licence is accepted next to the standard EULA and the Publisher Agreement's minimum terms (see [The licence and the store](#the-licence-and-the-store)), and whether the keyboard hook described under [Review risks](#review-risks) is acceptable.
4. **Test in real Navisworks**: Help > Run Self-Test and `docs/QA_CHECKLIST.md` on each release you will tick (2024, 2025, 2026; Manage and Simulate if you tick both). The form makes you commit to support for each. Only tick what you have seen working.
5. **Screenshots from Navisworks** (shot list in `listing.md`): the editor docked next to a model, and a model coloured by a graph. The build machine's pictures are in `assets/screenshots/`.
6. **Download the `appstore-package` artifact** of a release run made after you set the email, check `package-report.txt` shows no problems, and **submit**: the form asks for the app file (the zip), logo, screenshots with texts, descriptions, categories, compatible products, price (free), privacy policy link and the publisher privacy policy.
7. **Review**: Autodesk reviews within about two weeks, builds the installer and sends you the final package to check. Answer their questions by email. After approval, each update goes through the same review while the live version stays online.

## The licence and the store

Dyncamelo is licensed under PolyForm Noncommercial 1.0.0: free for personal and other noncommercial use, a commercial licence from BIMCamel for everything else. The Autodesk App Store is mostly used by people who work for companies, and the standard EULA that comes with every store app is written for that. Two things can go wrong:

* The reviewer may find a free listing that forbids commercial use misleading, or in conflict with the EULA or the Publisher Agreement's minimum terms (Exhibit A).
* Customers may install a "free" app from the store and then be in breach of the licence without knowing it. The listing and the help page both say plainly who needs a commercial licence, but a store page is read quickly.

Options, for you to choose between after you have Autodesk's answer: (1) keep the free listing as it is if Autodesk accepts it; (2) list a paid edition for commercial users (the store supports paid apps through PayPal and lets you set the price; a free app and a paid app are separate listings); (3) put a store build under a licence that allows commercial use, which is your call as the owner. Nothing in the package or the listing has to change for (1) or (2) beyond the price.

## DLL names

The store requires every DLL name to be unique within a Navisworks session. Dyncamelo's own DLLs are all prefixed `Dyncamelo.`. The package also contains `Newtonsoft.Json.dll`, `Nodify.dll`, `AutomaticGraphLayout.dll` and `BIMCamel.dll` (the vendored engine). Names like Newtonsoft.Json are common to many add-ins, and the same-named DLL from two add-ins in one session is how version conflicts happen. `BIMCamel.dll` will clash with any other BIMCamel product that ships a DLL of that name. Options, from least to most work: ask the reviewer whether shared third-party names are acceptable; rename the vendored engine to a Dyncamelo-specific name; ship third-party libraries under unique assembly names. The builder prints this as a warning on every build.

## Review risks

Autodesk rejects apps that "use undocumented API calls" or "alter the behaviour of Autodesk products". Dyncamelo no longer merges its ribbon tab with other BIMCamel tools (that reflection code, which touched Autodesk's ribbon assembly, was removed). What is left to mention to the reviewer:

* `Dyncamelo.App/PaneKeyGuard.cs` installs a keyboard hook (`WH_GETMESSAGE`) while the Dyncamelo pane has the focus, so that Navisworks' own shortcuts (Ctrl+Z, Delete, F1) do not take keys the editor needs. It only acts while the pane has the keyboard focus. The source is open for the reviewer to read.

## Keeping the store version current

The version in `AppVersion` is the release tag. When you release, the workflow builds a new package; submit it as an update in Publisher Corner with the change text from `CHANGELOG.md`. Keep `upgradeCode` in `publisher.json` unchanged forever: it is how Windows tells the new MSI from the old one.
