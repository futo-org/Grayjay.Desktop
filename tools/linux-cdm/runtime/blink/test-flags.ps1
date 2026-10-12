param([Parameter(Mandatory = $true)][string]$CygwinRoot, [Parameter(Mandatory = $true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'
$module = $PSScriptRoot
$repository = (Resolve-Path (Join-Path $module '../../../..')).Path
function CygwinPath([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    return '/cygdrive/' + [char]::ToLowerInvariant($full[0]) + $full.Substring(2).Replace('\', '/')
}
& python (Join-Path $module 'prepare-flags-tests.py') $SourceRoot
if ($LASTEXITCODE -ne 0) { throw 'Preparing arithmetic tests failed.' }
Copy-Item -LiteralPath (Join-Path $module 'flags-tests.c') -Destination (Join-Path $SourceRoot 'flags-tests.c')
$script = Join-Path $SourceRoot 'build-flags-tests.sh'
$body = @'
set -eu
export PATH=/bin:/usr/bin:"$2"
cd "$1"
gcc -O2 -DBLINK_CYGWIN_LINEAR -iquote. flags-tests.c -static-libgcc -o flags-tests.exe
'@
[IO.File]::WriteAllText($script, $body.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
& (Join-Path $CygwinRoot 'bin/bash.exe') (CygwinPath $script) (CygwinPath $SourceRoot) (CygwinPath (Join-Path $env:SystemRoot 'System32'))
if ($LASTEXITCODE -ne 0) { throw 'Compiling arithmetic tests failed.' }
$previousPath = $env:PATH
try {
    $env:PATH = (Join-Path $CygwinRoot 'bin') + ';' + $env:PATH
    & python (Join-Path $module 'windows-budget.py') --memory-mib 200 --cpu-percent 0 --seconds 0 -- (Join-Path $SourceRoot 'flags-tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Arithmetic tests failed.' }
} finally { $env:PATH = $previousPath }
