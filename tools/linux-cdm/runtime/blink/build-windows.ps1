param(
    [Parameter(Mandatory = $true)][string]$CygwinRoot,
    [string]$SourceRoot = (Join-Path $env:TEMP 'grayjay-cdm-blink-build'),
    [string]$Output
)
$ErrorActionPreference = 'Stop'
$module = $PSScriptRoot
$repository = (Resolve-Path (Join-Path $module '../../../..')).Path
if (-not $Output) { $Output = Join-Path $repository 'Grayjay.ClientServer/deps/playback/runtimes/win-x64' }
$pins = Get-Content -LiteralPath (Join-Path $module 'sources.json') -Raw | ConvertFrom-Json
$CygwinRoot = (Resolve-Path -LiteralPath $CygwinRoot).Path
$dll = Join-Path $CygwinRoot 'bin/cygwin1.dll'
if ((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pins.cygwin_dll_sha256) {
    throw 'Use the pinned Cygwin runtime listed in sources.json.'
}
$compiler = & (Join-Path $CygwinRoot 'bin/gcc.exe') -dumpfullversion
if ($LASTEXITCODE -ne 0 -or $compiler.Trim() -ne $pins.gcc_version) { throw 'Unexpected GCC version.' }
if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot '.git'))) {
    & git clone --no-checkout $pins.blink_repository $SourceRoot
    if ($LASTEXITCODE -ne 0) { throw 'Blink clone failed.' }
    & git -C $SourceRoot checkout --detach $pins.blink_commit
    if ($LASTEXITCODE -ne 0) { throw 'Pinned Blink revision is missing.' }
}
$revision = & git -C $SourceRoot rev-parse HEAD
if ($revision -ne $pins.blink_commit) { throw 'Build requires the pinned Blink revision.' }
$patch = Join-Path $module 'cdm.patch'
$ErrorActionPreference = 'Continue'
& git -C $SourceRoot apply --reverse --check $patch 2>$null
$alreadyApplied = $LASTEXITCODE -eq 0
$ErrorActionPreference = 'Stop'
if (-not $alreadyApplied) {
    & git -C $SourceRoot apply --check $patch
    if ($LASTEXITCODE -ne 0) { throw 'CDM patch does not apply cleanly.' }
    & git -C $SourceRoot apply $patch
    if ($LASTEXITCODE -ne 0) { throw 'Applying CDM patch failed.' }
}
Copy-Item -LiteralPath (Join-Path $module 'winmap.c') -Destination (Join-Path $SourceRoot 'blink/winmap.c')
Copy-Item -LiteralPath (Join-Path $module 'winmap.h') -Destination (Join-Path $SourceRoot 'blink/winmap.h')
function CygwinPath([string]$path) {
    $resolved = [IO.Path]::GetFullPath($path)
    if ($resolved[1] -ne ':') { throw 'Build paths require a local drive.' }
    return '/cygdrive/' + [char]::ToLowerInvariant($resolved[0]) + $resolved.Substring(2).Replace('\', '/')
}
$script = Join-Path $SourceRoot 'build-cdm.sh'
$body = @'
set -eu
export PATH=/bin:/usr/bin:"$2"
cd "$1"
# Upstream configure launches many compiler probes concurrently. Keep probes
# sequential too, otherwise the build job can turn OOM into false feature results.
sed -i 's/) \&$/) || :/' configure
./configure --enable-vfs --disable-sockets
if grep -E 'out of memory|Cannot allocate memory' config.log; then
  echo 'configure ran out of memory' >&2
  exit 1
fi
grep -q '^#define HAVE_MAP_ANONYMOUS' config.h
sed -i '/^$(OBJS): $(HDRS) config.h$/d' Makefile
sed -i '/^-include o\/$(MODE)\/depend$/d' Makefile
printf '\n$(OBJS): $(HDRS) config.h\n' >> Makefile
sed -i 's/-march=native/-march=x86-64 -mtune=generic/g' build/config.mk
sed -i '/^CPPFLAGS += -DBLINK_CYGWIN_LINEAR$/d; /^LDFLAGS += -static-libgcc$/d' build/config.mk
printf '\nCPPFLAGS += -DBLINK_CYGWIN_LINEAR\nLDFLAGS += -static-libgcc\n' >> build/config.mk
make -j1 MODE=opt o/opt/blink/blink
'@
[IO.File]::WriteAllText($script, $body.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
& python (Join-Path $module 'windows-budget.py') --memory-mib 512 --cpu-percent 0 --seconds 0 -- (Join-Path $CygwinRoot 'bin/bash.exe') (CygwinPath $script) (CygwinPath $SourceRoot) (CygwinPath (Join-Path $env:SystemRoot 'System32'))
if ($LASTEXITCODE -ne 0) { throw 'Blink compilation failed.' }
& (Join-Path $module 'package-windows.ps1') -CygwinRoot $CygwinRoot -SourceRoot $SourceRoot -Output $Output
