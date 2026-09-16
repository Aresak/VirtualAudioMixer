# Third-party notices

Software VAM redistributes, and the notices its licence requires be carried with it.

This file exists so that a binary release can ship these libraries. Where a licence
asks for a copyright notice and a disclaimer to accompany the binary, this is the
document it accompanies it in.

---

## RNNoise

The denoise a release ships. Not required — VAM has a managed suppressor that runs
when RNNoise is absent — but it is the better of the two and a properly packaged
release includes it.

- **Project:** <https://github.com/xiph/rnnoise>
- **Licence:** BSD 3-Clause

> Copyright (c) 2017, Mozilla
> Copyright (c) 2007-2017, Jean-Marc Valin
> Copyright (c) 2005-2017, Xiph.Org Foundation
> Copyright (c) 2003-2004, Mark Borgerding
>
> Redistribution and use in source and binary forms, with or without modification,
> are permitted provided that the following conditions are met:
>
> - Redistributions of source code must retain the above copyright notice, this
>   list of conditions and the following disclaimer.
>
> - Redistributions in binary form must reproduce the above copyright notice, this
>   list of conditions and the following disclaimer in the documentation and/or
>   other materials provided with the distribution.
>
> - Neither the name of the Xiph.Org Foundation nor the names of its contributors
>   may be used to endorse or promote products derived from this software without
>   specific prior written permission.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS ``AS IS'' AND
> ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
> WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
> DISCLAIMED. IN NO EVENT SHALL THE FOUNDATION OR CONTRIBUTORS BE LIABLE FOR ANY
> DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
> (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS
> OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
> THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
> NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN
> IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

### A warning about where you get it

There is no official Windows binary from Xiph — they publish source. Sites that
appear to offer one are worth checking before you trust them: **`rnnoise.com` is not
the project**, it is an unaffiliated page that renames RNNoise to "MNoise" and whose
download button links to the GitHub source archive.

A DLL runs in the same process as the audio engine. Build it, or take it from a
source you would trust with that.

---

## NAudio

The Windows device layer uses NAudio's WASAPI interop.

- **Project:** <https://github.com/naudio/NAudio>
- **Licence:** MIT

---

## Shiny.Mediator

The application layer.

- **Project:** <https://github.com/shinyorg/mediator>
- **Licence:** MIT

---

## Grpc.AspNetCore, Google.Protobuf, NLog, Sentry.NLog

The transport, the wire format and the logging pipeline. All redistributed under
their own terms — Apache-2.0 for the gRPC and protobuf packages, BSD-3-Clause for
NLog, MIT for the Sentry integration.

Their notices travel with the packages themselves; nothing here modifies them.
