#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

project_path="Tests/Atom.Net.Browsing.WebDriver.Tests/Atom.Net.Browsing.WebDriver.Tests.csproj"
display_inventory_script="$repo_root/.vscode/scripts/inspect-managed-displays.sh"
filter_expression=""
use_firefox_dev=0
require_real_browser_env=0
log_dir_name="mtp-webdriver"
artifact_prefix="webdriver-mtp"
run_description="full suite"

usage() {
    cat <<'EOF'
Usage:
  run-webdriver-mtp-repro.sh
  run-webdriver-mtp-repro.sh --filter 'FullyQualifiedName~WebDriverRealBrowserIntegrationTests'
  run-webdriver-mtp-repro.sh --filter 'FullyQualifiedName~WebDriverRealBrowserIntegrationTests' --firefox-dev

Options:
  --filter <expr>                Apply dotnet test filter to discovery and execution.
  --firefox-dev                  Resolve Firefox Developer Edition/Nightly env automatically.
  --require-real-browser-env     Fail fast when ATOM_TEST_WEBDRIVER_BROWSER is missing.
  -h, --help                     Show this help.
EOF
}

is_real_browser_filter() {
    local filter_value="${1:-}"
    [[ "$filter_value" == *"RealBrowser"* || "$filter_value" == *"WebDriverRealBrowserIntegrationTests"* ]]
}

resolve_firefox_dev_environment() {
    local browser="${ATOM_TEST_WEBDRIVER_BROWSER_PATH:-${ATOM_TEST_WEBDRIVER_FIREFOX_DEV_BINARY:-}}"
    local browser_name=""
    local candidate=""

    if [[ -z "$browser" ]]; then
        for candidate in /usr/bin/firefox-developer-edition /usr/bin/firefox-nightly; do
            if [[ -x "$candidate" ]]; then
                browser="$candidate"
                break
            fi
        done
    fi

    if [[ -z "$browser" || ! -x "$browser" ]]; then
        echo "[mtp] Firefox Developer Edition or Nightly not found. Set ATOM_TEST_WEBDRIVER_BROWSER_PATH to a dev-capable binary." >&2
        exit 1
    fi

    case "$browser" in
        *nightly*|*Nightly*)
            browser_name="firefox-nightly"
            ;;
        *)
            browser_name="firefox-developer-edition"
            ;;
    esac

    export ATOM_TEST_WEBDRIVER_BROWSER="$browser_name"
    export ATOM_TEST_WEBDRIVER_BROWSER_PATH="$browser"
    export ATOM_TEST_WEBDRIVER_HEADLESS="${ATOM_TEST_WEBDRIVER_HEADLESS:-true}"
}

