# HERO AVR Studio

![License: MIT](https://img.shields.io/badge/License-MIT-green.svg) ![Platform: Windows x64](https://img.shields.io/badge/Platform-Windows%20x64-blue.svg) ![Version: 2.0.1](https://img.shields.io/badge/Version-2.0.1-blue.svg)

**From an idea to tested hardware — with AI as your engineering partner.**

HERO AVR Studio is an independent AVR development and PCB production environment designed and built from the ground up around AI-assisted engineering. It is not a wrapper around Arduino IDE or Arduino CLI. Its own application, build orchestration, project system and production workflow use established AVR-GCC and AVRDUDE tools.

Traditional embedded development asks engineers to learn a collection of tools, interfaces and procedures before they can even begin. HERO takes a different approach: a compatible AI agent with authorized local computer access can read the project, understand the toolchain, write firmware, compile it, inspect errors, communicate with hardware and guide the operator through production. The developer can focus on what the hardware should do instead of memorizing every command.

## Designed for AI agents. Built for engineers.

HERO exposes its capabilities through a consistent, documented command interface. Human operators and AI agents use the same underlying engine, with structured JSON responses, predictable project layouts, explicit hardware authorization, checksums and persistent evidence.

- **Agent-first architecture:** AGENTS.md gives an agent a map of the application, commands, files and operating rules. DIAGNOSTICS.md explains how to write diagnostic firmware and interpret results.
- **Natural-language workflows:** ask a compatible external AI agent to create a project, develop firmware, analyze serial output or prepare a production test. No embedded AI service or API key is required by HERO itself.
- **Independent compilation:** native AVR C/C++ and Arduino-compatible sketches, direct AVR-GCC orchestration, local library discovery, source-aware preprocessing and incremental compilation.
- **Reproducible releases:** managed projects, versioned source and library snapshots, firmware hashes and fresh-build verification.
- **Hardware tools:** verified serial uploads, persistent serial monitor, binary communication, plotting and supported ISP operations.

## From firmware development to PCB production

HERO integrates a guided manufacturing station into the same development environment. Instead of uploading the final application to an untested board, it uses two separately compiled firmware images: a diagnostic and the production application.

    Select project -> Verify firmware pair -> Check connection -> Prepare batch -> Production station

For each board the station:

1. Checks the locked diagnostic and production firmware hashes.
2. Uploads and verifies the diagnostic firmware.
3. Receives individual measurements and PASS/FAIL results over the serial port.
4. Waits for a complete, valid diagnostic transcript.
5. Only when every test passes, uploads and verifies the final production firmware.
6. Records the outcome, test comments, firmware identities and timing in an auditable batch history.

A failed, incomplete or malformed diagnostic blocks production programming. The diagnostic determines which physical measurements to perform; HERO enforces the protocol and firmware gate. A successful result is not a substitute for calibrated fixtures or independent verification of the diagnostic itself.

A diagnostic can report any number of tests (1–99), without configuring an expected count:

    TEST 01: Supply voltage
    Measured: 3.30 V
    RESULT 01: PASS
    TEST 02: Peripheral communication
    RESULT 02: PASS
    DIAGNOSTIC COMPLETED

The station tracks processed, passed and failed attempts, yield and batch progress, and can export CSV reports. The completion marker means testing has finished, not that all tests passed.

## Supported target

Current release: **2.0.1**. Windows x64, ATmega328P, external 8 MHz clock, 3.3 V, STK500v1 serial bootloader at 57600 baud. Application flash limit: 30,720 bytes; SRAM: 2,048 bytes. Additional board support is not yet implemented.

The application has passed automated compiler regression tests and physical UART/diagnostic-gated production tests. Independent clean-PC offline validation, physical ISP and calibrated electrical/radio qualification remain incomplete.

## Start here

| Document | Purpose |
|---|---|
| [AGENTS.md](AGENTS.md) | Complete AI agent and developer handbook |
| [DIAGNOSTICS.md](DIAGNOSTICS.md) | Diagnostic firmware protocol, example and production workflow |
| [QUICKSTART.md](QUICKSTART.md) | Human operator guide |
| [RELEASE.md](RELEASE.md) | Architecture, validation and current limitations |
| [Third-party inventory](licenses/THIRD_PARTY.md) | Dependency and redistribution considerations |

A compatible agent can discover the available commands from an appropriately provisioned installation:

    .\agent.ps1 -Module help

## Source checkout

This repository contains the HERO application source, build and packaging scripts, documentation and synthetic examples. It is not a complete Windows installer. The runtime, .NET SDK, AVR toolchain, optional Arduino compatibility core and local third-party libraries are excluded. Provision these from authorized sources at matching versions as described in RELEASE.md and the third-party inventory. The LoRa test requires the locally authorized SX1262 library. No user projects, production records, serial logs, compiled firmware or credentials are included.

For development, create the expected local working directories and dependencies, then run Build.ps1 to generate the desktop application and release manifest. Regression.ps1 and Qualify.ps1 provide nonprogramming checks by default; explicitly authorized hardware tests are optional. Never program a board merely because a COM port exists.

## License

HERO-authored application source, documentation and examples in this repository are released under the [MIT License](LICENSE). Third-party dependencies retain their respective licenses. External toolchain and runtime distributions are excluded; the ArduinoISP example retains its separate BSD notice in licenses/THIRD_PARTY.md. The MIT license does not relicense third-party code.

## Windows online installer

Run HERO_AVR_Studio_Setup.exe from the GitHub release assets. Select an installation folder and click Install. The installer obtains pinned, hash-verified AVR tools, AVR core and .NET runtime from their upstream publishers, verifies the local compiler and application, and creates a desktop shortcut. No administrator access, Arduino IDE or Arduino CLI is needed. Internet is required for setup, not normal firmware development. The optional SX1262 library is not included; obtain it separately under its applicable terms. USB driver installation may be required. The installer is unsigned.


## Version 2.0.1
The application uses explicit process-scoped PowerShell execution settings for its trusted bundled backend scripts. No permanent Windows execution policy is changed; enforced organizational policies still take precedence. Invoke agent.cmd from CMD or another agent launcher with the same module/action arguments as agent.ps1.
Projects now open into a persistent, hideable source tree with independent diagnostic build and release history. Drag the shared divider to resize debug and serial output. The ISP tutorial supports USBasp and separate ATmega328P Arduino helpers (Uno R3, Nano new/old bootloader, Pro Mini 3.3 V 8 MHz). Confirm the separate helper and target wiring before any writes. Read-only detection is cancellable and times out after ten seconds of AVRDUDE communication; target bootloader programming requires explicit erase consent. Physical ISP qualification remains pending.