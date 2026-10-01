# What's new in 0.45.1

A fix release. Nothing is added to the node library.

* **Picking from the node library no longer closes Navisworks.** Selecting an item in the library panel (the first step of adding a node from it) built four selection colours that WPF refuses to build, and the exception ended the Navisworks process. Opening a sample or a saved graph was never affected, because those add nodes without selecting anything in the library. The library's selection colours are now the theme's own brushes and still follow the palette.
* **A WPF failure that follows a click or a key press is reported, not fatal.** Any mouse or key input in the editor or the Script Player now counts as recent activity for the crash guard, so a failure to build a visual is written to `%APPDATA%\Dyncamelo\errors.log` (with the XAML file and line) and shown in the status bar instead of ending Navisworks.
* **Tests:** every resource of the editor and the Player is built up front, the library is selected in a real window, and the "editor hosted the way Navisworks hosts it" check runs in a process of its own.