while (( $# > 0 )); do
    case "$1" in
        --filter)
            shift
            if (( $# == 0 )); then
                echo "Missing value for --filter" >&2
                usage >&2
                exit 1
            fi
            filter_expression="$1"
            ;;
        --firefox-dev)
            use_firefox_dev=1
            ;;
        --require-real-browser-env)
            require_real_browser_env=1
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            usage >&2
            exit 1
            ;;
    esac

    shift
done

if [[ -n "$filter_expression" ]]; then
    run_description="filtered run"
fi

if is_real_browser_filter "$filter_expression"; then
    require_real_browser_env=1
    log_dir_name="mtp-webdriver-class"
    artifact_prefix="webdriver-realbrowser-class"
    run_description="filtered real-browser run"
fi

if (( use_firefox_dev == 1 )); then
    resolve_firefox_dev_environment
fi

if (( require_real_browser_env == 1 )) && [[ -z "${ATOM_TEST_WEBDRIVER_BROWSER:-}" ]]; then
    echo "[mtp] Real-browser repro requires ATOM_TEST_WEBDRIVER_BROWSER. Re-run with --firefox-dev or export ATOM_TEST_WEBDRIVER_BROWSER* first." >&2
    exit 1
fi

if [[ -n "${ATOM_TEST_WEBDRIVER_BROWSER:-}" ]] && [[ -z "${ATOM_TEST_WEBDRIVER_HEADLESS:-}" ]]; then
    export ATOM_TEST_WEBDRIVER_HEADLESS=true
fi

timestamp="$(date +%Y%m%d-%H%M%S)"
log_dir="$repo_root/.tmp/$log_dir_name"
run_dir="$log_dir/run-$timestamp"
discovery_log="$run_dir/discovery.log"
run_log="$run_dir/dotnet-test.log"
pre_inventory_log="$run_dir/display-inventory.before.log"
post_inventory_log="$run_dir/display-inventory.after.log"
staging_root="${TMPDIR:-/tmp}/$log_dir_name-$timestamp"
results_dir="$staging_root/results"
diagnostic_dir="$staging_root/diagnostic"
trx_filename="$artifact_prefix-$timestamp.trx"
diagnostic_prefix="$artifact_prefix-$timestamp"
staged_trx_path="$results_dir/$trx_filename"
trx_path="$run_dir/$trx_filename"
trx_summary_path="$run_dir/trx-summary.env"

mkdir -p "$run_dir" "$results_dir" "$diagnostic_dir"
cd "$repo_root"

discovery_args=(test --project "$project_path" --list-tests)
run_args=(
    test
    --project "$project_path"
    --no-progress
    --output Detailed
    --results-directory "$results_dir"
    --report-trx
    --report-trx-filename "$trx_filename"
    --diagnostic
    --diagnostic-verbosity trace
    --diagnostic-output-directory "$diagnostic_dir"
    --diagnostic-file-prefix "$diagnostic_prefix"
)

if [[ -n "$filter_expression" ]]; then
    discovery_args+=(--filter "$filter_expression")
    run_args+=(--filter "$filter_expression")
fi

if [[ -f "$display_inventory_script" ]]; then
    echo "[mtp] managed display inventory before run: ${pre_inventory_log#$repo_root/}"
    bash "$display_inventory_script" > "$pre_inventory_log" 2>&1 || true
fi

echo "[mtp] run mode: $run_description"
if [[ -n "$filter_expression" ]]; then
    echo "[mtp] filter: $filter_expression"
fi

if [[ -n "${ATOM_TEST_WEBDRIVER_BROWSER:-}" ]]; then
    echo "[mtp] real browser: ${ATOM_TEST_WEBDRIVER_BROWSER}"
fi

if [[ -n "${ATOM_TEST_WEBDRIVER_BROWSER_PATH:-}" ]]; then
    echo "[mtp] browser path: ${ATOM_TEST_WEBDRIVER_BROWSER_PATH}"
fi

if [[ -n "${ATOM_TEST_WEBDRIVER_HEADLESS:-}" ]]; then
    echo "[mtp] headless: ${ATOM_TEST_WEBDRIVER_HEADLESS}"
fi

echo "[mtp] discovering tests: $project_path"
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet "${discovery_args[@]}" > "$discovery_log" 2>&1

discovered_total="$(sed -n 's/.*(\([0-9][0-9]*\)).*/\1/p' "$discovery_log" | head -n 1)"
if [[ -z "$discovered_total" ]]; then
    discovered_total="unknown"
fi

echo "[mtp] discovered tests: $discovered_total"
echo "[mtp] discovery log: ${discovery_log#$repo_root/}"
echo "[mtp] artifacts directory: ${run_dir#$repo_root/}"
echo "[mtp] staged results directory: $results_dir"
echo "[mtp] staged diagnostic directory: $diagnostic_dir"
echo "[mtp] running dotnet test with TRX and MTP diagnostics enabled"

set +e
env DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet "${run_args[@]}" > "$run_log" 2>&1
status=$?
set -e

shopt -s nullglob
diagnostic_files=("$diagnostic_dir"/*.diag)
shopt -u nullglob

if (( ${#diagnostic_files[@]} > 0 )); then
    cp "${diagnostic_files[@]}" "$run_dir/"
fi

if [[ -f "$staged_trx_path" ]]; then
    cp "$staged_trx_path" "$trx_path"
fi

if [[ -f "$display_inventory_script" ]]; then
    echo "[mtp] managed display inventory after run: ${post_inventory_log#$repo_root/}"
    bash "$display_inventory_script" > "$post_inventory_log" 2>&1 || true
fi

echo "[mtp] dotnet exit code: $status"
echo "[mtp] run log: ${run_log#$repo_root/}"

if (( ${#diagnostic_files[@]} > 0 )); then
    echo "[mtp] diagnostic logs:"
    for diagnostic_file in "${diagnostic_files[@]}"; do
        copied_diagnostic_path="$run_dir/$(basename "$diagnostic_file")"
        echo "[mtp]   ${copied_diagnostic_path#$repo_root/}"
    done
else
    echo "[mtp] diagnostic log was not produced"
fi

if [[ -f "$trx_path" ]]; then
    echo "[mtp] trx report: ${trx_path#$repo_root/}"
    python3 - "$trx_path" "$discovered_total" "$trx_summary_path" <<'PY'
from pathlib import Path
from xml.etree import ElementTree as ET
import sys

trx_path = Path(sys.argv[1])
discovered_total = sys.argv[2]
summary_path = Path(sys.argv[3])
root = ET.parse(trx_path).getroot()
counters = next((element for element in root.iter() if element.tag.endswith('Counters')), None)
namespace = {'trx': root.tag.split('}')[0].strip('{')} if root.tag.startswith('{') else {}

def parse_count(value: str) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return -1

if counters is None:
    print('[mtp] trx counters not found')
    summary_path.write_text('MTP_TRX_ALL_SKIPPED=0\n', encoding='utf-8')
    sys.exit(0)

total = counters.attrib.get('total', 'unknown')
passed = counters.attrib.get('passed', 'unknown')
failed = counters.attrib.get('failed', 'unknown')
not_executed = counters.attrib.get('notExecuted', counters.attrib.get('notexecuted', 'unknown'))
total_count = parse_count(total)
passed_count = parse_count(passed)
failed_count = parse_count(failed)
not_executed_count = parse_count(not_executed)
all_skipped = total_count > 0 and passed_count == 0 and failed_count == 0 and not_executed_count == total_count

summary_path.write_text(
    '\n'.join([
        f'MTP_TRX_TOTAL={total}',
        f'MTP_TRX_PASSED={passed}',
        f'MTP_TRX_FAILED={failed}',
        f'MTP_TRX_NOT_EXECUTED={not_executed}',
        f"MTP_TRX_ALL_SKIPPED={'1' if all_skipped else '0'}",
    ]) + '\n',
    encoding='utf-8')

print(f'[mtp] trx counters: total={total}, passed={passed}, failed={failed}, notExecuted={not_executed}, discovered={discovered_total}')

result_path = './/trx:UnitTestResult' if namespace else './/UnitTestResult'
message_path = 'trx:Output/trx:ErrorInfo/trx:Message' if namespace else 'Output/ErrorInfo/Message'
failed_results = []

for unit_test_result in root.findall(result_path, namespace):
    if unit_test_result.attrib.get('outcome') != 'Failed':
        continue

    message = unit_test_result.find(message_path, namespace)
    first_line = ''
    if message is not None and message.text:
        first_line = message.text.strip().splitlines()[0]

    failed_results.append((unit_test_result.attrib.get('testName', 'unknown'), first_line))

if failed_results:
    print('[mtp] failed tests:')
    for test_name, first_line in failed_results:
        print(f'[mtp]   {test_name}')
        if first_line:
            print(f'[mtp]     {first_line}')
PY
else
    echo "[mtp] trx report was not produced at $staged_trx_path"
fi

if [[ -f "$trx_summary_path" ]]; then
    # shellcheck disable=SC1090
    source "$trx_summary_path"
fi

if [[ "$status" -eq 8 && "${MTP_TRX_ALL_SKIPPED:-0}" -eq 1 ]]; then
    echo "[mtp] dotnet returned exit code 8 for an all-skipped run; normalizing to success because TRX reports zero failed tests and all selected tests were not executed"
    status=0
fi

if [[ "$status" -eq 0 && ! -f "$trx_path" ]]; then
    echo "[mtp] dotnet test returned 0 without producing TRX; treating the run as incomplete"
    if (( ${#diagnostic_files[@]} > 0 )); then
        echo "[mtp] diagnostic hints:"
        rg -n -m 8 'FailedTestNodeStateProperty|VirtualDisplayException|Fatal server error|Server is already active for display|Xvfb command has terminated|vfb failed to start' "$run_dir"/*.diag | sed 's/^/[mtp]   /' || true
    fi
    status=1
fi

if [[ "$status" -ne 0 ]]; then
    echo "[mtp] tail of run log:"
    tail -n 40 "$run_log" | sed 's/^/[mtp]   /' || true
fi

exit "$status"