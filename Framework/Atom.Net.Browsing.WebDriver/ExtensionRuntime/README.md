# ExtensionRuntime

Этот каталог держит браузерный runtime для bridge transport и не меняет внешний контракт WebBrowser, WebWindow и WebPage.

**Важно для сборки (Release / pack / publish):**

- Требуется **Node.js 22+** и **npm** в PATH.
- При первой сборке (или после `git clean`) выполняется `npm ci`.
- Typecheck и сборка скриптов (`tsc`, `esbuild`, `node` скрипты) вызываются автоматически из `.csproj`.
- Если увидишь ошибку `MSB3073 ... exit code 127` или `tsc: not found` / `node: not found` — установи Node.js и выполни `npm ci` вручную.

Команды для ручного восстановления:

```bash
cd Framework/Atom.Net.Browsing.WebDriver/ExtensionRuntime
npm ci
npm run typecheck
```

Ключевые правила:

- Shared хранит единые protocol и config contracts для всех браузеров
- Background владеет discovery, transport, session lifecycle, routing и tab state
- Content владеет tab-local port channel, DOM-командами и lifecycle-событиями страницы
- Page остаётся слоем второй фазы для main-world hooks и callback proxy
- Platform и Packaging изолируют различия Chromium, Firefox, MV2, MV3 и permissions
- Внутренние ApplyContext и другие background-content envelope не должны протекать в host bridge contract

Build output layout и working directory policy зафиксированы в BUILD_OUTPUT_LAYOUT.md
