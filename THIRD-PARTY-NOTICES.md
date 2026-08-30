# Third-party notices

VAM builds on other people's work. This file lists it, with the attributions
their licences require.

**Status:** the first dependencies now ship. Entries marked *shipped* are
referenced by a project that builds; *planned* ones come from the architecture
decisions and are listed so the obligations are known before the code exists
rather than after the first release. Anything dropped comes out of this file.

---

## RNNoise — *planned*

Real-time noise suppression. Native library, called through P/Invoke.

- Project: https://github.com/xiph/rnnoise
- Licence: BSD 3-Clause
- Copyright (c) 2017, Mozilla; Copyright (c) 2007-2017, Jean-Marc Valin;
  Copyright (c) 2005-2017, Xiph.Org Foundation and contributors;
  Copyright (c) 2003-2004, Mark Borgerding

## Opus — *planned*

Audio codec for the monitor feed sent to remote clients.

- Project: https://opus-codec.org/ · https://github.com/xiph/opus
- Licence: BSD 3-Clause
- Copyright (c) 2001-2023 Xiph.Org, Skype Limited, Octasic, Jean-Marc Valin,
  Timothy B. Terriberry, CSIRO, Gregory Maxwell, Mark Borgerding,
  Erik de Castro Lopo

## SpeexDSP — *planned*

Acoustic echo cancellation and jitter buffering, if the online return is ever
played into the room.

- Project: https://github.com/xiph/speexdsp
- Licence: BSD 3-Clause
- Copyright (c) 2002-2008 Jean-Marc Valin; Copyright (c) 2002 Xiph.org Foundation

## NAudio — *shipped*

Windows audio device access behind the engine's device backend interface,
referenced by `src/Vam.Engine.Windows` only. The `NAudio.Wasapi` package rather
than the `NAudio` metapackage: the device layer wants COM declarations, the
device enumerator and the WASAPI clients, and none of the MIDI, WinMM or file
readers the full package would bring with them.

VAM uses NAudio's declarations and drives `IAudioCaptureClient` itself rather
than using its `WasapiCapture` wrapper — that wrapper creates an event-argument
object per callback, on the one thread whose timing the engine rests on.

- Project: https://github.com/naudio/NAudio
- Licence: MIT
- Copyright (c) Mark Heath and contributors

## gRPC for .NET and Google.Protobuf — *shipped*

The control protocol's transport and its wire format. `Grpc.Tools` generates the client and the
server from `src/Vam.Protocol/Protos/vam.proto` at build time and ships nothing itself.

- Project: https://github.com/grpc/grpc-dotnet · https://github.com/protocolbuffers/protobuf
- Licence: Apache-2.0 (gRPC), BSD 3-Clause (Protobuf)
- Copyright (c) The gRPC Authors; Copyright (c) Google Inc.

## NLog — *shipped*

The logging pipeline behind `ILogger` in the server: a rotated file, the in-memory tail the
diagnostics view reads, and the Sentry sink when a key is configured.

- Project: https://github.com/NLog/NLog
- Licence: BSD 3-Clause
- Copyright (c) 2004-2024 Jaroslaw Kowalski, Kim Christensen, Julian Verdurmen

## Sentry — *shipped*

Error reporting, and disabled entirely when no key is present. Nothing that identifies a person is
sent: no audio, no file contents, and personal information the SDK would otherwise attach by default
is switched off.

- Project: https://github.com/getsentry/sentry-dotnet
- Licence: MIT
- Copyright (c) Sentry

## Shiny.Mediator — *planned*

Mediator pattern implementation used as the application-layer backbone.

- Project: https://github.com/shinyorg/mediator
- Licence: MIT
- Copyright (c) Allan Ritchie and contributors

## Microsoft.Extensions.Logging.Abstractions — *shipped*

The `ILogger` interfaces the engine logs through. No provider is chosen at this
layer; NLog and Sentry are wired up by the host.

- Project: https://github.com/dotnet/runtime
- Licence: MIT
- Copyright (c) .NET Foundation and Contributors

## .NET MAUI, Blazor and the .NET runtime — *planned*

- Project: https://github.com/dotnet
- Licence: MIT
- Copyright (c) .NET Foundation and Contributors

---

## Not dependencies, but worth crediting

VAM exists because these projects showed what was possible and where the gaps
were. No code is taken from any of them.

- **[VB-Audio VoiceMeeter](https://vb-audio.com/Voicemeeter/)** — the reference for
  what a software mixer's operator surface should feel like. Closed source; VAM is
  not a fork or a port of it.
- **[Dan Dugan Sound Design](https://www.dandugan.com/)** — the gain-sharing
  automixing principle VAM's automixer implements. The technique, not the code.
- **[Synchronous Audio Router](https://github.com/eiz/SynchronousAudioRouter)**
  (GPL-3.0) — showed that unlimited named Windows endpoints sharing one clock is
  achievable. Not currently used.
- **[Open Live Mixing System](https://github.com/Open-Live-Mixing-System-OLMS/Open-Live-Mixing-System)**
  — the same idea solved on Linux, and a useful design precedent.

## BSD 3-Clause

The BSD 3-Clause components above are distributed under these terms:

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

- Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

- Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

- Neither the name of the copyright holder nor the names of its contributors
  may be used to endorse or promote products derived from this software without
  specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## MIT

The MIT components above are distributed under these terms:

```
Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```
