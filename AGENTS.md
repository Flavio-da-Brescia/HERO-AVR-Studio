# HERO AVR Studio — agent/developer handbook

This is the current source of truth. Version 2.0.0; Windows x64, .NET 10 WinForms and Windows PowerShell 5.1. Canonical root: C:\HERO_AVR_Studio. This is the sole supported installation. Start with this document, DIAGNOSTICS.md, QUICKSTART.md and RELEASE.md. Inspect the actual source before editing; the code is authoritative if details differ. No old installation is required.


## First five minutes for a new AI agent

1. Use the connected Windows computer (Desktop Commander if available). Verify root C:\HERO_AVR_Studio; read AGENTS.md and DIAGNOSTICS.md. Never assume another computer has the same root or COM mapping.
2. For read-only inventory, run agent.ps1 -Module help, core/status, core/ports and projects/list. Inspect actual project.json and source before editing. Inspect production/project and production/status for batch metadata. Do not open a port merely to enumerate it.
3. Identify intended hardware and product before any write or reset. A compile is not permission to program.
4. Prefer managed projects: create -Mode Arduino, NativeC or NativeCpp, edit manifest entry, build, inspect JSON success, HEX SHA-256 and compiler log. Release an immutable version when requested. Write diagnostics using DIAGNOSTICS.md.
5. In multi-command PowerShell scripts, launch each backend as a child process because scripts call exit. Inspect JSON and exit status. Never invent a test pass.

    Set-Location C:\HERO_AVR_Studio
    $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $ps -NoProfile -File .\agent.ps1 -Module help
    & $ps -NoProfile -File .\agent.ps1 -Module core -Action status
    & $ps -NoProfile -File .\agent.ps1 -Module core -Action ports
    & $ps -NoProfile -File .\agent.ps1 -Module projects -Action list

For the diagnostic grammar, complete working code, timeout behavior and gate, read DIAGNOSTICS.md. Run Regression.ps1 and Qualify.ps1 for nonprogramming tests (omit TestPort). Read Build.ps1 before rebuilding the GUI.
## Operating principles
- This is a real hardware environment. Do not program, erase, change fuses or reset an attached board without user authorization. Verify selected board, port and firmware hashes. Do not substitute a simulated test for a physical qualification.
- Do not force-close the editor or a programming process. Preserve unsaved sketches. Only one serial owner per port; use the existing session when possible. Close hardware dialogs before independent commands, and suspend/stop serial before direct upload or diagnostic runs.
- Preserve project/release snapshots, firmware pairs, user sketches/libraries, production records and referenced evidence. Generated compiler caches, finished development serial logs and test outputs can be cleaned; don't remove active sessions or audit evidence.
- Keep the UI and all automation entrypoints consistent. Use JSON backend result fields and exit codes, not UI strings as sole proof. Update these current docs and release.json when architecture or commands change. Do not accumulate duplicate app-v* directories or historical changelogs. Never store secrets or credentials in docs or firmware.

## Hardware and toolchain
Fixed board FQBN arduino:avr:pro:cpu=8MHzatmega328, ATmega328P 3.3 V 8 MHz. Max application 30720 bytes (bootloader reserved), SRAM 2048. Serial upload Arduino/STK500v1 at 57600 baud through CP2105. COM4 is an illustrative test port in the examples; re-detect and verify the intended target before use. Default monitor/diagnostic baud 38400. The diagnostic examples use illustrative pin assignments and nominal supply values; confirm the actual target schematic. NiceRF SX1268 radio uses the local SX1262 library, not an arbitrary online replacement.

HERO Build Engine uses toolchain/avr-gcc (7.3.0-atmel3.6.1-arduino7) and toolchain/avrdude (6.3.0-arduino9) directly. Optional compatibility/arduino-avr contains AVR Core 1.8.8, libraries, variants and bootloaders. No Arduino CLI or package manager. boards/ATmega328P_8MHz.json documents the fixed target; the legacy board ID remains in firmware metadata for compatibility. Runtime and development SDK are bundled separately. See RELEASE.md for compiler behavior, caches, known limitations and validation.

