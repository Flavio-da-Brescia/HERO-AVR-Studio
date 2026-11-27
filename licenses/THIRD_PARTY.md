# Third-party inventory — third-party dependencies
The local HERO distribution uses the following third-party components. These dependencies are excluded from this public source repository. Review applicable licenses before distributing any bundled installation. Keep upstream copyright notices and license texts, identify corresponding source (including patches and build scripts where required), and review each dependency's obligations before sharing outside the organization.

- Arduino AVR core 1.8.8, built-in libraries and bundled bootloaders: mixed upstream licenses; source and notices retained in compatibility/arduino-avr; upstream https://github.com/arduino/ArduinoCore-avr
- AVR-GCC 7.3.0-atmel3.6.1-arduino7, avr-libc, binutils: inspect individual licenses, exceptions and source archives; upstream Arduino package metadata retained in licenses/provenance/arduino-package-index.json; https://gcc.gnu.org/ and https://www.nongnu.org/avr-libc/
- AVRDUDE 6.3.0-arduino9: upstream https://github.com/avrdudes/avrdude ; package-specific source and notices require review.
- Microsoft .NET: runtime/LICENSE.txt and runtime/ThirdPartyNotices.txt retained; upstream https://github.com/dotnet/runtime and https://github.com/dotnet/winforms.
- NiceRF SX1262 library: original 2018 copyright and local fixes retained; version and redistribution terms not confirmed. Treat as internal only until permission or license is verified.
- Windows USB drivers are external OS requirements, not installed by this package.
The MIT license for HERO-authored source does not alter these dependencies' respective licenses. Do not publish the bundled Windows distribution externally without reviewing its redistribution obligations.
