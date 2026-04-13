#!/usr/bin/env bash

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

forbidden_patterns=(
  "Framework/Atom.Net.Browsing.WebDriver/Extension/**"
  "Framework/Atom.Net.Browsing.WebDriver/Extension.Firefox/**"
  "Framework/Atom.Net.Browsing.WebDriver/ExtensionRuntime/generated/**"
  "Framework/Atom.Net.Browsing.WebDriver/extension-working-layout/**"
  "Framework/Atom.Net.Browsing.WebDriver/extension-runtime/generated/**"
  "Reference/WebDriver.Reference/**"
)

forbidden_roots=(
  "Framework/Atom.Net.Browsing.WebDriver/Extension"
  "Framework/Atom.Net.Browsing.WebDriver/Extension.Firefox"
  "Framework/Atom.Net.Browsing.WebDriver/ExtensionRuntime/generated"
  "Framework/Atom.Net.Browsing.WebDriver/extension-working-layout"
  "Framework/Atom.Net.Browsing.WebDriver/extension-runtime/generated"
  "Reference/WebDriver.Reference"
)

matches_file="$(mktemp)"
tracked_matches_file="$(mktemp)"
local_matches_file="$(mktemp)"
trap 'rm -f "$matches_file" "$tracked_matches_file" "$local_matches_file"' EXIT

for pattern in "${forbidden_patterns[@]}"; do
  while IFS= read -r tracked_file; do
    if [[ -n "$tracked_file" && -e "$tracked_file" ]]; then
      printf '%s\n' "$tracked_file" >> "$tracked_matches_file"
      printf '%s\n' "$tracked_file" >> "$matches_file"
    fi
  done < <(git ls-files -- "$pattern")
done

for root in "${forbidden_roots[@]}"; do
  if [[ -d "$root" ]]; then
    while IFS= read -r existing_file; do
      if [[ -n "$existing_file" ]]; then
        if [[ -s "$tracked_matches_file" ]] && grep -Fqx -- "$existing_file" "$tracked_matches_file"; then
          continue
        fi

        printf '%s\n' "$existing_file" >> "$local_matches_file"
        printf '%s\n' "$existing_file" >> "$matches_file"
      fi
    done < <(find "$root" -type f)
  fi
done

if [[ -s "$matches_file" ]]; then
  echo "WebDriver build artifacts and legacy reference snapshots are not allowed in source paths"

  if [[ -s "$tracked_matches_file" ]]; then
    echo "Tracked files:"
    sort -u "$tracked_matches_file"
    echo
  fi

  if [[ -s "$local_matches_file" ]]; then
    echo "Local existing files:"
    sort -u "$local_matches_file"
    echo
  fi

  exit 1
fi

echo "No WebDriver build artifacts or legacy reference snapshots found in guarded source paths"