## Source map
- source/HeroAVRStudio.csproj: net10.0-windows, WinExe, WinForms, version. Program.cs: app bootstrap/root discovery, Studio main window, empty sketch template, toolbar, document tracking, save/open, build/upload, serial handoff and CLI utility modes.
- CodeEditor.cs: syntax coloring, indentation, line gutter, bracket matching. FindDialog.cs: search/replace. HeroTheme.cs: shared dark theme, header, sidebar icons, custom memory bars. SerialMonitor.cs: persistent worker control, connection/retry, serial RX/TX, reset, plotting; suspend/resume around upload/hardware dialogs.
- PlotData.cs / PlotWindow.cs: parse numeric serial records and chart/export. LibraryDialog.cs: local library browser/import. ProjectDialog.cs: project management, build/release, production pair configuration. FirmwareCatalog.cs: HEX validation and firmware metadata.
- ProductionDialog.cs: ProductionBackend (process/JSON helper), ProductionConfigDialog (two HEX choices, baud and timeout). ProductionWizard.cs: ProductionDialog, the five-step wizard and locked operator station (live test table/comments/progress/history/report). production.ps1 is the authoritative gate. production-protocol.ps1 is the strict protocol parser.
- IspDialog.cs / isp.ps1: ISP detection, backup, flash/EEPROM, bootloader/fuses; separate from UART. DiagnosticsDialog.cs / diagnostics.ps1: older tokenized request-response profile diagnostics, not the production protocol.
- hero-build.ps1 / hero-preprocess.ps1: direct GCC build orchestration, sketch preprocessing, dependency discovery and content-addressed caches. hero.ps1: compilation/ports/status/library delegation. upload.ps1: application-only Intel HEX, checksum/flash limits, explicit port, staged copy, avrdude verification and logs. libraries.ps1: folder/ZIP import, no overwrite. projects.ps1: source and library snapshots, toolchain fingerprints, builds, immutable releases, restore/rebuild/backup.
- serial.ps1 / serial-worker.ps1: persistent worker with exclusive serial handle and JSONL events/queue; plot.ps1: export serial log using bundled runtime/current app. agent.ps1: common command dispatcher. Launch.cmd: current app. Build.ps1: guarded publish/selftest/deploy. Install.ps1: clean installation. Update-Manifest.ps1: current-file SHA256 inventory.

## Build/edit workflow
Use actual root paths, not former app-v* directories. Edit source or backend scripts; use UTF-8 and preserve PowerShell literal quoting. Read current param blocks when in doubt. Close app normally, then run powershell -NoProfile -File .\Build.ps1. It publishes source/HeroAVRStudio.csproj with local SDK to a temporary stage, calls --selftest, promotes only after success, and regenerates release.json. If build fails, current app remains. Source obj/bin are generated and can be removed when no build is running. Launch.cmd opens app/HeroAVRStudio.exe with DOTNET_ROOT set to runtime. Never overwrite a running executable. GUI selftest checks ports and compiler integration, not interactive GUI or attached firmware. For script changes parse with System.Management.Automation.Language.Parser and test read-only commands first. Firmware build runs are not uploads.

## Agent interface
Run from root, using Windows PowerShell. Examples below are commands, not permission to execute destructive actions. Scripts return JSON and generally use exit 0 on success, 1 on failure; inspect both. agent.ps1 forwards named args to module backend; direct backend scripts are also supported.

```powershell
.\agent.ps1 -Module help
.\agent.ps1 -Module core -Action ports
.\agent.ps1 -Module core -Action status
.\agent.ps1 -Module core -Action selftest
.\agent.ps1 -Module core -Action compile -Sketch 'C:\path\SketchFolder'
.\agent.ps1 -Module core -Action libraries
.\agent.ps1 -Module core -Action library-import -Source 'C:\path\Library.zip'
# Authorized upload; use exact returned hex_path and sha256:
.\agent.ps1 -Module core -Action upload -Port COM4 -Hex '<path>' -Sha256 '<hash>'
```
Build result: success, exit_code, board, sketch, build_id, output_dir, hex_path, sha256, compiler_log, stderr. The hex field is not reliable structured metadata; use hex_path and sha256. Read compiler.log JSON builder_result.executable_sections_size for flash/SRAM. Never upload .with_bootloader.hex.

