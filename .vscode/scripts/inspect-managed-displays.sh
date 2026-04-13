#!/usr/bin/env bash

set -euo pipefail

runtime_dir="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
xpra_dir="$runtime_dir/xpra"
host_name="$(hostname)"

clean_mode=0
clean_all=0
declare -a target_displays=()

usage() {
    cat <<'EOF'
Usage:
  inspect-managed-displays.sh
  inspect-managed-displays.sh --clean --display 102 --display 103
  inspect-managed-displays.sh --clean --all

Modes:
  default            Print inventory for managed display processes and artifacts.
  --clean            Kill selected managed display processes and clean artifacts.
  --all              With --clean, target all discovered managed displays.
  --display N        With --clean, target a specific display number. Repeatable.
EOF
}

contains_value() {
    local needle="$1"
    shift || true

    local value
    for value in "$@"; do
        if [[ "$value" == "$needle" ]]; then
            return 0
        fi
    done

    return 1
}

normalize_cmdline() {
    tr '\0' ' ' < "$1" 2>/dev/null | sed 's/[[:space:]]\+/ /g; s/^ //; s/ $//'
}

detect_managed_display() {
    local cmdline="$1"
    local display_number=""
    local process_kind=""

    if [[ "$cmdline" =~ Xvfb-for-Xpra-([0-9]{2,3}) ]]; then
        display_number="${BASH_REMATCH[1]}"
        process_kind="xpra-xvfb"
    elif [[ "$cmdline" =~ (^|[[:space:]])xpra[[:space:]].*(start|attach)[[:space:]]:([0-9]{2,3}) ]]; then
        display_number="${BASH_REMATCH[3]}"
        process_kind="xpra"
    elif [[ "$cmdline" =~ (^|[[:space:]])Xvfb[[:space:]]:([0-9]{2,3})([[:space:]]|$) ]] \
        && [[ "$cmdline" == *"+extension XTEST"* ]] \
        && [[ "$cmdline" == *"-nolisten tcp"* ]] \
        && [[ "$cmdline" == *"-screen 0"* ]]; then
        display_number="${BASH_REMATCH[2]}"
        process_kind="xvfb"
    fi

    if [[ -z "$display_number" ]]; then
        return 1
    fi

    if (( display_number < 99 || display_number > 255 )); then
        return 1
    fi

    printf '%s|%s\n' "$display_number" "$process_kind"
}

collect_entries() {
    local proc_dir
    while IFS= read -r -d '' proc_dir; do
        local pid="${proc_dir##*/}"
        local cmdline_file="$proc_dir/cmdline"

        [[ -r "$cmdline_file" ]] || continue

        local cmdline
        cmdline="$(normalize_cmdline "$cmdline_file")"
        [[ -n "$cmdline" ]] || continue

        local detection
        detection="$(detect_managed_display "$cmdline" || true)"
        [[ -n "$detection" ]] || continue

        local display_number="${detection%%|*}"
        local process_kind="${detection##*|}"
        local ppid="$(awk '/^PPid:/ { print $2 }' "$proc_dir/status" 2>/dev/null || true)"
        local elapsed="$(ps -o etime= -p "$pid" 2>/dev/null | xargs || true)"
        local started="$(ps -o lstart= -p "$pid" 2>/dev/null | xargs || true)"

        printf '%s|%s|%s|%s|%s|%s|%s\n' \
            "$display_number" \
            "$pid" \
            "$ppid" \
            "$process_kind" \
            "$elapsed" \
            "$started" \
            "$cmdline"
    done < <(find /proc -maxdepth 1 -mindepth 1 -type d -name '[0-9]*' -print0 2>/dev/null | sort -z)
}

display_artifact_summary() {
    local display_number="$1"
    local lock_file="/tmp/.X${display_number}-lock"
    local socket_file="/tmp/.X11-unix/X${display_number}"
    local direct_socket="$xpra_dir/$display_number/socket"
    local host_socket="$xpra_dir/$host_name-$display_number"
    local display_dir="$xpra_dir/$display_number"
    local entries=""

    if [[ -d "$display_dir" ]]; then
        entries="$(find "$display_dir" -mindepth 1 -maxdepth 1 -printf '%f\n' 2>/dev/null | sort | paste -sd',' -)"
    fi

    printf 'lock=%s socket=%s direct=%s host=%s dir=%s entries=%s' \
        "$([[ -e "$lock_file" ]] && echo yes || echo no)" \
        "$([[ -e "$socket_file" ]] && echo yes || echo no)" \
        "$([[ -e "$direct_socket" ]] && echo yes || echo no)" \
        "$([[ -e "$host_socket" ]] && echo yes || echo no)" \
        "$([[ -d "$display_dir" ]] && echo yes || echo no)" \
        "${entries:-<none>}"
}

