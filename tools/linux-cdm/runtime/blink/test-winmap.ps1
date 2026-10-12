param([Parameter(Mandatory = $true)][string]$CygwinRoot, [Parameter(Mandatory = $true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'
$module = $PSScriptRoot
$repository = (Resolve-Path (Join-Path $module '../../../..')).Path
function CygwinPath([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    return '/cygdrive/' + [char]::ToLowerInvariant($full[0]) + $full.Substring(2).Replace('\', '/')
}
foreach ($name in @('winmap-tests.c', 'winmap-failure-tests.c')) {
    Copy-Item -LiteralPath (Join-Path $module $name) -Destination (Join-Path $SourceRoot $name)
}
$script = Join-Path $SourceRoot 'build-winmap-tests.sh'
$body = @'
set -eu
export PATH=/bin:/usr/bin:"$2"
cd "$1"
gcc -O2 -DBLINK_CYGWIN_LINEAR -iquote. winmap-tests.c blink/winmap.c -static-libgcc -o winmap-tests.exe
gcc -O2 -DBLINK_CYGWIN_LINEAR -iquote. winmap-failure-tests.c blink/winmap.c -static-libgcc -o winmap-failure-tests.exe
'@
[IO.File]::WriteAllText($script, $body.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
& (Join-Path $CygwinRoot 'bin/bash.exe') (CygwinPath $script) (CygwinPath $SourceRoot) (CygwinPath (Join-Path $env:SystemRoot 'System32'))
if ($LASTEXITCODE -ne 0) { throw 'Compiling mapping tests failed.' }
$previousPath = $env:PATH
try {
    $env:PATH = (Join-Path $CygwinRoot 'bin') + ';' + $env:PATH
    & python (Join-Path $module 'windows-budget.py') --memory-mib 200 --cpu-percent 0 --seconds 0 -- (Join-Path $SourceRoot 'winmap-tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Mapping tests failed.' }
    & python (Join-Path $module 'windows-budget.py') --memory-mib 32 --cpu-percent 0 --seconds 0 -- (Join-Path $SourceRoot 'winmap-failure-tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Mapping rollback tests failed.' }
} finally { $env:PATH = $previousPath }