```powershell
.\agent.ps1 -Module serial -Action start -Port COM4 -Baud 38400
.\agent.ps1 -Module serial -Action status -Session '<32-hex-id>'
.\agent.ps1 -Module serial -Action read -Session '<id>' -Tail 100
.\agent.ps1 -Module serial -Action send -Session '<id>' -Data '<base64>'
.\agent.ps1 -Module serial -Action reset -Session '<id>'
.\agent.ps1 -Module serial -Action stop -Session '<id>'
.\agent.ps1 -Module plot -Action export -Session '<id>'
```
Serial logs: serial-logs/<id>/session.json and events.jsonl; data on rx/tx events is base64 bytes. Reassemble chunks before interpreting lines. dtr/rts accept -Value true or false. Stop releases handle; done indicates completed worker. Use a GUI-owned existing session for agent RX/TX. Plot export requires original session log.

```powershell
.\agent.ps1 -Module projects -Action list
.\agent.ps1 -Module projects -Action create -Name Example -Source '<sketch folder>'
.\agent.ps1 -Module projects -Action history -Name Example
.\agent.ps1 -Module projects -Action build -Name Example
.\agent.ps1 -Module projects -Action release -Name Example -BuildId '<id>' -Version 1.0.0
.\agent.ps1 -Module projects -Action rebuild -Name Example -Version 1.0.0
.\agent.ps1 -Module projects -Action restore -Name Example -Version 1.0.0 -Destination '<new path>'
```
Project name ASCII letter first, then letters/digits/underscore, max 48, no reserved names. Main source is specified in project.json: projects/<name>/src/<name>/<name>.ino, main.c or main.cpp; create with -Mode Arduino, NativeC or NativeCpp. builds/<id> includes source/libraries snapshots, build.json and output; releases/<version> immutable. Do not edit archived content; rebuild/release a new version. Diagnostic source is a separate sketch folder, convention diagnostic-src/<name>/<name>.ino (not inside production sketch folder); compile with core compile. Keep its source in the project.

## Production (schema 2, current)
Both application HEX files are required and must differ. Per-project production.json points to an immutable production-configs/<revision>/pair.json with diagnostic.hex and firmware.hex copies, hashes, diagnostic baud and timeout. New configuration creates new revision, old batches keep their locked copies. No preset count. Wizard steps: select project, verify both HEX checksums, check port availability, prepare batch, production station. New batches optionally record target_quantity (0 = no goal); it is an advisory count of passed attempts, not a hard limit or unique PCB count. Resume only if project, port, both firmware hashes, diagnostic baud and timeout match reviewed configuration. All historical batches remain accessible read-only. Finish batch returns to preparation without deleting or sealing records. The wizard disables navigation/close during programming. Clicking START TEST & PROGRAM PCB authorizes and immediately begins one cycle without an additional confirmation dialog; backend authorization and diagnostic gating remain unchanged.