print_inventory() {
    local -a entries=("$@")

    if (( ${#entries[@]} == 0 )); then
        echo '[managed-display] no managed display processes found'
        return
    fi

    echo '[managed-display] inventory:'

    local -a displays=()
    local entry
    for entry in "${entries[@]}"; do
        local display_number="${entry%%|*}"
        if ! contains_value "$display_number" "${displays[@]}"; then
            displays+=("$display_number")
        fi
    done

    IFS=$'\n' displays=($(printf '%s\n' "${displays[@]}" | sort -n))
    unset IFS

    local display_number
    for display_number in "${displays[@]}"; do
        echo
        echo "[managed-display] :$display_number"
        echo "[managed-display]   artifacts $(display_artifact_summary "$display_number")"

        for entry in "${entries[@]}"; do
            [[ "$entry" == "$display_number|"* ]] || continue
            IFS='|' read -r _ pid ppid process_kind elapsed started cmdline <<< "$entry"
            unset IFS
            echo "[managed-display]   pid=$pid ppid=$ppid kind=$process_kind elapsed=${elapsed:-<unknown>} started=${started:-<unknown>}"
            echo "[managed-display]   cmd=$cmdline"
        done
    done
}

kill_pid_if_alive() {
    local pid="$1"

    [[ -d "/proc/$pid" ]] || return 0

    kill -TERM "$pid" 2>/dev/null || true

    local attempt
    for attempt in {1..10}; do
        [[ -d "/proc/$pid" ]] || return 0
        sleep 0.1
    done

    kill -KILL "$pid" 2>/dev/null || true

    for attempt in {1..10}; do
        [[ -d "/proc/$pid" ]] || return 0
        sleep 0.1
    done

    return 1
}

cleanup_display_artifacts() {
    local display_number="$1"
    rm -f "/tmp/.X${display_number}-lock" "/tmp/.X11-unix/X${display_number}" || true
    rm -f "$xpra_dir/$display_number/socket" "$xpra_dir/$host_name-$display_number" || true
    rm -rf "$xpra_dir/$display_number" || true
}

cleanup_displays() {
    local -a initial_entries=("$@")
    local display_number
    local failed=0

    for display_number in "${target_displays[@]}"; do
        echo "[managed-display] cleaning :$display_number"

        local matched=0
        local entry
        for entry in "${initial_entries[@]}"; do
            [[ "$entry" == "$display_number|"* ]] || continue
            matched=1
            IFS='|' read -r _ pid _ _ _ _ _ <<< "$entry"
            unset IFS
            if ! kill_pid_if_alive "$pid"; then
                echo "[managed-display]   pid=$pid did not exit cleanly"
                failed=1
            fi
        done

        if (( matched == 0 )); then
            echo "[managed-display]   no managed processes matched :$display_number in current snapshot"
        fi

        local -a remaining_entries=()
        local remaining_entry
        while IFS= read -r remaining_entry; do
            [[ -n "$remaining_entry" ]] || continue
            if [[ "$remaining_entry" == "$display_number|"* ]]; then
                remaining_entries+=("$remaining_entry")
            fi
        done < <(collect_entries)

        if (( ${#remaining_entries[@]} == 0 )); then
            cleanup_display_artifacts "$display_number"
            echo "[managed-display]   cleaned artifacts for :$display_number"
        else
            echo "[managed-display]   managed processes still alive for :$display_number; artifacts left intact"
            failed=1
        fi
    done

    return "$failed"
}

while (( $# > 0 )); do
    case "$1" in
        --clean)
            clean_mode=1
            ;;
        --all)
            clean_all=1
            ;;
        --display)
            shift
            if (( $# == 0 )); then
                echo 'Missing value for --display' >&2
                usage >&2
                exit 1
            fi
            target_displays+=("$1")
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

mapfile -t entries < <(collect_entries)
print_inventory "${entries[@]}"

if (( clean_mode == 0 )); then
    exit 0
fi

if (( clean_all == 1 )); then
    target_displays=()
    for entry in "${entries[@]}"; do
        display_number="${entry%%|*}"
        if ! contains_value "$display_number" "${target_displays[@]}"; then
            target_displays+=("$display_number")
        fi
    done
fi

if (( ${#target_displays[@]} == 0 )); then
    if (( clean_all == 1 )); then
        echo '[managed-display] cleanup requested for all displays, but inventory is already empty'
        exit 0
    fi

    echo 'Cleanup requested but no displays were selected. Use --all or one or more --display N flags.' >&2
    exit 1
fi

IFS=$'\n' target_displays=($(printf '%s\n' "${target_displays[@]}" | sort -n | uniq))
unset IFS

echo
echo "[managed-display] cleanup targets: ${target_displays[*]}"
cleanup_displays "${entries[@]}"

echo
echo '[managed-display] inventory after cleanup:'
mapfile -t final_entries < <(collect_entries)
print_inventory "${final_entries[@]}"