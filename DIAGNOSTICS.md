# Diagnostic sketches and PCB production

Required behavior for any human or AI agent creating production diagnostics in HERO AVR Studio 2.0.1. production-protocol.ps1 and production.ps1 are the implementation of record.

## Two separate firmware images

Diagnostic firmware tests the actual PCB, reports results over UART and finishes once. Production firmware is the final application, uploaded only after valid complete all-PASS diagnostics. Source layout:

    projects/<Project>/src/<Project>/<Project>.ino (or main.c/main.cpp)
    projects/<Project>/diagnostic-src/<Diagnostic>/<Diagnostic>.ino

Keep diagnostic source OUTSIDE the production sketch folder; otherwise .ino tabs may be concatenated. Compile separately. Both application-only HEX files must differ. Never use with_bootloader.hex.

## Required serial grammar

ASCII, LF or CRLF. Default 38400 baud, 8N1; match configured baud. Sequential two-digit IDs 01..99; case-sensitive keywords and results. Exactly one header and matching result per test. Ordinary comments between them are displayed in the station. Finish each test before starting the next.

    ATmega328P 3.3V 8MHz | Battery diagnostic
    TEST 01: Battery voltage greater than 3.5 V
    Measured: 4.10 V (estimated)
    RESULT 01: PASS
    TEST 02: Example second measurement
    Measured value: 12
    RESULT 02: FAIL
    DIAGNOSTIC COMPLETED

DIAGNOSTIC COMPLETED is the exact standalone completion line, sent ONCE after the last result even if tests failed. Completion is not success. There is NO configured test count. At least one test must execute; all must have a result and pass. Duplicate IDs, missing/mismatched results, malformed reserved lines, premature/repeated completion or timeout fail the gate. Avoid comments beginning TEST, RESULT or DIAGNOSTIC COMPLETED. Startup text outside tests is ignored. Host observes briefly (750 ms) after completion. Never repeat the sequence from loop().

## Writing a real diagnostic

- Start with target/program identification, then actual measured results. Do not print unconditional PASS to represent a hardware measurement. Include units, limits, fixture assumptions. An unavailable required fixture must fail. Host reception verifies UART TX; true RX requires host-originated stimulus (e.g. a separate echo test).
- Run once in setup() or native main(). Empty loop() or idle afterward. Allow startup time after Serial.begin (typically 1500 ms for serial tests), flush serial after the final marker.
- Bound all waits; set overall timeout (5..600 seconds, default 15). Include time for startup, measurements and observation.
- Verify actual pinout, reference voltage, divider, pull-ups, expected state and peripheral/fixture requirements before selecting limits. Calibrate analog measurements. COM4 is a port, not a PCB identity.
- Do not drive loads or high-power outputs without a verified safe fixture and appropriate authorization. Do not equate a software message with physical calibration, RF or mechanical qualification.

## Complete working battery example

Only applicable to the documented board with a 50% divider on A3 and nominal 3.300 V AVCC. Threshold strictly greater than 3.500 V. Actual calibration required. 32 samples; compare before rounding.

```cpp
// Test Project | Battery diagnostic
// A3: 50% battery divider; nominal AVCC reference 3300 mV.
// Calibrate reference and divider before production qualification.
constexpr uint8_t BATTERY_PIN = A3;
constexpr uint8_t SAMPLES = 32;
constexpr uint32_t REFERENCE_MV = 3300;
constexpr uint32_t THRESHOLD_MV = 3500;

void setup() {
  Serial.begin(38400);
  delay(1500);
  Serial.println(F("ATmega328P 3.3V 8MHz | Battery diagnostic"));
  analogReference(DEFAULT);
  pinMode(BATTERY_PIN, INPUT);
  digitalWrite(BATTERY_PIN, LOW);
  delay(300);
  Serial.println(F("TEST 01: Battery voltage greater than 3.5 V"));
  analogRead(BATTERY_PIN); // discard first conversion
  uint32_t sum = 0;
  for (uint8_t i = 0; i < SAMPLES; ++i) {
    sum += analogRead(BATTERY_PIN);
    delay(10);
  }
  // Compare before rounding; nominal divider ratio = 1/2.
  const uint32_t numerator = sum * REFERENCE_MV * 2UL;
  const uint32_t denominator = 1023UL * SAMPLES;
  const uint32_t batteryMv = (numerator + denominator / 2) / denominator;
  const bool passed = numerator > THRESHOLD_MV * denominator;
  Serial.print(F("ADC average: "));
  Serial.println((float)sum / SAMPLES, 2);
  Serial.print(F("Battery voltage: "));
  Serial.print(batteryMv / 1000.0f, 3);
  Serial.println(F(" V (nominal reference and divider)"));
  Serial.println(F("Threshold: strictly greater than 3.500 V"));
  if (passed) Serial.println(F("RESULT 01: PASS"));
  else Serial.println(F("RESULT 01: FAIL"));
  Serial.println(F("DIAGNOSTIC COMPLETED"));
  Serial.flush();
}

void loop() {}

```

