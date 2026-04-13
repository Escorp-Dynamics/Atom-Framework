# Build Output Layout

Этот документ фиксирует, куда extension runtime должен складывать рабочие артефакты во время обычной сборки проекта.

## Источник истины

- исходники runtime живут в ExtensionRuntime
- browser-specific manifest templates живут в ExtensionRuntime/Packaging/Chrome и ExtensionRuntime/Packaging/Firefox
- проверки TypeScript живут в локальном package.json и tsconfig.json внутри ExtensionRuntime

## Рабочие каталоги сборки

Обычный dotnet build проекта Atom.Net.Browsing.WebDriver должен готовить два рабочих каталога в промежуточном build-каталоге `obj/.../extension-working-layout`:

- Extension — рабочий каталог Chrome MV3
- Extension.Firefox — рабочий каталог Firefox MV2

Эти каталоги считаются build-owned working directories, не коммитятся в репозиторий и не требуют отдельного ручного запуска перед сборкой проекта.

## Что делает сборка сейчас

- при необходимости устанавливает локальную typescript dependency в ExtensionRuntime
- запускает npm --prefix ExtensionRuntime run typecheck
- собирает background runtime в промежуточный generated-каталог под obj
- синхронизирует manifest.json из Packaging/Chrome в промежуточный Extension
- синхронизирует manifest.json из Packaging/Firefox в промежуточный Extension.Firefox
- временно синхронизирует reference content.js и icons в оба промежуточных working directory как baseline runtime layout, а background runtime публикует только через generated/background.runtime.js
- копирует эти working directories в bin как Extension и Extension.Firefox

## Что должно появиться следующим шагом

- content.js из нового ExtensionRuntime build pipeline должен заменить временный reference baseline и собираться сразу в Extension и Extension.Firefox; background.js как отдельный legacy payload больше не нужен
- config.json handoff должен материализоваться в этих же working directories
- packaging и project output должны читать промежуточные build-owned working directories и публиковать их в bin как Extension и Extension.Firefox, а не хранить эти артефакты в исходниках
