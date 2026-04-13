# Background

Background слой соединяет host bridge transport с tab-local content каналами.

Reference runtime уже показывает обязательные опорные шаги:

- config handoff стартует из config.json или discovery meta и переводит runtime в configure path
- socket state, runtime Port state и tab context state живут раздельно
- очередь команд до готовности runtime Port считается допустимой семантикой
- ApplyContext остаётся внутренней background-content командой и не становится BridgeCommand
- executeInMain и mainWorldResult остаются отдельным внутренним port path и не смешиваются с BridgeMessage

Следующая реализация должна сохранить эти правила и уложить их в инвариантные Session, Transport, Tabs и Routing contracts.
