#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
extension_runtime_dir="$(cd -- "$script_dir/.." && pwd)"
project_dir="$(cd -- "$extension_runtime_dir/.." && pwd)"
repo_root="$(cd -- "$project_dir/../.." && pwd)"

configuration="Debug"
target_framework="net10.0"
signing_channel="unlisted"
approval_timeout="900000"
skip_build="false"
signed_dir=""

usage() {
    cat <<'EOF'
Usage: sign-firefox-package.sh [options]

Options:
  --configuration <value>      Build configuration, default: Debug
  --target-framework <value>   Target framework, default: net10.0
  --channel <value>            AMO signing channel, default: unlisted
    --approval-timeout <ms>     Wait time for AMO approval/download, default: 900000
  --signed-dir <path>          Output directory for signed XPI files
  --skip-build                 Reuse existing build output without dotnet build
  --help                       Show this help message

Required environment variables:
  WEB_EXT_API_KEY
  WEB_EXT_API_SECRET
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --configuration)
            configuration="${2:?Missing value for --configuration}"
            shift 2
            ;;
        --target-framework)
            target_framework="${2:?Missing value for --target-framework}"
            shift 2
            ;;
        --channel)
            signing_channel="${2:?Missing value for --channel}"
            shift 2
            ;;
        --approval-timeout)
            approval_timeout="${2:?Missing value for --approval-timeout}"
            shift 2
            ;;
        --signed-dir)
            signed_dir="${2:?Missing value for --signed-dir}"
            shift 2
            ;;
        --skip-build)
            skip_build="true"
            shift
            ;;
        --help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            usage >&2
            exit 1
            ;;
    esac
done

test -n "${WEB_EXT_API_KEY:-}" || { echo "Missing WEB_EXT_API_KEY" >&2; exit 1; }
test -n "${WEB_EXT_API_SECRET:-}" || { echo "Missing WEB_EXT_API_SECRET" >&2; exit 1; }

project_path="$project_dir/Atom.Net.Browsing.WebDriver.csproj"
firefox_output_dir="$project_dir/bin/$configuration/$target_framework/Extension.Firefox"
firefox_package_dir="$project_dir/obj/$configuration/$target_framework/extension-packages/firefox"

if [[ -z "$signed_dir" ]]; then
    signed_dir="$firefox_package_dir/signed"
fi

if [[ "$skip_build" != "true" ]]; then
    pushd "$repo_root" >/dev/null
    dotnet build "$project_path" -c "$configuration" -f "$target_framework" -p:CreateFirefoxExtensionPackageOnBuild=true
    popd >/dev/null
fi

test -d "$firefox_output_dir" || { echo "Firefox build output not found: $firefox_output_dir" >&2; exit 1; }

mkdir -p "$signed_dir"

pushd "$repo_root" >/dev/null
node "$extension_runtime_dir/scripts/sign-firefox-package.mjs" \
    --source-dir "$firefox_output_dir" \
    --signed-dir "$signed_dir" \
    --channel "$signing_channel" \
    --approval-timeout "$approval_timeout" \
    --request-timeout 900000 \
    --extension-runtime-dir "$extension_runtime_dir"
popd >/dev/null

signed_xpi="$(find "$signed_dir" -type f -name '*.xpi' | sort | tail -n 1)"
test -n "$signed_xpi" || { echo "Signed Firefox XPI not found in $signed_dir" >&2; exit 1; }