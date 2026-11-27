# Public online installer

The installer is a small .NET Framework 4.x Windows Forms bootstrapper with an embedded, checksummed HERO application ZIP and PowerShell installation engine. The official dependencies are downloaded from the pinned URLs and verified before extraction. The payload excludes proprietary project data, logs, the optional NiceRF library, the SDK, and bundled runtime/compiler archives. The application host uses Microsoft's .NET apphost; applicable notices are included.

The installer creates a fresh per-user directory, does not replace an existing installation, and performs compile-only tests before committing the directory. It does not program attached hardware. Dependencies are needed at installation time only. For development, read AGENTS.md.

Files:
- Bootstrap.cs - graphical setup, embedded resource extraction and process control.
- InstallOnline.ps1 - dependency download, integrity verification, staged install, local selftests and shortcut.
- dependencies.json - exact upstream archive URLs, versions and checksums.
- BuildPublicInstaller.ps1 - build procedure, requires locally built HERO app and Windows .NET Framework C# compiler.

No NiceRF library is redistributed. Optional LoRa qualification is skipped when that library is absent. Future dependency versions must be explicitly reviewed and pinned, then the installer must be requalified. The installer is unsigned; do not bypass security warnings blindly.
