# HERO AVR Studio — Current quick start

## Development
1. Open HERO AVR Studio from the desktop shortcut (Launch.cmd).
2. Select the correct COM port in the right column; the serial monitor connects automatically, normally at 38400 baud. COM4 is an example port; verify the intended board and port; only one process may own it.
3. New or Ctrl+N creates empty setup() and loop(). Save or Open an .ino, .c or .cpp source. Native code uses main(), not setup()/loop(). A sketch folder and its primary .ino must have matching names.
4. Compile (Ctrl+B) builds locally; Upload (Ctrl+U) compiles, asks confirmation, pauses the serial monitor, uploads, verifies flash and reconnects. The fixed AVR application flash limit is 30,720 bytes.
5. Serial monitor: send text or HEX, select ending and baud, reset, show timestamps, open plotter. Only one process can own a COM port.

## Projects and production
In Projects, create or import an Arduino-compatible, Native AVR C or Native AVR C++ managed project (ASCII name with letters/digits/underscore; no spaces), build it, then archive a versioned release if desired. Keep the diagnostic sketch in a separate sketch folder, outside the production source folder; compile separately. In Projects or Production choose Configure production HEX pair, browse for the diagnostic and production application-only HEX files, choose diagnostic baud and timeout, and save. This creates an immutable pair revision. Both HEX files must be present and different. No test-count entry is required.

Open Production and follow the five guided steps:

1. Select project: choose a managed product; View batch history opens any current or legacy batch in read-only mode.
2. Verify firmware: review the two saved HEX files, their byte counts, SHA-256 hashes, diagnostic baud and timeout. Configure HEX pair if needed; Next revalidates both.
3. Check connection: choose/refresh the COM port. Next checks that the port can be opened and released; it does not verify board identity. A serial driver may reset the target when opening the port, but no firmware is written.
4. Prepare batch: enter name, operator and optional target passed PCBs (0 means no target; advisory only), or resume a schema-2 batch matching the selected project, both hashes, port, baud and timeout. Open station checks the reviewed revision and locked files. Historical batches with other settings remain accessible read-only from step 1.
5. Production station: connect the intended PCB and press START TEST & PROGRAM PCB to begin immediately (no confirmation dialog). The station shows live tests, comments, current phase, final verdict, processed/passed/failed/yield counters, attempt history and target progress. Replace the PCB only between cycles. Export report and Open batch folder preserve existing evidence. Finish batch returns to step 1; it does not delete or permanently close the batch.

The serial monitor is suspended while the production wizard is open. Diagnostic flash is written and verified, then the diagnostic is reset and listened to. Only a valid all-PASS sequence followed by DIAGNOSTIC COMPLETED allows production flash and verification. On failure or timeout production is blocked; diagnostic firmware generally remains installed. A final upload failure is a failed attempt. A board can be tested again, so passed counters are attempt counts, not independently identified unique PCBs. Navigation and closing are disabled during a cycle.


Test protocol (ASCII, LF or CRLF; case-sensitive keywords and two-digit sequential IDs 01..99):
```text
TEST 01: Battery voltage
Measured: 4.12 V
RESULT 01: PASS
DIAGNOSTIC COMPLETED
```
Use Serial.println() for each line; send the completion marker once after the final result, including when tests fail. Do not repeat the sequence from loop(). At least one test must execute. Extra, incomplete, mismatched or malformed protocol lines fail validation. Unrelated ordinary startup text is ignored. A missing completion marker times out. A test program must itself execute every intended physical test; the host cannot know if firmware silently omitted a test before emitting completion.

See DIAGNOSTICS.md for complete diagnostic sketch examples and the agent workflow.

## Diagnostic example
See DIAGNOSTICS.md for a battery measurement example. The pin assignment and voltage limits are illustrative and must be verified against the actual board.

Legacy production batches are visible for historical reporting but cannot program. The separate Diagnostics button runs the older profile/request-response diagnostic subsystem; it does not upload production firmware. ISP requires actual supported hardware, not the target UART.

## Rebuild
Close the GUI normally, then from the installation directory run:
```powershell
powershell -NoProfile -File .\Build.ps1
.\Launch.cmd
```
Build.ps1 uses the local dev-sdk, stages a release, runs the compiler integration self-test, then replaces app only after success. Read AGENTS.md before modifying source or scripts.
