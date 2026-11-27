# Third-party inventory — third-party dependencies
The local HERO distribution uses the following third-party components. These dependencies are excluded from this public source repository. Review applicable licenses before distributing any bundled installation. Keep upstream copyright notices and license texts, identify corresponding source (including patches and build scripts where required), and review each dependency's obligations before sharing outside the organization.

- Arduino AVR core 1.8.8, built-in libraries and bundled bootloaders: mixed upstream licenses; source and notices retained in compatibility/arduino-avr; upstream https://github.com/arduino/ArduinoCore-avr
- AVR-GCC 7.3.0-atmel3.6.1-arduino7, avr-libc, binutils: inspect individual licenses, exceptions and source archives; upstream Arduino package metadata retained in licenses/provenance/arduino-package-index.json; https://gcc.gnu.org/ and https://www.nongnu.org/avr-libc/
- AVRDUDE 6.3.0-arduino9: upstream https://github.com/avrdudes/avrdude ; package-specific source and notices require review.
- Microsoft .NET: runtime/LICENSE.txt and runtime/ThirdPartyNotices.txt retained; upstream https://github.com/dotnet/runtime and https://github.com/dotnet/winforms.
- NiceRF SX1262 library: original 2018 copyright and local fixes retained; version and redistribution terms not confirmed. Treat as internal only until permission or license is verified.
- Windows USB drivers are external OS requirements, not installed by this package.
The MIT license for HERO-authored source does not alter these dependencies' respective licenses. Do not publish the bundled Windows distribution externally without reviewing its redistribution obligations.
## ArduinoISP

The ArduinoISP example is retained unmodified from the installed Arduino IDE. Its source identifies the author and the original BSD license URL. The legacy URL has changed redirects over time; the notice below retains the traditional three-clause BSD conditions, including the non-endorsement condition. See https://github.com/arduino/arduino-examples/tree/main/examples/11.ArduinoISP/ArduinoISP and the original source header. The HERO MIT license does not apply to this example.

Copyright (c) 2008-2011 Randall Bohn.
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
