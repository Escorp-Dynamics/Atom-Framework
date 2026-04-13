#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "Usage: $0 <project-path>"
    exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
project_path="$1"

if [[ ! -f "$project_path" ]]; then
    project_path="$repo_root/$project_path"
fi

if [[ ! -f "$project_path" ]]; then
    echo "Project file not found: $1"
    exit 1
fi

cd "$repo_root"

project_name="$(basename "${project_path%.csproj}")"
package_output_dir="$repo_root/.tmp/publish-packages/$project_name"
nuget_source="${NUGET_PUSH_SOURCE:-nuget.org}"
nuget_cli_source="${NUGET_PUSH_NUGET_SOURCE:-$nuget_source}"
nuget_config_file="${NUGET_PUSH_CONFIGFILE:-${HOME}/.config/NuGet/NuGet.Config}"
push_tool="dotnet"

if [[ "$nuget_cli_source" == "nuget.org" ]]; then
    nuget_cli_source="https://www.nuget.org/api/v2/package"
fi

if [[ -n "${NUGET_API_KEY:-}" ]]; then
    push_args=(
        --source "$nuget_source"
        --skip-duplicate
        --api-key "$NUGET_API_KEY"
    )
elif command -v nuget >/dev/null 2>&1 && [[ -f "$nuget_config_file" ]]; then
    push_tool="nuget"
    push_args=(
        -Source "$nuget_cli_source"
        -SkipDuplicate
        -NonInteractive
        -ConfigFile "$nuget_config_file"
    )
    echo "[publish] NUGET_API_KEY is not set; using nuget push with saved credentials from $nuget_config_file"
else
    push_args=(
        --source "$nuget_source"
        --skip-duplicate
    )
    echo "[publish] NUGET_API_KEY is not set; relying on dotnet nuget push to resolve configured credentials or saved API key for $nuget_source"
fi

push_package() {
    local package_file="$1"

    if [[ "$push_tool" == "nuget" ]]; then
        nuget push "$package_file" "${push_args[@]}"
        return
    fi

    dotnet nuget push "$package_file" "${push_args[@]}"
}

rm -rf "$package_output_dir"
mkdir -p "$package_output_dir"

echo "[publish] build Release: $project_path"
dotnet build "$project_path" \
    -c Release \
    --nologo \
    -p:GeneratePackageOnBuild=false

echo "[publish] pack Release: $project_path"
dotnet pack "$project_path" \
    -c Release \
    --no-build \
    --nologo \
    --output "$package_output_dir" \
    -p:GeneratePackageOnBuild=false

shopt -s nullglob

nupkgs=("$package_output_dir"/*.nupkg)
snupkgs=("$package_output_dir"/*.snupkg)

if [[ ${#nupkgs[@]} -eq 0 ]]; then
    echo "No .nupkg files were produced in $package_output_dir"
    exit 1
fi

echo "[publish] push packages to $nuget_source"
for package_file in "${nupkgs[@]}"; do
    push_package "$package_file"
done

for symbols_file in "${snupkgs[@]}"; do
    push_package "$symbols_file"
done

echo "[publish] completed: $project_name"