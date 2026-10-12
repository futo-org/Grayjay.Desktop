# Windows CDM runner

Windows playback uses the pinned Blink/Cygwin runner directly, with its own
stdin/stdout pipes. It does not launch QEMU, Python, or an installed Cygwin
shell. macOS continues to use the existing QEMU runtime.

The CDM-specific patch uses private 4 KiB Windows mappings over 64 KiB
reservations, lazy inaccessible mappings, compact page-table reservations,
fast reverse address lookup for generated-code faults, and bounded JIT link
searches. Networking and fork are disabled; CDM threads remain supported.
Unused shared mappings are rejected. Empty Windows reservations are released,
and failed mappings roll back their newly installed pages.

The parent owns a kill-on-close Windows job, assigned while the helper is
suspended. Production imposes no memory ceiling, CPU throttle, or overall
runtime deadline. Protocol startup and request timeouts still detect failed
helpers. Optional committed-memory limits remain available to development
validation.

## Rebuilding

Install the pinned Cygwin DLL and GCC compiler listed in `sources.json`, then:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/linux-cdm/runtime/blink/build-windows.ps1 -CygwinRoot C:\path\to\cygwin
```

The builder checks out the pinned Blink commit, applies `cdm.patch`, copies
`winmap.c` and `winmap.h`, and compiles with one job and an x86-64 CPU baseline.
It statically links libgcc, checks imported DLLs, strips the shipping executable,
and generates the runtime integrity manifest. The build-only launcher uses a
512 MiB job ceiling without CPU throttling or a wall-clock cutoff. Configure
probes also run sequentially; allocation failures fail the build instead of
silently disabling required features. Packaging checks native helper startup.

`package-windows.ps1` packages an existing build. It includes Blink's license,
Cygwin's license files, GCC's runtime exception and license, the verified
corresponding Cygwin source archive, and
source pins, the CDM patch, mapper source and build recipes. The app bundles only Blink, Cygwin, the manifest, notices/sources,
and the base guest archive on Windows. The extracted guest contains the CDM
host and its Linux libraries, plus a writable temporary directory for fragments.

## Validation

Run `dotnet run --project tools/linux-cdm/tests/DownloadTests.csproj` on Windows.
For native mapping and allocation-failure rollback checks, run
`runtime/blink/test-winmap.ps1 -CygwinRoot C:\path\to\cygwin -SourceRoot C:\path\to\blink`.
The failure test deliberately uses a 32 MiB development ceiling.

Tests cover native suspended process creation, pipe and argument handling,
optional memory enforcement, process ownership after normal and abrupt exit,
minimal guest extraction, cached-root repair, and archive path rejection.

An opt-in playback benchmark downloads the public reference media and obtains
its test license from Widevine's test proxy. It verifies the decrypted hash on
every iteration and runs the native helper with a 200 MiB development ceiling:

```powershell
dotnet run --project tools/linux-cdm/tests/DownloadTests.csproj -- --blink-reference Grayjay.ClientServer/deps/playback 16
```

Package a staged runtime ZIP with:

```powershell
python tools/linux-cdm/package-windows.py --output runtime-win-x64.zip
```

Playback timings and reference-media checks are recorded in
`../../experiments/`. The reference media has 1,290 decrypted samples and SHA256
`39d48f0c2611ebf76850950a38d046b2b9996f666f3f250d9af6ebf500ada9da`.

The packaged native-launcher validation on 2026-10-09 measured SoundCloud
Backwards at 6.49 seconds for the first track, BA at 2.70 seconds and DARK SIDE
at 2.89 seconds. The helper had no resource caps and sampled private memory
reached about 155 MiB. Unbuffered seeking, automatic queue advancement, normal
exit and forced app termination passed. Reference playback also passed an
explicit 200 MiB development ceiling. These timings are comparable to the
previous prototype; packaging itself does not establish a further latency gain.
The initial license update remains the largest measured delay.

## First-track optimization

The fast arithmetic micro-ops capture status with LAHF and SETO instead of
PUSHFQ/POP. Increment instructions now use the existing flag analysis to select
a simpler micro-op when only zero or no changed flags are needed; carry remains
unchanged. Flag lookahead has a total 256-instruction budget so branching DAGs
cannot cause exponential compilation work. Exhaustion conservatively retains
complete flag calculations.

Run the arithmetic differential test against the actual patched routines:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/linux-cdm/runtime/blink/test-flags.ps1 -CygwinRoot C:\path\to\cygwin -SourceRoot C:\path\to\blink
```

It checks 640,000 inputs and initial flag states against the original stack-based
native flag capture. `../../experiments/inc-flags-probe.s` checks carry preservation,
zero detection, 16-bit preservation, and 32-bit zero extension inside the JIT.

Matched fresh-process SoundCloud trials with the same five-second startup
interval measured mean first playback at 8.477 seconds before and 7.720 seconds
after, an 8.94% improvement. A separate more-settled pair measured 4.476 versus
3.949 seconds. Absolute first-track delays vary; these comparisons keep the
startup interval the same and do not add license warmup. Detailed results are in
`../../experiments/windows-blink-first-track-results.json`.
