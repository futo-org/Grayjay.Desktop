# Protected playback dependencies

Windows uses the CDM-specific Blink runner. macOS retains QEMU. Windows has no
QEMU fallback or runtime dependency; the app owns Blink through a kill-on-close
job and imposes no production resource caps.

The helper starts on demand when a protected source is selected. App launch and
ordinary playback do not initialize the CDM. After protected playback closes,
the window retains the helper for subsequent protected tracks; window reload
or closure stops it.

## Windows source and build

See [runtime/blink/README.md](runtime/blink/README.md) for prerequisites,
build commands, validation and measured performance. `runtime/blink/sources.json`
pins the upstream repository, commit, compiler and Cygwin artifacts.
`cdm.patch` plus `winmap.c` and `winmap.h` are the maintained source changes;
there is no full upstream vendor copy. Build scripts are owned alongside them.

The Windows build produces `blink.exe`, `cygwin1.dll`, an integrity manifest and
licenses with corresponding Cygwin source. These dependencies use the repository's
existing Git LFS rules. To prepare a release component:

```powershell
python tools/linux-cdm/package-windows.py --output runtime-win-x64.zip
```

`stage-bundled.py` accepts the guest and all three platform runtime archives. It
validates them before staging and replaces the Windows runtime directory so old
QEMU executables, firmware and DLLs cannot survive a refresh. Windows publish
excludes the Linux kernel; Blink extracts only the guest host and libraries.
The macOS build and packaging scripts continue to use `runtime/sources.json`.

## Checks

Run `dotnet run --project tools/linux-cdm/tests/DownloadTests.csproj` for offline
runtime, lifecycle and extraction tests. The opt-in `--blink-reference` mode runs
real Windows CDM playback and checks every decrypted output hash. The older
`tests/guest-playback.py` is a QEMU test for non-Windows hosts.

`python tools/linux-cdm/tests/staging.py` checks stale QEMU removal and rejects
mixed Windows archives without changing a previously staged runtime.

Historical benchmark artifacts under `experiments/` are evidence, not build
inputs. Their prototype patches and launchers are superseded by `runtime/blink/`.
