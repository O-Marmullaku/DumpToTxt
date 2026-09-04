# To-do for human (blocked on tools or external verification)

## Installer follow-ups

The repository build found per-user Inno Setup 6.7.3 and compiled the redesigned full installer successfully. The setup executable was deliberately not launched or installed on this development machine.

1. On a disposable Windows VM, install once and confirm exactly 1 visible **Create dump with DumpToTxt** action for files, folders, and folder backgrounds.
2. Run the setup again and verify the in-wizard **Already installed** page: **Update or reinstall** continues setup, **Uninstall** starts the existing uninstaller, and **Cancel** changes nothing.
3. During uninstall, test both answers to the settings prompt: keep `%APPDATA%\DumpToTxt`, then reinstall/uninstall again and delete it. Confirm `%PROGRAMDATA%\DumpToTxt` follows the same delete choice.
4. Test the compact installer’s .NET 8 Desktop Runtime prompt on a clean VM without that runtime.
5. Compile the lite flavor and confirm its Classic-only behavior remains truthful.

Do not install the candidate setup on the development machine as part of a build-only check.

## Windows Explorer acceptance

The app-side flow is implemented and tested without changing the machine’s existing Explorer registration. After compiling the candidate installer, verify on the disposable VM:

- The single context action opens the review workspace for a file, folder, and folder background.
- The largest contributors update while scanning; folders expand to files; mouse and Space toggle inclusion.
- **Skip this screen next time** reuses the last accepted format/layout/destination/content mode.
- Holding Shift while invoking the action forces review back open.
- `.txt`, `.md`, `.json`, `.xml`, and `.docx` open with their associated applications.
- The `.docx` index links to each file section and each section links back to the index.

## Release / distribution

- Add a license before public distribution.
- Build and sign the intended release artifacts.
- Publish the 3 flavor installers through a GitHub Release; binaries remain gitignored in this repository.
