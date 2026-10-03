# Requirements

## To run Dyncamelo

| You need | Details |
|---|---|
| **Windows** | 64-bit Windows 10 or 11. Navisworks itself only runs on Windows. |
| **Autodesk Navisworks** | **Manage** or **Simulate**, release **2024, 2025 or 2026**. The install package lists exactly these six products and no others. |
| **Rights to install** | None beyond your own user account. The installer works per user. A copy for all users needs write access to `C:\ProgramData\Autodesk\ApplicationPlugins\`. |
| **A model** | Dyncamelo works against the active Navisworks document. Open a model first (`.nwd`, `.nwf`, or an appended `.rvt`, `.ifc`, `.dwg`…). A few nodes, such as the maths and text nodes, run without one. |
| **A licence that fits your use** | The copy from GitHub or bimcamel.com is free for personal use. For work at a company or in a paid project, the professional copy is coming soon to the Autodesk App Store ([Licence](licence.md#which-copy-do-i-need)). |
| **Internet** | Not needed. The only network request Dyncamelo makes by itself is the once-a-day look for a newer version, and you can switch that off ([Privacy and safety](privacy-and-safety.md)). |

Nodes that use **Clash Detective** need Navisworks **Manage**, because Simulate does not include Clash Detective.

No other Autodesk product, runtime or licence is needed. The IFC export engine ships inside Dyncamelo, so the IFC nodes need no extra plug-in ([IFC, BCF, Excel and CSV](exchange-formats.md)).

## What has been tested

Navisworks cannot be run in an automated test, so the nodes that talk to Navisworks are compiled, their logic is unit-tested, and their behaviour inside Navisworks is checked by hand.

| Item | Status |
|---|---|
| Navisworks **Manage 2024** | Used in the field. |
| Navisworks **2025** and **2026**, and **Simulate** | Built and installed the same way as 2024, but **not yet seen running**. Please report what you find. |
| The editor, the engine, the general node library | Covered by automated tests on every change, including rendering the editor and the Script Player on a Windows build machine. |
| The nodes added in 0.45 | The least tried of the Navisworks nodes. |

The [known issues](troubleshooting.md#known-issues) list what is still open. The editor has a built-in check, **Help ▸ Run Self-Test**, which runs a set of read-only Navisworks nodes against your open model and shows pass or fail for each. Run it once after installing, especially on 2025 or 2026.

## To build it yourself

Only needed if you build Dyncamelo from source ([Installation](installation.md#build-it-from-source)).

* Windows 10 or 11 with **Visual Studio 2022** (the ".NET desktop development" workload) or the **.NET 8 SDK**.
* A Navisworks installation is **not** needed to build. The Navisworks API is referenced at compile time through NuGet packages and is never redistributed with Dyncamelo; at run time your own Navisworks provides it.
* On Linux or macOS the engine, the general node library, the Navisworks node library and the tests build and run; the WPF editor does not.
