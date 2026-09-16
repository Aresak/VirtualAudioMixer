@echo off
setlocal enabledelayedexpansion

rem Builds rnnoise.dll for a VAM release, with MSVC and nothing else installed.
rem
rem This exists because the upstream build is autotools, which on Windows means installing MSYS2 and
rem a MinGW toolchain to produce one small DLL. RNNoise is plain C with no dependencies, so the
rem compiler that is already on a Windows development machine can build it directly. What autotools
rem does that this has to do by hand is fetch the trained model and pick the SIMD paths; both are
rem below.
rem
rem Usage:  tools\build-rnnoise.cmd [output-directory]
rem Needs:  a "Developer Command Prompt for VS" - or run vcvars64.bat first - plus git, curl and tar,
rem         all three of which ship with Windows 10 and later.
rem
rem The result goes beside Vam.Server.exe. Check the engine log on the next start: it names the
rem suppressor it picked every time, and that line is the only proof the DLL loaded rather than
rem being silently the wrong architecture.

if "%VSCMD_ARG_TGT_ARCH%" NEQ "x64" (
    echo This needs a 64-bit MSVC environment. Open "x64 Native Tools Command Prompt for VS"
    echo or run vcvars64.bat, then run this again.
    exit /b 1
)

set OUTDIR=%~1
if "%OUTDIR%"=="" set OUTDIR=%CD%\artifacts

set WORK=%TEMP%\vam-rnnoise
if not exist "%WORK%" mkdir "%WORK%"
cd /d "%WORK%" || exit /b 1

if not exist rnnoise (
    echo Cloning RNNoise...
    git clone --depth 1 https://github.com/xiph/rnnoise || exit /b 1
)
cd rnnoise || exit /b 1

rem ---------------------------------------------------------------------------
rem The model. Upstream stores the archive's own sha256 in model_version and uses
rem it as the filename, so the version and the checksum are the same string -
rem which is why this can verify without a second file to keep in step.
rem ---------------------------------------------------------------------------

set /p MODELHASH=<model_version
set MODEL=rnnoise_data-%MODELHASH%.tar.gz

if not exist "%MODEL%" (
    echo Downloading the trained model...
    curl -fLO "https://media.xiph.org/rnnoise/models/%MODEL%" || exit /b 1
)

for /f "skip=1 tokens=*" %%h in ('certutil -hashfile "%MODEL%" SHA256') do (
    if "!ACTUAL!"=="" set ACTUAL=%%h
)
set ACTUAL=%ACTUAL: =%

if /i "%ACTUAL%" NEQ "%MODELHASH%" (
    echo Checksum mismatch. Expected %MODELHASH%, got %ACTUAL%.
    echo Delete "%WORK%\rnnoise\%MODEL%" and run this again.
    exit /b 1
)
echo Checksum matches.

tar xzf "%MODEL%" || exit /b 1

rem ---------------------------------------------------------------------------
rem The build. Three groups, because they need three different instruction sets.
rem ---------------------------------------------------------------------------

if exist obj rmdir /s /q obj
mkdir obj
if not exist "%OUTDIR%" mkdir "%OUTDIR%"

set COMMON=/nologo /c /O2 /MD /DWIN32 /DRNNOISE_BUILD /DDLL_EXPORT /DRNN_ENABLE_X86_RTCD /D_CRT_SECURE_NO_WARNINGS /Iinclude /Isrc

rem AVX2 gets its own translation unit and its own flag. Everything else stays at the x64 baseline,
rem so the DLL still loads on a machine without AVX2 - rnn_select_arch reads CPUID at startup and
rem picks the fastest path that machine actually has.
cl %COMMON% /arch:AVX2 src\x86\nnet_avx2.c /Fo:obj\ || exit /b 1

rem MSVC never defines __SSE4_1__, because it has no /arch flag for it: every x64 target has SSE4.1
rem unconditionally. The upstream file guards on the GCC macro, so it is defined here rather than the
rem guard being patched out.
cl %COMMON% /D__SSE4_1__ src\x86\nnet_sse4_1.c /Fo:obj\ || exit /b 1

cl %COMMON% src\x86\x86_dnn_map.c src\x86\x86cpu.c /Fo:obj\ || exit /b 1

rem nnet_default.c prints "Compiling without any vectorization" here. That is correct and not a
rem warning to chase: it is the scalar fallback for a CPU with neither of the above.
cl %COMMON% src\denoise.c src\rnn.c src\pitch.c src\kiss_fft.c src\celt_lpc.c ^
   src\nnet.c src\nnet_default.c src\parse_lpcnet_weights.c ^
   src\rnnoise_data.c src\rnnoise_tables.c /Fo:obj\ || exit /b 1

link /nologo /DLL /OUT:"%OUTDIR%\rnnoise.dll" obj\*.obj || exit /b 1

echo.
echo Built %OUTDIR%\rnnoise.dll
echo Copy it beside Vam.Server.exe, and ship THIRD-PARTY-NOTICES.md with it.
endlocal
