#!/usr/bin/env bash

# Наполняет локальный NuGet-фид .tmp/local-nuget-feed пакетами Escorp.Atom.*,
# необходимыми Release-конфигурации Atom.Net.Browsing.WebDriver.
#
# Зачем: в Release вебдрайвер и вся цепочка ссылаются на пакеты плавающей
# версией "*". Часть из них (на момент написания — Escorp.Atom.Media.Audio)
# не опубликована на nuget.org вообще, поэтому restore на свежем клоне падает
# с NU1101. Локальный фид уже подключён в NuGet.config: после прогона этого
# скрипта `dotnet build/pack -c Release` резолвит актуальные версии прямо из
# исходников репозитория.
#
# Порядок — снизу вверх по графу зависимостей, чтобы каждый последующий пакет
# при собственной Release-сборке видел уже упакованные зависимости из фида.
#
# Этот скрипт НЕ заменяет публикацию: push на nuget.org выполняется через
# publish-framework-package.sh (в том же порядке, вебдрайвер — последним).

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
feed_dir="$repo_root/.tmp/local-nuget-feed"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "[local-feed] dotnet не найден в PATH. Установите .NET SDK 10 (см. global.json)." >&2
    exit 1
fi

if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
    echo "[local-feed] ВНИМАНИЕ: Node.js/npm не найдены в PATH. Сама наполняемая цепочка их не требует," >&2
    echo "[local-feed] но сборка Atom.Net.Browsing.WebDriver (ExtensionRuntime) без них не пройдёт." >&2
fi

dependency_projects=(
    "Framework/Atom/Atom.csproj"
    "Framework/Atom.Debug/Atom.Debug.csproj"
    "Framework/Atom.IO.Compression/Atom.IO.Compression.csproj"
    "Framework/Atom.Media/Atom.Media.csproj"
    "Framework/Atom.Net/Atom.Net.csproj"
    "Framework/Atom.Media.Video/Atom.Media.Video.csproj"
    "Framework/Atom.Media.Audio/Atom.Media.Audio.csproj"
)

# Задача скрипта — стабильная заготовка зависимостей, а не аналитика: правила
# дрейфуют независимо от кода и не должны блокировать локальную упаковку.
# Полноценная проверка анализаторами остаётся в publish-framework-package.sh.
analyzer_flags=(
    "-p:RunAnalyzers=false"
    "-p:EnforceCodeStyleInBuild=false"
    "-p:EnableNETAnalyzers=false"
    "-p:AnalysisMode=None"
    "-p:TreatWarningsAsErrors=false"
    "-p:CodeAnalysisTreatWarningsAsErrors=false"
)

cd "$repo_root"
mkdir -p "$feed_dir"

for relative_project in "${dependency_projects[@]}"; do
    project_path="$repo_root/$relative_project"

    if [[ ! -f "$project_path" ]]; then
        echo "[local-feed] проект не найден: $relative_project" >&2
        exit 1
    fi

    echo "[local-feed] build Release: $relative_project"
    dotnet build "$project_path" \
        -c Release \
        --nologo \
        -p:GeneratePackageOnBuild=false \
        "${analyzer_flags[@]}"

    echo "[local-feed] pack Release: $relative_project"
    dotnet pack "$project_path" \
        -c Release \
        --no-build \
        --nologo \
        --output "$feed_dir" \
        -p:GeneratePackageOnBuild=false \
        "${analyzer_flags[@]}"
done

echo "[local-feed] готово: $feed_dir"
ls -1 "$feed_dir"/*.nupkg 2>/dev/null || true
echo "[local-feed] теперь: dotnet pack Framework/Atom.Net.Browsing.WebDriver/Atom.Net.Browsing.WebDriver.csproj -c Release"
