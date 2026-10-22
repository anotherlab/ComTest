#!/usr/bin/env bash
set -euo pipefail

task_root="$(cd "$(dirname "$0")/.." && pwd)"
task_runtime="${1:-osx-arm64}"
case "$task_runtime" in
  osx-arm64|osx-x64) ;;
  *) echo "Usage: bash scripts/publish-macos.sh [osx-arm64|osx-x64]" >&2; exit 1 ;;
esac
task_bundle="$task_root/artifacts/$task_runtime/ComTest.app/Contents"
mkdir -p "$task_bundle/MacOS"
cp "$task_root/packaging/Info.plist" "$task_bundle/Info.plist"
dotnet publish "$task_root/src/ComTest/ComTest.csproj" -c Release -r "$task_runtime" \
  --self-contained true -o "$task_bundle/MacOS"
chmod +x "$task_bundle/MacOS/ComTest"
echo "Published $task_root/artifacts/$task_runtime/ComTest.app"
