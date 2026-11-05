#!/usr/bin/env bash
# Runs the Core tests without NuGet (shim runner). Optional arg: name filter.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build tests/LocalRunner -c Debug -nologo -v q -clp:ErrorsOnly
dotnet tests/LocalRunner/bin/Debug/net10.0/LocalRunner.dll "$@"
