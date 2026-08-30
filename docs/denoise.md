# The denoise, and where RNNoise comes from

**Short version: you do not need to do anything.** VAM runs without RNNoise, the denoise still
works, and the engine says which one it is using in the log on every start. Read on only if you want
the better one.

## What VAM ships with

A managed spectral-subtraction suppressor, written for this project. It removes steady background
noise — ventilation, a projector fan, the hum of a full room — and it sounds like what it is:
serviceable, and audibly not as good as a trained model on a difficult room.

It is the default because it has no dependencies and cannot fail to load.

## What RNNoise is

[RNNoise](https://github.com/xiph/rnnoise) is Xiph's recurrent-neural-network noise suppressor.
It is small, fast, trained on speech, and much better than spectral subtraction on the kind of noise
a council chamber actually has.

**It is BSD-3-Clause licensed and it is not distributed with VAM.** That is deliberate rather than
an oversight: it is somebody else's work under its own licence, and vendoring a binary blob into a
public repository is a thing to do on purpose or not at all.

## Getting it on Windows

Xiph publishes **source only**. There is no official Windows `.dll` in their releases, so there is
no honest "download it from here" link to give you. You have two options.

### Build it yourself

The upstream build is autotools, which on Windows means [MSYS2](https://www.msys2.org/):

```bash
pacman -S --needed base-devel mingw-w64-x86_64-toolchain autoconf automake libtool
```

```bash
git clone https://github.com/xiph/rnnoise && cd rnnoise && ./autogen.sh && ./configure --enable-shared && make
```

`autogen.sh` downloads the trained model, so it needs a network connection. The result is under
`.libs/`; copy the DLL next to `Vam.Server.exe` and rename it `rnnoise.dll` if it is not called that
already.

### Take a prebuilt one

Several projects ship a compiled RNNoise, and community CMake forks publish Windows releases. VAM
does not endorse or verify any of them, and **a DLL from a stranger runs in the same process as your
audio engine** — treat it the way you would treat any other binary you did not build.

## Telling VAM about it

Put `rnnoise.dll` beside the engine executable. That is the whole installation: the engine looks for
it once at startup, uses it if it loads, and falls back if it does not.

You will see one of these in the log, every start:

```
Denoise is RNNoise, through the native library.
```

```
RNNoise is not installed, so the denoise is the managed spectral suppressor.
```

If you see the second one after copying the DLL, it is almost always the wrong architecture — VAM is
64-bit, so a 32-bit build will not load.

## What VAM expects of it

Three things, and they are why a DLL that loads can still sound wrong:

- **48 kHz, 480-sample frames.** That is what the network was trained on, not a setting. VAM runs
  120-sample blocks and buffers four of them, which costs ten milliseconds of latency — declared to
  the engine so the automixer aligns the other strips around it.
- **Sixteen-bit sample scale, not ±1.** Handed floats in the range the rest of the graph uses,
  RNNoise decides the signal is silence and gates the lot.
- **The standard three entry points**: `rnnoise_create`, `rnnoise_destroy`, `rnnoise_process_frame`.

A fork that changed any of those will load and misbehave rather than failing cleanly.

## Turning it down

The denoise has a **strength** control rather than an on/off switch, whichever backend is running.
Pushed to the top, RNNoise makes speech sound underwater. The right amount is a judgement made by
listening to the actual room, which is why it is a knob and not a checkbox.