```powershell
.\agent.ps1 -Module production -Action projects
.\agent.ps1 -Module production -Action project -Project Example
.\agent.ps1 -Module production -Action configure -Project Example -DiagnosticHex '<diag.hex>' -DiagnosticSha256 '<diag hash>' -Hex '<prod.hex>' -Sha256 '<prod hash>' -Baud 38400 -TimeoutSeconds 15
.\agent.ps1 -Module production -Action ports
.\agent.ps1 -Module production -Action check-port -Port COM4 -Baud 38400
.\agent.ps1 -Module production -Action create -Project Example -Revision '<reviewed revision>' -Name 'Batch name' -Operator 'Operator' -Port COM4 -TargetQuantity 100
.\agent.ps1 -Module production -Action list
.\agent.ps1 -Module production -Action status -Batch '<32-hex-id>'
.\agent.ps1 -Module production -Action validate-log -Log '<text file>'
# Only with explicit user authorization and serial port free:
.\agent.ps1 -Module production -Action program -Batch '<id>' -Confirm PROGRAM-BOARD
.\agent.ps1 -Module production -Action report -Batch '<id>'
```
check-port opens and releases a port under the production port lock, with DTR/RTS false; may reset some targets, does not establish board identity. create accepts optional Revision to reject a changed current configuration. Production engine locks batch and port against other production attempts, validates both HEX files, uploads/verifies diagnostic, opens serial, purges old data, DTR resets target, reads ASCII lines, records comments/result and progress. Protocol: TEST 01: description, arbitrary comment lines, RESULT 01: PASS or FAIL, then sequential IDs 02..99. Final standalone line DIAGNOSTIC COMPLETED, exactly once. At least one test, no unmatched or repeated headers/results, no malformed protocol lines, all PASS and marker required. No expected-count field. Timeout (5..600 s) blocks incomplete diagnostics. Marker is followed by a short 750 ms observation period. A diagnostic must not emit a premature marker or silently skip intended tests. If all pass, close serial, revalidate production HEX, upload/verify and record success. Failure or timeout blocks production; a final upload/verification failure is not a pass. Diagnostic may remain installed on failure. Do not disconnect/swap boards mid-attempt. The gate cannot independently know physical board identity or verify claims inside diagnostic firmware.

production/<batch>/batch.json + locked HEX pair; attempts/<sequence>/started.json, progress.json, result.json, diagnostic-serial.txt, diagnostic-upload.json, production-upload.json when applicable. Per-attempt result includes diagnostic_passed, production_verified, diagnostic_upload_id, upload_id, diagnostics.tests with comments/results, and status. Batch serial is an audit record ID, NOT an on-chip serial number. Interrupted attempts remain unknown. CSV reports summarize attempts. Schema-1 batches are legacy read-only. Do not bypass the gate via old program logic. Direct development uploads are separate authorized actions.

## Standalone diagnostics and ISP
Separate legacy diagnostics module uses profile JSON, token/command response, exact/range/increasing/fixture checks with bounded time. Use list/history or run -Profile '<file>' -Port COM4 -Confirm RUN-DIAGNOSTICS. The HardwareDiagnostic example is designed for this subsystem, not production. Unmeasured fixtures are skipped, not passed. Inspect diagnostics/profiles/ATmega328P_basic.json.
ISP detect/backup/verify/write-flash/write-eeprom/bootloader/configure-fuses use actual USBasp or ArduinoISP (not a USB-to-UART adapter). Read isp.ps1 for exact confirmation tokens, and require -ExternalClockConfirmed for external 8 MHz fuse/bootloader changes. Fuse target FF/DA/FD, bootloader lock 0F. Do not set fuses unless physical external clock and programmer confirmed.

## Current validation
Version 2.0.0 passed automated and physical UART/production qualification on the development board. Operational records and project details remain local. Independent clean-PC/offline, ISP, calibrated electrical/LoRa and licensing review are pending.

## Maintenance and paths
Current app/ is the only live binary. source/ contains editable .cs/.csproj. dev-sdk/ is the local development compiler, not an old studio version. runtime/, toolchain/, compatibility/, boards/, workspace/libraries/, licenses/ are dependencies. Projects, workspace sketches, production and linked evidence are user data. build/ contains transient compile output, uploads/ flash records, serial-logs/ monitor sessions, isp-logs/ ISP evidence, diagnostics/runs/ profile runs, plots/ exports. Keep required referenced evidence; clear only obsolete non-active test data. release.json covers selected immutable installation files, not all dependencies/user data. Installer makes a clean workspace and does not migrate projects. Project manifests use absolute paths in some build metadata. For relocation preserve original metadata and verify hashes before rebasing paths. Never rewrite archived source/library/HEX content. Older toolchain fingerprints may require historical tools that are no longer installed; do not bypass fingerprint checks. Audit records are not a second live installation.
