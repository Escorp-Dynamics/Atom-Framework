# Platform

Platform слой держит browser-specific различия внутри адаптеров.

Сюда должны уходить:

- различия Chromium и Firefox API
- MV2 и MV3 execution path
- Firefox containers, Chromium declarativeNetRequest и другие platform hooks

Наружу этот слой обязан отдавать те же контракты Session, Transport, Tabs и Content channel.
