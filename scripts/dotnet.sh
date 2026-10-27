#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_ROOT="$task_root/.tools/dotnet"
if [[ -f "$task_root/.tools/sdk-path" ]] && [[ -x "$(cat "$task_root/.tools/sdk-path")/dotnet" ]]; then
  DOTNET_ROOT="$(cat "$task_root/.tools/sdk-path")"
fi
export DOTNET_CLI_HOME="${CHAGOK_BUILD_CACHE:-$task_root/.tools}/cli-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export NUGET_PACKAGES="${CHAGOK_BUILD_CACHE:-$task_root/.tools}/nuget"
exec "$DOTNET_ROOT/dotnet" "$@"
