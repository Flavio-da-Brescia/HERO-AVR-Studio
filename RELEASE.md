# HERO AVR Studio 2.0.1 - current release

Internal operational Windows x64 release. Product name HERO AVR Studio; canonical root C:\HERO_AVR_Studio. The source repository excludes local user projects and evidence. A provisioned local toolchain is required.

## Architecture
.NET 10 WinForms and PowerShell 5.1, explicit system PowerShell path. Fixed ATmega328P 3.3 V external 8 MHz, 30720 B flash, 2048 B SRAM, STK500v1 57600. Bundled AVR-GCC 7.3.0/avr-libc/binutils, AVRDUDE 6.3.0. Optional Arduino AVR Core 1.8.8 for .ino; native .c/.cpp do not require it. Board profile documents fixed target, not dynamic selection. Preserve upstream notices.

hero-build.ps1 orchestrates compile/link/HEX conversion and memory checks. hero-preprocess.ps1 concatenates .ino tabs, generates conservative prototypes and maps line numbers. GCC -M -MG resolves active macro/conditional and transitive dependencies, architecture metadata, and rejects unresolved same-tier ambiguity. Local libraries only; no version solver/download. Complex C++ signatures, conditional definitions, templates, attributes and late-declared types require explicit prototypes as appropriate; unusual syntax/precompiled libraries not fully supported.

Core archives and eligible C/C++ objects content-addressed, SHA-256 verified, atomic publication; keys include preprocessed input, toolchain fingerprints, flags. Assembly bypasses object cache. Link/HEX checks always run. HERO_NO_CACHE=1 bypasses both caches. Archived release rebuild verifies fingerprints/hashes with fresh compile; historical tools may no longer be installed. Archive HEX retained. Cache is disposable.

## Installation
Verify ZIP SHA256 sidecar, extract entire media, run Setup.cmd. Install.ps1 stages per-user installation, checks every file, refuses existing destinations and preserves shortcuts. -Destination <new folder> -NoShortcut for evaluation. Optional SDK only needed to rebuild desktop app. USB drivers external. Package.ps1 creates ZIP excluding project, production, test evidence and caches; includes synthetic tests and installed local libraries. Review licenses. SHA256 provides integrity, not publisher authentication; package unsigned.

## Tests and limits
Regression.ps1 tests compiler/caches; Qualify.ps1 tests known hashes, release lifecycle, protocol and GUI. Reports under qualification/. -RequireOffline requires disconnected non-loopback networks, sampled start/end. Optional -TestPort COM4 explicitly authorizes destructive hardware test and leaves D13 blink. Omit for software-only qualification.

RC1: 15/15 qualification, 7/7 compiler regression, PowerShell PATH independence, 14/14 physical COM4 checks with 258-byte native UART program, DTR reset, 2048/2048 exact binary echo bytes, serial release, hash rejection, verified diagnostic-gated production (15.155 s). Prior installed-package qualification 22/22, 15.481 s physical cycle. Internal 2.0.1 approval is not independent certification. Independent clean-PC/offline, physical ISP, interactive GUI coverage, calibrated analog/LoRa and third-party license clearance still incomplete. No blanket Arduino compatibility guarantee.


## Version 2.0.1
The application uses explicit process-scoped PowerShell execution settings for its trusted bundled backend scripts. No permanent Windows execution policy is changed; enforced organizational policies still take precedence. Invoke agent.cmd from CMD or another agent launcher with the same module/action arguments as agent.ps1.
Projects now open into a persistent, hideable source tree with independent diagnostic build and release history. Drag the shared divider to resize debug and serial output. The ISP tutorial supports USBasp and separate ATmega328P Arduino helpers (Uno R3, Nano new/old bootloader, Pro Mini 3.3 V 8 MHz). Confirm the separate helper and target wiring before any writes. Read-only detection is cancellable and times out after ten seconds of AVRDUDE communication; target bootloader programming requires explicit erase consent. Physical ISP qualification remains pending.