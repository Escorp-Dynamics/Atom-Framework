# ExtensionRuntime

Этот каталог держит браузерный runtime для bridge transport и не меняет внешний контракт WebBrowser, WebWindow и WebPage.

Ключевые правила:

- Shared хранит единые protocol и config contracts для всех браузеров
- Background владеет discovery, transport, session lifecycle, routing и tab state
- Content владеет tab-local port channel, DOM-командами и lifecycle-событиями страницы
- Page остаётся слоем второй фазы для main-world hooks и callback proxy
- Platform и Packaging изолируют различия Chromium, Firefox, MV2, MV3 и permissions
- Внутренние ApplyContext и другие background-content envelope не должны протекать в host bridge contract

Build output layout и working directory policy зафиксированы в BUILD_OUTPUT_LAYOUT.md
