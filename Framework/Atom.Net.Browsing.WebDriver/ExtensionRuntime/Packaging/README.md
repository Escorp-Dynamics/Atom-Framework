# Packaging

Packaging слой отвечает за manifest, иконки, permissions и готовые browser-specific артефакты.

Сюда относятся:

- Chrome и Firefox manifest variants
- config.json handoff в сборочном артефакте расширения
- packaging assets и layout выходных файлов

Текущее состояние:

- стартовые manifest templates уже созданы для Chrome и Firefox
- icons parity и временная JS baseline раскладка сейчас подтягиваются из reference extension во время project build
- browser-specific permission sets пока зафиксированы как каркас и будут уточняться вместе с первой transport-реализацией

Packaging может различаться по браузерам, но не должен менять Shared и Background contracts.
