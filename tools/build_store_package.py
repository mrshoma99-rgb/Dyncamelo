#!/usr/bin/env python3
"""Build the Autodesk App Store package for CamelGraph (previously Dyncamelo) from the staged release bundle.

The GitHub release ships ``Dyncamelo.bundle`` with one folder per Navisworks year (``2024``, ``2025``, ``2026``). The App Store wants
the layout Autodesk documents for Navisworks apps -- ``Dyncamelo.bundle/PackageContents.xml`` and ``Contents/v21``, ``v22``, ``v23`` (the
Navisworks API major version, 2024 = 21) -- and a PackageContents.xml with the attributes a downloadable app needs (Author, Name,
Description, Icon, HelpFile, ProductCode, UpgradeCode, and the company's name and email). This script takes the staged bundle that
the release workflow has already built and signed, re-lays it out, writes that manifest, checks the result and zips it.

    python3 tools/build_store_package.py --staging staging/Dyncamelo.bundle --version v0.45.1 --out store-package
    python3 tools/build_store_package.py ... --submission     # also fails while the support email is empty
    python3 tools/build_store_package.py --self-test

Outputs, in --out:
    CamelGraph-AppStore-<version>.zip     the package (a single top-level Dyncamelo.bundle folder)
    submission/                           listing.md, README.md, icons, screenshots, privacy policy, package-report.txt

Exit status: 0 = built (warnings may be printed), 1 = could not build, 2 = built but not ready to submit (--submission only).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import sys
import tempfile
import uuid
import zipfile
from pathlib import Path
from typing import Dict, List, Optional, Tuple
from xml.dom import minidom
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parent.parent
STORE = REPO / "appstore"

# Navisworks release -> (package folder under Contents, RuntimeRequirements series). 2024 is API 21, 2025 is 22, 2026 is 23.
YEARS: Dict[str, Tuple[str, str]] = {"2024": ("v21", "Nw21"), "2025": ("v22", "Nw22"), "2026": ("v23", "Nw23")}
PLATFORMS = (("NAVMAN", "Manage"), ("NAVSIM", "Simulate"))

PLUGIN_DLL = "Dyncamelo.App.dll"
MARKER_FILE = "distribution.txt"      # next to the DLLs; Dyncamelo.Core.Editing.DistributionChannel reads it
MARKER_TEXT = "autodesk-app-store"
RESOURCES = "Contents/Resources"
ICON_PATH = RESOURCES + "/Dyncamelo.ico"
HELP_PATH = RESOURCES + "/Help/index.html"

GUID_RE = re.compile(r"^\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}$")
EMAIL_RE = re.compile(r"^[^@\s]+@[^@\s]+\.[^@\s.]+$")
VERSION_RE = re.compile(r"^v?(\d+)\.(\d+)\.(\d+)$")

# The store requires every DLL name to be unique within a Navisworks session. These are third-party names other add-ins are likely to
# ship too (or Navisworks itself does); the build prints them so the publisher can weigh the risk. Dyncamelo's own are all prefixed.
GENERIC_DLL_NAMES = ("Newtonsoft.Json.dll", "Nodify.dll", "AutomaticGraphLayout.dll", "BIMCamel.dll")

EMAIL_EMPTY = "CompanyDetails@Email is empty: the store requires it for a download (set supportEmail in appstore/publisher.json)"

REQUIRED_PACKAGE_ATTRIBUTES = (
    "SchemaVersion", "AppVersion", "Author", "Name", "Description", "Icon", "HelpFile", "ProductCode", "UpgradeCode",
)


class BuildError(Exception):
    """The package cannot be built (bad input), as opposed to a package that builds but is not ready to submit."""


def load_publisher(path: Path) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as ex:
        raise BuildError(f"cannot read {path}: {ex}")
    for key in ("appName", "bundleFolder", "author", "companyName", "upgradeCode", "description"):
        if not str(data.get(key, "")).strip():
            raise BuildError(f"{path}: '{key}' is empty")
    if not GUID_RE.match(str(data["upgradeCode"]).upper()):
        raise BuildError(f"{path}: upgradeCode must look like {{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}}")
    data["upgradeCode"] = str(data["upgradeCode"]).upper()
    return data


def app_version(tag: str) -> str:
    m = VERSION_RE.match(tag.strip())
    if not m:
        raise BuildError(f"'{tag}' is not a version (expected v<major>.<minor>.<patch>)")
    return ".".join(m.groups())


def product_code(upgrade_code: str, version: str) -> str:
    """Stable per version (an MSI wants a new product code for each version, the same upgrade code for all of them)."""
    namespace = uuid.UUID(upgrade_code.strip("{}"))
    return "{" + str(uuid.uuid5(namespace, "Dyncamelo " + version)).upper() + "}"


def build_manifest(pub: dict, version: str) -> str:
    """PackageContents.xml as text. Built with the XML DOM: never edit this file as a string (see tools/ history)."""
    root = ET.Element("ApplicationPackage")
    for name, value in (
        ("SchemaVersion", "3.0"),
        ("AppVersion", version),
        ("Author", pub["author"]),
        ("ProductCode", product_code(pub["upgradeCode"], version)),
        ("UpgradeCode", pub["upgradeCode"]),
        ("Name", pub["appName"]),
        ("Description", pub["description"]),
        ("Icon", "./" + ICON_PATH),
        ("HelpFile", "./" + HELP_PATH),
    ):
        root.set(name, value)

    company = ET.SubElement(root, "CompanyDetails")
    company.set("Name", pub["companyName"])
    if pub.get("companyUrl"):
        company.set("Url", pub["companyUrl"])
    company.set("Email", pub.get("supportEmail", "").strip())

    root.append(ET.Comment(
        " One DLL per Navisworks release: a plug-in must be compiled against the API of the release it runs in "
        "(2024 = API 21 = v21, 2025 = v22, 2026 = v23). Each component loads the folder of its own release. "))
    for year, (folder, series) in YEARS.items():
        for platform, label in PLATFORMS:
            components = ET.SubElement(root, "Components")
            components.set("Description", f"{year} {label}")
            requirements = ET.SubElement(components, "RuntimeRequirements")
            requirements.set("OS", "Win64")
            requirements.set("Platform", platform)
            requirements.set("SeriesMin", series)
            requirements.set("SeriesMax", series)
            entry = ET.SubElement(components, "ComponentEntry")
            entry.set("AppName", pub["appName"])
            entry.set("AppType", "ManagedPlugin")
            entry.set("ModuleName", f"./Contents/{folder}/{PLUGIN_DLL}")

    rough = ET.tostring(root, encoding="utf-8")
    pretty = minidom.parseString(rough).toprettyxml(indent="  ", encoding="utf-8").decode("utf-8")
    # minidom leaves blank lines inside the tree on some versions; keep the file tidy.
    return "\n".join(line for line in pretty.splitlines() if line.strip()) + "\n"


def render_help(template: str, version: str, support_email: str) -> str:
    line = ""
    if support_email:
        line = f'<p>Email: <a href="mailto:{support_email}">{support_email}</a></p>'
    return template.replace("{{VERSION}}", version).replace("{{SUPPORT_EMAIL_LINE}}", line)


def stage_package(staging: Path, pkg: Path, pub: dict, version: str, repo: Path) -> None:
    """Lay the staged per-year bundle out as the store package under pkg (the .bundle folder)."""
    if pkg.exists():
        shutil.rmtree(pkg)
    for year, (folder, _series) in YEARS.items():
        source = staging / year
        if not (source / PLUGIN_DLL).is_file():
            raise BuildError(f"{source / PLUGIN_DLL} is missing: stage the release bundle first (release workflow, 'Build & stage')")
        shutil.copytree(source, pkg / "Contents" / folder)
        (pkg / "Contents" / folder / MARKER_FILE).write_text(MARKER_TEXT + "\n", encoding="utf-8")

    resources = pkg / RESOURCES
    (resources / "Help").mkdir(parents=True)
    store = repo / "appstore"
    shutil.copyfile(store / "assets" / "Dyncamelo.ico", pkg / ICON_PATH)
    template = (store / "help" / "index.html").read_text(encoding="utf-8")
    (pkg / HELP_PATH).write_text(render_help(template, version, pub.get("supportEmail", "").strip()), encoding="utf-8")
    for name in ("PRIVACY.md", "LICENSE", "THIRD-PARTY-NOTICES.md"):
        source = repo / name
        if source.is_file():
            shutil.copyfile(source, resources / name)

    (pkg / "PackageContents.xml").write_text(build_manifest(pub, version), encoding="utf-8")


def validate(pkg: Path, pub: dict, submission: bool) -> Tuple[List[str], List[str]]:
    """Returns (errors, warnings). Errors make the package unusable or, with submission=True, not ready to submit."""
    errors: List[str] = []
    warnings: List[str] = []
    manifest = pkg / "PackageContents.xml"
    try:
        root = ET.parse(manifest).getroot()
    except (OSError, ET.ParseError) as ex:
        return [f"PackageContents.xml does not parse: {ex}"], warnings
    if root.tag != "ApplicationPackage":
        errors.append("PackageContents.xml: the root element is not ApplicationPackage")

    for attribute in REQUIRED_PACKAGE_ATTRIBUTES:
        if not root.get(attribute, "").strip():
            errors.append(f"PackageContents.xml: ApplicationPackage@{attribute} is missing (the store requires it for a download)")
    for attribute in ("ProductCode", "UpgradeCode"):
        if root.get(attribute) and not GUID_RE.match(root.get(attribute, "")):
            errors.append(f"PackageContents.xml: {attribute} is not a braced upper-case GUID")
    if root.get("ProductCode") and root.get("ProductCode") == root.get("UpgradeCode"):
        errors.append("PackageContents.xml: ProductCode and UpgradeCode must differ")

    company = root.find("CompanyDetails")
    if company is None or not (company.get("Name") or "").strip():
        errors.append("PackageContents.xml: CompanyDetails@Name is missing (required for a download)")
    email = (company.get("Email") if company is not None else "") or ""
    if not email.strip():
        (errors if submission else warnings).append(EMAIL_EMPTY)
    elif not EMAIL_RE.match(email.strip()):
        errors.append(f"CompanyDetails@Email '{email}' is not an email address")

    for attribute in ("Icon", "HelpFile"):
        target = root.get(attribute, "")
        if target and not (pkg / target.lstrip("./")).is_file():
            errors.append(f"PackageContents.xml: {attribute} points at {target}, which is not in the package")

    modules = [e.get("ModuleName", "") for e in root.iter("ComponentEntry")]
    if len(modules) != len(YEARS) * len(PLATFORMS):
        errors.append(f"PackageContents.xml: expected {len(YEARS) * len(PLATFORMS)} components (3 releases x Manage/Simulate), found {len(modules)}")
    for module in modules:
        if not module.startswith("./Contents/v") or not (pkg / module[2:]).is_file():
            errors.append(f"PackageContents.xml: ModuleName {module} is not a file in the package")
    for entry in root.iter("RuntimeRequirements"):
        if not entry.get("SeriesMin") or entry.get("SeriesMin") != entry.get("SeriesMax"):
            errors.append("PackageContents.xml: a component accepts more than one Navisworks series; each build is for exactly one")

    shared: List[str] = []
    for folder, _series in YEARS.values():
        directory = pkg / "Contents" / folder
        marker = directory / MARKER_FILE
        if not marker.is_file() or marker.read_text(encoding="utf-8").strip() != MARKER_TEXT:
            errors.append(f"Contents/{folder}/{MARKER_FILE} is missing or wrong (a store install must not offer the GitHub update)")
        shared += [name for name in GENERIC_DLL_NAMES if (directory / name).is_file() and name not in shared]
    if shared:
        warnings.append(
            "Third-party DLL names in every release folder; the store requires DLL names to be unique within a Navisworks session: "
            + ", ".join(shared))

    help_file = pkg / HELP_PATH
    if help_file.is_file():
        text = help_file.read_text(encoding="utf-8")
        if "{{" in text:
            errors.append("Help/index.html still has an unreplaced {{placeholder}}")
        if re.search(r"<script|<link\b|<iframe|\bsrc\s*=\s*[\"']?https?:", text, re.I):
            errors.append("Help/index.html loads something from outside the page; it must be self-contained")
    return errors, warnings


def write_zip(pkg: Path, zip_path: Path, bundle_folder: str) -> str:
    """Deterministic zip: same input, same bytes (sorted names, fixed timestamps)."""
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(p for p in pkg.rglob("*") if p.is_file()):
            relative = bundle_folder + "/" + path.relative_to(pkg).as_posix()
            info = zipfile.ZipInfo(relative, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, path.read_bytes())
    return hashlib.sha256(zip_path.read_bytes()).hexdigest()


def write_submission_folder(out: Path, repo: Path, report: str) -> None:
    folder = out / "submission"
    if folder.exists():
        shutil.rmtree(folder)
    folder.mkdir(parents=True)
    store = repo / "appstore"
    for name in ("listing.md", "README.md"):
        if (store / name).is_file():
            shutil.copyfile(store / name, folder / name)
    assets = store / "assets"
    if assets.is_dir():
        shutil.copytree(assets, folder / "assets")
    if (repo / "PRIVACY.md").is_file():
        shutil.copyfile(repo / "PRIVACY.md", folder / "PRIVACY.md")
    (folder / "package-report.txt").write_text(report, encoding="utf-8")


def build(staging: Path, version_tag: str, out: Path, publisher: Path, submission: bool, repo: Path = REPO) -> int:
    pub = load_publisher(publisher)
    version = app_version(version_tag)
    if not staging.is_dir():
        raise BuildError(f"{staging} is not a folder")
    out.mkdir(parents=True, exist_ok=True)

    with tempfile.TemporaryDirectory(prefix="dyc-store-") as work:
        pkg = Path(work) / pub["bundleFolder"]
        stage_package(staging, pkg, pub, version, repo)
        errors, warnings = validate(pkg, pub, submission)
        not_ready = [e for e in errors if e == EMAIL_EMPTY]
        broken = [e for e in errors if e != EMAIL_EMPTY]
        lines = [f"CamelGraph Autodesk App Store package {version_tag}"]
        zip_name = ""
        if not broken:
            # Without a support email the store would refuse the download, so the file says it is a draft.
            draft = bool(not_ready) or EMAIL_EMPTY in warnings
            zip_name = f"CamelGraph-AppStore-{version_tag}" + ("-DRAFT" if draft else "") + ".zip"
            digest = write_zip(pkg, out / zip_name, pub["bundleFolder"])
            lines += [
                f"File:    {zip_name}",
                f"SHA-256: {digest}",
                f"Product code: {product_code(pub['upgradeCode'], version)}   Upgrade code: {pub['upgradeCode']}",
            ]
        lines.append("")
        lines += [f"ERROR:   {e}" for e in errors] + [f"WARNING: {w}" for w in warnings]
        if not errors and not warnings:
            lines.append("No problems found.")

    report = "\n".join(lines) + "\n"
    write_submission_folder(out, repo, report)
    print(report, end="")
    if broken:
        return 1
    return 2 if not_ready else 0


# ----------------------------------------------------------------------------------------------------------------------------------
# Self-test: builds a package from a fake staging folder and breaks it in the ways that matter.
# ----------------------------------------------------------------------------------------------------------------------------------

def _fake_staging(root: Path) -> Path:
    staging = root / "staging" / "Dyncamelo.bundle"
    for year in YEARS:
        folder = staging / year
        (folder / "en-US").mkdir(parents=True)
        for name in (PLUGIN_DLL, "Dyncamelo.Core.dll", "Dyncamelo.UI.dll", "Nodify.dll", "Newtonsoft.Json.dll"):
            (folder / name).write_bytes(b"MZ" + year.encode())
        (folder / "en-US" / "Dyncamelo.xaml").write_text("<x/>", encoding="utf-8")
    return staging


def self_test() -> int:
    failures: List[str] = []

    def check(condition: bool, what: str) -> None:
        if not condition:
            failures.append(what)

    base = json.loads((STORE / "publisher.json").read_text(encoding="utf-8"))
    with tempfile.TemporaryDirectory(prefix="dyc-store-selftest-") as tmp:
        root = Path(tmp)
        staging = _fake_staging(root)

        def run(email: str, submission: bool, name: str, drop_year: Optional[str] = None) -> Tuple[int, Path]:
            publisher = root / (name + ".json")
            data = dict(base)
            data["supportEmail"] = email
            publisher.write_text(json.dumps(data), encoding="utf-8")
            use = staging
            if drop_year:
                use = root / ("staging-" + name) / "Dyncamelo.bundle"
                shutil.copytree(staging, use)
                shutil.rmtree(use / drop_year)
            out = root / ("out-" + name)
            try:
                code = build(use, "v1.2.3", out, publisher, submission, REPO)
            except BuildError:
                code = 1
            return code, out

        # A complete package.
        import contextlib
        import io
        with contextlib.redirect_stdout(io.StringIO()):
            code, out = run("support@example.com", True, "ok")
        check(code == 0, f"a complete package should build and validate (exit {code})")
        zips = list(out.glob("CamelGraph-AppStore-*.zip"))
        check(len(zips) == 1 and "DRAFT" not in zips[0].name, "a complete package should not be named DRAFT")
        if zips:
            with zipfile.ZipFile(zips[0]) as archive:
                names = set(archive.namelist())
                for must in (
                    "Dyncamelo.bundle/PackageContents.xml",
                    "Dyncamelo.bundle/Contents/v21/Dyncamelo.App.dll",
                    "Dyncamelo.bundle/Contents/v22/Dyncamelo.App.dll",
                    "Dyncamelo.bundle/Contents/v23/Dyncamelo.App.dll",
                    "Dyncamelo.bundle/Contents/v21/en-US/Dyncamelo.xaml",
                    "Dyncamelo.bundle/Contents/v22/distribution.txt",
                    "Dyncamelo.bundle/Contents/Resources/Dyncamelo.ico",
                    "Dyncamelo.bundle/Contents/Resources/Help/index.html",
                ):
                    check(must in names, f"the zip should contain {must}")
                check(not any(n.startswith("Dyncamelo.bundle/20") for n in names), "the per-year folders must not be in the store package")
                manifest = ET.fromstring(archive.read("Dyncamelo.bundle/PackageContents.xml"))
                check(manifest.get("AppVersion") == "1.2.3", "AppVersion should be the release version")
                check(manifest.get("UpgradeCode") == base["upgradeCode"].upper(), "UpgradeCode should be the constant one")
                check(manifest.get("ProductCode") == product_code(base["upgradeCode"].upper(), "1.2.3"), "ProductCode should be derived from version")
                check(manifest.find("CompanyDetails").get("Email") == "support@example.com", "the support email should be in the manifest")
                check(len(list(manifest.iter("ComponentEntry"))) == 6, "three releases x Manage/Simulate = six components")
                help_text = archive.read("Dyncamelo.bundle/Contents/Resources/Help/index.html").decode("utf-8")
                check("1.2.3" in help_text and "{{" not in help_text, "the help page should have the version and no placeholders")
                check("support@example.com" in help_text, "the help page should show the support email")
            digest_a = hashlib.sha256(zips[0].read_bytes()).hexdigest()
            with contextlib.redirect_stdout(io.StringIO()):
                _code, out_b = run("support@example.com", True, "ok")
            digest_b = hashlib.sha256(next(out_b.glob("CamelGraph-AppStore-*.zip")).read_bytes()).hexdigest()
            check(digest_a == digest_b, "building twice should give the same bytes")
        check((out / "submission" / "package-report.txt").is_file(), "the submission folder should have the report")
        check((out / "submission" / "listing.md").is_file(), "the submission folder should have the listing")

        # No support email: a draft builds (exit 0, named DRAFT); a submission build refuses (exit 2).
        with contextlib.redirect_stdout(io.StringIO()):
            code, out = run("", False, "draft")
        check(code == 0, f"a draft without a support email should still build (exit {code})")
        check(any("DRAFT" in p.name for p in out.glob("*.zip")), "a package without a support email should be named DRAFT")
        with contextlib.redirect_stdout(io.StringIO()):
            code, _ = run("", True, "nosubmit")
        check(code == 2, f"a submission build without a support email must exit 2 (got {code})")
        with contextlib.redirect_stdout(io.StringIO()):
            code, _ = run("not-an-email", True, "badmail")
        check(code != 0, "a malformed email must fail")

        # A missing release folder cannot be built.
        with contextlib.redirect_stdout(io.StringIO()):
            code, _ = run("support@example.com", True, "noyear", drop_year="2025")
        check(code == 1, f"a staging folder without 2025 must exit 1 (got {code})")

        # The validator itself: break a good package in turn.
        publisher = root / "ok.json"
        pub = load_publisher(publisher)
        pkg = root / "pkg" / "Dyncamelo.bundle"
        stage_package(staging, pkg, pub, "1.2.3", REPO)
        errors, _ = validate(pkg, pub, True)
        check(errors == [], f"the staged package should validate, got {errors}")
        (pkg / "Contents" / "v22" / MARKER_FILE).unlink()
        errors, _ = validate(pkg, pub, True)
        check(any("v22" in e and MARKER_FILE in e for e in errors), "a missing distribution marker should be an error")
        (pkg / "Contents" / "v22" / MARKER_FILE).write_text(MARKER_TEXT + "\n", encoding="utf-8")
        (pkg / "Contents" / "v23" / PLUGIN_DLL).unlink()
        errors, _ = validate(pkg, pub, True)
        check(any("v23" in e for e in errors), "a missing plug-in DLL should be an error")
        (pkg / "Contents" / "v23" / PLUGIN_DLL).write_bytes(b"MZ")
        help_path = pkg / HELP_PATH
        help_path.write_text(help_path.read_text(encoding="utf-8") + '<script src="https://example.com/x.js"></script>', encoding="utf-8")
        errors, _ = validate(pkg, pub, True)
        check(any("outside the page" in e for e in errors), "a help page that loads a script must be an error")
        manifest_path = pkg / "PackageContents.xml"
        manifest_path.write_text(manifest_path.read_text(encoding="utf-8").replace(' Author="BIMCamel"', ""), encoding="utf-8")
        errors, _ = validate(pkg, pub, True)
        check(any("Author" in e for e in errors), "a manifest without Author must be an error")

    if failures:
        print("SELF-TEST FAILED:")
        for failure in failures:
            print("  - " + failure)
        return 1
    print("self-test passed")
    return 0


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--staging", type=Path, help="the staged release bundle (Dyncamelo.bundle with 2024/2025/2026 folders)")
    parser.add_argument("--version", help="the release tag, for example v0.45.1")
    parser.add_argument("--out", type=Path, default=Path("store-package"), help="output folder")
    parser.add_argument("--publisher", type=Path, default=STORE / "publisher.json", help="publisher facts (default appstore/publisher.json)")
    parser.add_argument("--submission", action="store_true", help="exit 2 unless the package is ready to submit (support email set)")
    parser.add_argument("--self-test", action="store_true", help="build and break a fake package to check this script")
    args = parser.parse_args(argv)

    if args.self_test:
        return self_test()
    if not args.staging or not args.version:
        parser.error("--staging and --version are required")
    try:
        return build(args.staging, args.version, args.out, args.publisher, args.submission)
    except BuildError as ex:
        print(f"error: {ex}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
