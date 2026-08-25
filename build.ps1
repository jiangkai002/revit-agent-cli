$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "==> Build CLI (also builds & stages Revit 2019-2022 executors)" -ForegroundColor Cyan
dotnet build RevitAgent.Cli\RevitAgent.Cli.csproj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> Pack global tool" -ForegroundColor Cyan
dotnet pack RevitAgent.Cli\RevitAgent.Cli.csproj -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Done. nupkg is in RevitAgent.Cli\bin\Release\" -ForegroundColor Green
