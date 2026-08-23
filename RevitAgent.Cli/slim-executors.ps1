# Slims staged RevitAgent executors: removes debug symbols, the unused PolyHook
# cross-platform runtimes (linux-x64 incl. libcpolyhook2.so, win-x86), and the 13
# Roslyn locale satellite folders. The locale folders are what trigger pack-time
# duplication of the native runtimes under each locale dir (the 1.15GB nupkg bloat).
# win-x64 is kept (PolyHook may probe it at runtime).
#
# Invoked from BuildAndStageExecutors in RevitAgent.Cli.csproj. The staged root is
# passed via the STAGED_ROOT env var (set with cmd's quoted `set "VAR=value"` form,
# which tolerates a trailing backslash in OutputPath without breaking PowerShell's
# own argument parser).
$ErrorActionPreference = 'SilentlyContinue'
$root = $env:STAGED_ROOT
if ([string]::IsNullOrWhiteSpace($root)) { exit 0 }
$root = $root.TrimEnd('\', '/')

$locales = 'cs','de','es','fr','it','ja','ko','pl','pt-BR','ru','tr','zh-Hans','zh-Hant'

foreach ($v in 2019, 2020, 2021, 2022) {
    $d = Join-Path $root "executor-$v"
    if (-not (Test-Path -LiteralPath $d)) { continue }

    # debug symbols (unused at runtime; executor runs LLM-compiled code w/o pdbs)
    Get-ChildItem -LiteralPath $d -Recurse -File -Filter *.pdb | Remove-Item -Force -Recurse

    # Roslyn locale satellites — the pack-time dup trigger
    foreach ($l in $locales) {
        Remove-Item -Recurse -Force -LiteralPath (Join-Path $d $l)
    }

    # unused PolyHook platforms (executor only ever runs win-x64; root dlls remain)
    Remove-Item -Recurse -Force -LiteralPath (Join-Path $d 'runtimes\linux-x64')
    Remove-Item -Recurse -Force -LiteralPath (Join-Path $d 'runtimes\win-x86')
}

exit 0
