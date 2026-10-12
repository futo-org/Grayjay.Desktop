param([Parameter(Mandatory = $true)][string]$CygwinRoot, [Parameter(Mandatory = $true)][string]$SourceRoot, [Parameter(Mandatory = $true)][string]$Output)
$ErrorActionPreference = 'Stop'
$module = $PSScriptRoot
$pins = Get-Content -LiteralPath (Join-Path $module 'sources.json') -Raw | ConvertFrom-Json
$dll = Join-Path $CygwinRoot 'bin/cygwin1.dll'
$compiler = & (Join-Path $CygwinRoot 'bin/gcc.exe') -dumpfullversion
if ($LASTEXITCODE -ne 0 -or $compiler.Trim() -ne $pins.gcc_version) { throw 'Unexpected GCC version.' }
$revision = & git -C $SourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $revision -ne $pins.blink_commit) { throw 'Unexpected Blink source revision.' }
$patch = Join-Path $module 'cdm.patch'
function CygwinPath([string]$path) { $resolved = [IO.Path]::GetFullPath($path); return '/cygdrive/' + [char]::ToLowerInvariant($resolved[0]) + $resolved.Substring(2).Replace('\', '/') }
if ((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pins.cygwin_dll_sha256) { throw 'Unexpected Cygwin DLL.' }
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$binary = Join-Path $Output 'blink.exe'
Copy-Item -LiteralPath (Join-Path $SourceRoot 'o/opt/blink/blink.exe') -Destination $binary
& (Join-Path $CygwinRoot 'bin/strip.exe') (CygwinPath $binary)
if ($LASTEXITCODE -ne 0) { throw 'Stripping Blink failed.' }
Copy-Item -LiteralPath $dll -Destination (Join-Path $Output 'cygwin1.dll')
& $binary -h | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Packaged Blink failed its startup smoke test.' }
$imports = & (Join-Path $CygwinRoot 'bin/objdump.exe') -p (CygwinPath $binary)
foreach ($line in $imports) {
    if ($line -match 'DLL Name:\s*(\S+)' -and $Matches[1] -notin @('cygwin1.dll', 'KERNEL32.dll', 'ntdll.dll')) {
        throw "Unexpected DLL dependency: $($Matches[1])"
    }
}
$manifest = [ordered]@{
    version = '1'; protocol = 4; blinkRepository = $pins.blink_repository; blinkCommit = $pins.blink_commit; cpuBaseline = 'x86-64';
    compiler = $compiler.Trim(); cygwinVersion = $pins.cygwin_version;
    blinkSha256 = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant();
    cygwinSha256 = $pins.cygwin_dll_sha256;
    patchSha256 = (Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash.ToLowerInvariant();
    mapperHeaderSha256 = (Get-FileHash -LiteralPath (Join-Path $module 'winmap.h') -Algorithm SHA256).Hash.ToLowerInvariant();
    mapperSha256 = (Get-FileHash -LiteralPath (Join-Path $module 'winmap.c') -Algorithm SHA256).Hash.ToLowerInvariant()
}
[IO.File]::WriteAllText((Join-Path $Output 'blink-runtime.json'), ($manifest | ConvertTo-Json) + "`n", [Text.UTF8Encoding]::new($false))
$licenses = Join-Path $Output 'licenses/blink'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $SourceRoot 'LICENSE') -Destination (Join-Path $licenses 'BLINK-ISC.txt')
Copy-Item -LiteralPath (Join-Path $module 'sources.json') -Destination (Join-Path $licenses 'sources.json')
foreach ($name in @('cdm.patch', 'winmap.c', 'winmap.h', 'build-windows.ps1', 'package-windows.ps1', 'windows-budget.py', 'package-licenses.py', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $module $name) -Destination (Join-Path $licenses $name)
}
& python (Join-Path $module 'package-licenses.py') $CygwinRoot $licenses
if ($LASTEXITCODE -ne 0) { throw 'Packaging runtime licenses and sources failed.' }
Write-Output "Built CDM runner in $Output"
