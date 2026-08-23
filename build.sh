#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

echo "==> Build CLI (also builds & stages both executors 2021/2022)"
dotnet build RevitAgent.Cli/RevitAgent.Cli.csproj -c Release

echo "==> Pack global tool"
dotnet pack RevitAgent.Cli/RevitAgent.Cli.csproj -c Release --no-build

echo "Done. nupkg is in RevitAgent.Cli/bin/Release/"