Adapt the pinout, measured limits and timing to the target. This documentation example includes a startup banner and settling delay; adapt them to the actual reset and serial path.

## Agent workflow: compile and configure

Use Windows PowerShell at root; each backend calls exit, so launch it as a child process. Replace example paths/names. Edit both sources before compiling.

    $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $ps -NoProfile -File .\agent.ps1 -Module projects -Action create -Name Example -Mode Arduino
    $p = & $ps -NoProfile -File .\agent.ps1 -Module projects -Action build -Name Example | ConvertFrom-Json
    $d = & $ps -NoProfile -File .\agent.ps1 -Module core -Action compile -Sketch (Join-Path (Get-Location) 'projects\Example\diagnostic-src\ExampleDiagnostic') | ConvertFrom-Json
    if (!$p.success -or !$d.success) { throw 'Compile failed' }
    $r = & $ps -NoProfile -File .\agent.ps1 -Module production -Action configure -Project Example -DiagnosticHex $d.hex_path -DiagnosticSha256 $d.sha256 -Hex $p.build.result.hex_path -Sha256 $p.build.result.sha256 -Baud 38400 -TimeoutSeconds 15 | ConvertFrom-Json
    if (!$r.success) { throw 'Configuration failed' }

Review both hashes, baud and physical target. GUI Production: Select project -> Verify firmware -> Check connection -> Prepare batch -> Production station. START TEST & PROGRAM PCB starts immediately, no second confirmation. Do not swap boards mid-cycle. Agent: check-port (may reset), create with reviewed Revision, then program with explicit -Confirm PROGRAM-BOARD only after authorization. See AGENTS.md for exact commands. Never bypass the gate with direct upload.

## What the host verifies

1. Validate locked diagnostic and production HEX/checksums.
2. Upload and verify diagnostic flash.
3. Open UART, discard stale input, reset, parse test lines until completion/timeout.
4. Permit final upload only for a valid complete all-PASS transcript.
5. Revalidate production HEX, upload and verify flash, record outcome.

Any failure blocks success; diagnostic may remain installed. A FAIL followed by completion is still FAIL. Final flash verification failure is not a pass. The host cannot detect silently omitted measurements or independently establish PCB identity. Attempt IDs identify audit records, not physical PCBs.

## Troubleshooting and evidence

Validate an ASCII transcript without hardware:
    agent.ps1 -Module production -Action validate-log -Log <file>
Timeout: check serial owner, baud, reset wiring, bootloader, delay and exact marker. FAIL: inspect measured value and fixture; never substitute PASS to proceed. Hash mismatch: check locked files and compile logs. Do not edit historical batches.

Evidence: production/<batch>/attempts/<sequence>/result.json, diagnostic-serial.txt, upload records. Success requires status=passed, diagnostic_passed=true, production_verified=true. Use production/report for CSV. Preserve linked evidence.

The separate Diagnostics tool uses request/response profiles, not this production protocol; consult diagnostics.ps1 and the profiles. Do not mix the two.


## Version 2.0.1
The application uses explicit process-scoped PowerShell execution settings for its trusted bundled backend scripts. No permanent Windows execution policy is changed; enforced organizational policies still take precedence. Invoke agent.cmd from CMD or another agent launcher with the same module/action arguments as agent.ps1.
Projects now open into a persistent, hideable source tree with independent diagnostic build and release history. Drag the shared divider to resize debug and serial output. The ISP tutorial supports USBasp and separate ATmega328P Arduino helpers (Uno R3, Nano new/old bootloader, Pro Mini 3.3 V 8 MHz). Confirm the separate helper and target wiring before any writes. Read-only detection is cancellable and times out after ten seconds of AVRDUDE communication; target bootloader programming requires explicit erase consent. Physical ISP qualification remains pending.