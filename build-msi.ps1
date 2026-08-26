# Build a current revit-agent.msi from source, end to end.
#
# Pipeline:
#   dotnet publish CLI (win-x64, self-contained) -> installer-payload\  (CLI runtime)
#   dotnet publish GUI (win-x64, self-contained) -> installer-payload\  (root-merge: adds the
#     WindowsDesktop/WPF runtime + GUI assemblies; overlapping NETCore files are byte-identical)
#   copy bin\Release\net10.0\win-x64\executor-<v>\ -> installer-payload\executor-<v>\
#     publish -o copies the CLI runtime into the payload but does NOT carry the
#     executor folders that BuildAndStageExecutors staged into the RID-specific
#     build output; they are already slimmed (slim-executors.ps1 ran in the build),
#     so copy them in.
#   gen-payload-fragment.ps1 -> payload-fragment.wxs (WiX v7 has no `heat`)
#   wix build -arch x64 -> revit-agent.msi (+ revit-agent.wixpdb)
#
# Output lives at the repo root and is gitignored (local-only — never committed).
# The MSI contains NO API key: the key is read at runtime from the env var named by
# config.ApiKeyEnv (default REVIT_AGENT_API_KEY); the installer only appends PATH.
#
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File build-msi.ps1
# Prereq: .NET 10 SDK + WiX v7 on PATH (winget install wixtoolset.WiXToolset).

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$Cli      = "RevitAgent.Cli\RevitAgent.Cli.csproj"
$Payload  = "installer-payload"
$Versions = 2019, 2020, 2021, 2022

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw "wix CLI not found on PATH. Install WiX v7 (winget install wixtoolset.WiXToolset) and re-run."
}

Write-Host "==> Publish self-contained (win-x64) -> $Payload\" -ForegroundColor Cyan
# Clean first: publish overwrites changed files but will NOT delete files that are
# no longer produced (removed deps, dropped runtimes), which would leave a stale
# payload baked into the MSI.
if (Test-Path $Payload) { Remove-Item -Recurse -Force $Payload }
dotnet publish $Cli -c Release -r win-x64 --self-contained true -o $Payload
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

# GUI: publish self-contained into the SAME payload root (root-merge). Overlapping files are
# the Microsoft.NETCore.App runtime, byte-identical here (same SDK/machine/script), so the
# overwrite is harmless; the GUI adds the WindowsDesktop (WPF) runtime + Wpf.Ui/MdXaml
# assemblies additively. App-identity files don't collide (revit-agent.* vs RevitAgent.Gui.*).
# Both exes in the install root also keep ExecutorLocator/SkillStore BaseDirectory probes
# working identically for the GUI. Note: the GUI publish also builds the CLI with -r win-x64
# via its ProjectReference — same staged output $StagedRoot reads below, harmless duplicate.
$Gui = "RevitAgent.Gui\RevitAgent.Gui.csproj"
Write-Host "==> Publish GUI self-contained (win-x64) -> $Payload\" -ForegroundColor Cyan
dotnet publish $Gui -c Release -r win-x64 --self-contained true -o $Payload
if ($LASTEXITCODE -ne 0) { throw "GUI dotnet publish failed (exit $LASTEXITCODE)" }
# Root-merge sanity: both hosts + the WPF runtime must coexist after the merged publish.
foreach ($exe in "revit-agent.exe", "RevitAgent.Gui.exe") {
    if (-not (Test-Path (Join-Path $Payload $exe))) { throw "Payload missing $exe after publish" }
}
if (-not (Test-Path (Join-Path $Payload "PresentationFramework.dll"))) {
    throw "WPF runtime missing from payload (WindowsDesktop runtime not merged)"
}

# publish -o brought in the CLI runtime but not the executor folders; copy them from
# the RID-specific build output where BuildAndStageExecutors staged (already slimmed).
$StagedRoot = "RevitAgent.Cli\bin\Release\net10.0\win-x64"
Write-Host "==> Copy slimmed executors into payload" -ForegroundColor Cyan
foreach ($v in $Versions) {
    $src = Join-Path $StagedRoot "executor-$v"
    $dst = Join-Path $Payload  "executor-$v"
    if (-not (Test-Path $src)) { throw "Staged executor not found: $src" }
    if (Test-Path $dst) { Remove-Item -Recurse -Force $dst }
    Copy-Item -Recurse -Force $src $dst
    $exe = Join-Path $dst "RevitAgent.Executor.$v.exe"
    if (-not (Test-Path $exe)) { throw "Executor copy incomplete: $exe" }
}

# Bundled read-only skills: staged into the RID-specific build output by BuildAndStageExecutors.
# `publish -o` does NOT carry custom-staged folders (same as the executors above), so copy the
# staged skills/ into the payload. gen-payload-fragment.ps1 harvests the whole payload tree,
# so skills/ then lands in the MSI.
$skillsSrc = Join-Path $StagedRoot "skills"
if (Test-Path $skillsSrc) {
    $skillsDst = Join-Path $Payload "skills"
    if (Test-Path $skillsDst) { Remove-Item -Recurse -Force $skillsDst }
    Copy-Item -Recurse -Force $skillsSrc $skillsDst
    $n = (Get-ChildItem -Recurse -File $skillsDst).Count
    Write-Host "  skills: $n files bundled (read-only)"
} else {
    Write-Host "  skills: none staged - MSI ships without bundled skills" -ForegroundColor Yellow
}

Write-Host "==> Generate WiX payload fragment" -ForegroundColor Cyan
& "$PSScriptRoot\gen-payload-fragment.ps1" -Payload $Payload -OutFile payload-fragment.wxs
if (-not (Test-Path payload-fragment.wxs)) { throw "payload-fragment.wxs was not generated" }

Write-Host "==> Build MSI (wix build -arch x64)" -ForegroundColor Cyan
wix build installer.wxs payload-fragment.wxs -arch x64 -o revit-agent.msi
if ($LASTEXITCODE -ne 0) { throw "wix build failed (exit $LASTEXITCODE)" }

$msi = Get-Item revit-agent.msi
$hash = (Get-FileHash -Algorithm SHA256 $msi.FullName).Hash.ToLowerInvariant()
$checksumFile = $msi.FullName + ".sha256"
Set-Content -Path $checksumFile -Value ($hash + "  " + $msi.Name) -Encoding ascii
Write-Host "Build complete"
Write-Host "SHA256: $hash"
Write-Host "Checksum saved to: $checksumFile"
