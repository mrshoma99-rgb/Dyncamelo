# Autodesk App Store files

Files for the store listing and the store package. The submission checklist, the reasoning about the licence and the review risks are kept privately by the publisher; they are not in this repository.

| File | What it is |
|---|---|
| `listing.md` | The text of the submission form: title, descriptions, categories, screenshots with their texts, quick-start fields |
| `help/index.html` | The quick-start page that ships in the package (`HelpFile` in PackageContents.xml); self-contained, no network |
| `assets/` | Logos (80, 120 and 256 pixels), the `.ico` the manifest points at, and `screenshots/` (made by CI, see `EditorScreenshotTests`) |
| `publisher.json` | Publisher facts for the package: names, support email (empty until the publisher sets it), the constant upgrade code |
| `../tools/build_store_package.py` | Builds the package from the signed release files (`--self-test` runs in the Linux build) |
| `../PRIVACY.md` | The privacy policy, also inside the app under Help > Privacy Policy |

## The package is built only on request

The release workflow does not build the store package unless asked, and never attaches it to a GitHub release. To get one, either

* push a commit that changes `dist/DRY_RUN` with both `[dry-run]` and `[store]` in its message, or
* run the *release* workflow by hand with `dry_run` and `store_package` ticked.

The result is the build artifact **appstore-package** (kept 30 days): the zip, plus a `submission` folder with the listing, icons, screenshots, the privacy policy and `package-report.txt`. A package built while `supportEmail` is empty is named `...-DRAFT.zip`; `--submission` makes the script fail until it is set.

## What the package contains

```
CamelGraph.bundle/
  PackageContents.xml
  Contents/
    v21/   Navisworks 2024: CamelGraph.App.dll, the other DLLs, en-US/, Resources/, Samples/, distribution.txt
    v22/   Navisworks 2025
    v23/   Navisworks 2026
    Resources/   CamelGraph.ico, Help/index.html, PRIVACY.md, LICENSE, THIRD-PARTY-NOTICES.md
```

`distribution.txt` in each release folder makes CamelGraph skip its GitHub update check (the store delivers updates). The product code is derived from the version; the upgrade code in `publisher.json` must never change.
