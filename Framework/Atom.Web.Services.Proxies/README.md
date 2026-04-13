# Atom Framework Proxies

## Overview

Этот пакет содержит абстракции и реализации для работы с HTTP proxy, их агрегированием внутри фабрики и валидацией.

Основные слои:

- ProxyFactory: агрегирует несколько IProxyProvider в один container-level пул и полностью владеет polling, snapshot lifetime и rotation.
- ProxyProvider: базовый абстрактный provider, который только получает и нормализует данные из внешнего источника.
- NetworkProxyProvider: тонкий specialization слой для провайдеров, которые грузят данные из внешнего network source.
- concrete providers в каталоге Services: GeoNode, ProxyScrape, ProxyNova и другие.

## Provider Stack

Типичный поток выглядит так:

1. concrete provider загружает внешний feed и нормализует его в ServiceProxy.
2. ProxyProvider возвращает fetch-only snapshot и опционально валидирует прокси.
3. ProxyFactory агрегирует несколько provider-ов, хранит последний успешный aggregate snapshot и отдает объединенный пул.

Для feed-ов, которые приходят страницами или continuation-чанками, доступен дополнительный контракт `IProxyPagedProvider`.

- provider по-прежнему не хранит pool state и не занимается polling
- provider просто умеет вернуть одну страницу через `FetchPageAsync(cursor)`
- базовый `ProxyProvider.FetchAsync()` сам дочитывает страницы до полного snapshot
- фабрика остаётся независимой от source-specific параметров вроде `page`, `cursor`, `nextToken` или `Retry-After`

Для provider-ов, у которых upstream уже умеет принимать полезные selection hints, доступен ещё один опциональный контракт `IProxyTargetedProvider`.

- этот контракт нужен только для cold-start optimization, а не как замена обычному snapshot fetch
- фабрика использует targeted path только когда все cold provider-ы его поддерживают; при mixed stack она откатывается к обычному warmup
- targeted fetch может вернуть более узкий batch по count, protocol, country или anonymity, но финальные factory filters и dedup всё равно остаются на стороне ProxyFactory
- реализовывать targeted contract имеет смысл только там, где upstream реально сокращает объём выборки или число запросов, а не просто повторяет локальную post-filtering логику
- текущие реальные примеры: ProxyNova для limit и country-aware cold fetch, ProxyScrape для protocol, country и anonymity narrowing

## ProxyFactory Convention

ProxyFactory помечен как ComponentOwner для IProxyProvider. Для него уже генерируется surface вида Use, UnUse, Has и related helpers через codegen.

Практическое правило:

- не добавлять ручные provider-specific методы вроде UseGeoNode или UseProxyNova в ProxyFactory
- использовать generated component-owner surface и обычный Use(provider); attached provider-ы подхватываются фабрикой через общий registration flow

## Diagnostics Surface

Публичный diagnostic surface ProxyFactory состоит из трех частей:

- Logger: принимает внешний ILogger для runtime-сигналов refresh, rebuild, targeted cold-start path и lease cleanup
- MeterFactory: принимает внешний IMeterFactory и публикует factory-level counters и up-down counters
- Count: показывает размер последнего перестроенного deduped aggregate snapshot под текущими factory filters

Поведение логгера в factory registration flow для attached provider-а:

- если у provider-а уже задан собственный ILogger, фабрика его не перезаписывает
- если у provider-а Logger не задан, фабрика назначает свой Logger до первого runtime-use этого provider-а в registration/refresh path
- concrete provider-ы также принимают optional ILogger в конструкторе, чтобы вызывающий код мог зафиксировать отдельный provider-level sink до регистрации в фабрике

Для Count важно учитывать семантику обновления:

- это не мгновенный live-view поверх setter-ов filter-ов и provider stack
- значение меняется после фонового rebuild, который фабрика сама сигналит при attach, detach, refresh и смене filter-ов
- если вызывающему коду нужна синхронизация после mutation, нужно дождаться следующего rebuild-triggered состояния, а не читать Count немедленно в той же инструкции

Текущий namespace метрик:

- proxy.factory.refresh.success
- proxy.factory.refresh.failure
- proxy.factory.rebuild
- proxy.factory.lease.granted
- proxy.factory.lease.released
- proxy.factory.selection.targeted
- proxy.factory.count.active
- proxy.factory.count.blocked
- proxy.factory.count.providers

Текущие logger-сигналы ориентированы на operational diagnosis, а не на audit trail:

- attach и detach provider-ов
- refresh success
- refresh failure с явным различием между preserved stale snapshot и cleared snapshot
- rebuild completion
- targeted cold-start selection
- manual и timeout-based lease cleanup

## Logging Convention

Runtime-логирование в модуле оформляется по единому правилу:

- сообщения оформляются через отдельные internal static partial классы наподобие ProxyFactoryLogs с LoggerMessage extension-методами, а не через inline LogDebug, LogWarning и подобные вызовы
- текст runtime-сообщений должен быть полностью на русском языке; смешанные формулировки вида `targeted cold-start`, `rebuild` или `lease` в сообщениях не допускаются
- EventId резервируются диапазонами по подсистемам; для ProxyFactory сейчас используется диапазон 1000-1099
- operational-сигналы штатного потока идут в Debug, а деградации и потери актуальности snapshot-а поднимаются в Warning
- README и тесты должны фиксировать значимые диагностические ветки, если меняется смысл сообщений или уровней

## Provider Endpoint Surfaces

Для provider-ов, которые раньше принимали только raw endpoint string, теперь используется явная конфигурация через options-модели плюс CreateEndpoint helpers.

Текущий stack:

- ProxyNovaProviderOptions: RequestsPerSecondLimit, Country, Near, Limit
- ProxyNovaProviderOptions.FetchPublishedCountries включает проход по опубликованным country filters ProxyNova, если не задан явный country или near
- ProxyNovaNearLocation: value object для near filter
- GeoNodeProxyProviderOptions: Limit, Page, FetchAllPages, RequestsPerSecondLimit, SortBy, SortType
- ProxyScrapeProviderOptions: RequestsPerSecondLimit, Protocol, TimeoutMilliseconds, Country, Ssl, Anonymity
- OpenProxyListProviderOptions: RequestsPerSecondLimit, Protocol
- ProxiflyProxyListProviderOptions: RequestsPerSecondLimit, Protocol
- ProxymaniaProxyListProviderOptions: RequestsPerSecondLimit, Protocol, Country, MaximumSpeedMilliseconds, Page, FetchAllPages
- HideMyNameProxyListProviderOptions: RequestsPerSecondLimit, CountryFilter, MaximumSpeedMilliseconds, TypeFilter, AnonymityFilter, Start, FetchAllPages
- IplocateProxyListProviderOptions: RequestsPerSecondLimit, Protocol
- R00teeProxyListProviderOptions: RequestsPerSecondLimit, Protocol
- VakhovProxyListProviderOptions: RequestsPerSecondLimit, Protocol
- GfpcomProxyListProviderOptions: RequestsPerSecondLimit, Protocol
- ZaeemProxyListProviderOptions: RequestsPerSecondLimit, Protocol

Во всех случаях старый endpoint-based конструктор сохранен как escape hatch для нестандартных feed URL.

Для GeoNode есть отдельный режим полного inventory прохода:

- FetchAllPages включает page-walk до total из ответа API
- RequestsPerSecondLimit ограничивает частоту старта page-request, чтобы можно было быстро выгрузить весь пул без burst-спайков по клиенту
- RetryAttempts и RetryDelayMilliseconds позволяют переживать GeoNode 429/5xx с backoff и учетом Retry-After

Для ProxyScrape стоит учитывать отдельную особенность targeted path:

- provider normalizes ProxyType из protocol query, а не маркирует весь plain-text ответ как HTTP
- targeted fetch поверх ProxyScrape полезен именно тогда, когда фабрика уже знает требуемый protocol, country или anonymity и может избежать общего all/all/all запроса
- если requested filters шире, чем умеет выразить один upstream параметр, provider может вернуть более широкий batch, а окончательная фильтрация всё равно остаётся за фабрикой

Для всех NetworkProxyProvider действует provider-level runtime limiter по старту HTTP-запросов.

- ProxyScrapeProviderOptions.RequestsPerSecondLimit и ProxyNovaProviderOptions.RequestsPerSecondLimit настраивают тот же механизм для single-endpoint provider-ов
- OpenProxyListProviderOptions.RequestsPerSecondLimit настраивает тот же механизм для raw GitHub list endpoint-а
- ProxiflyProxyListProviderOptions.RequestsPerSecondLimit настраивает тот же механизм для mixed-scheme raw GitHub list endpoint-а
- ProxymaniaProxyListProviderOptions.RequestsPerSecondLimit настраивает тот же механизм для HTML-таблицы ProxyMania
- HideMyNameProxyListProviderOptions.RequestsPerSecondLimit настраивает тот же механизм для HTML-таблицы hide-my-name
- IplocateProxyListProviderOptions.RequestsPerSecondLimit настраивает тот же механизм для raw GitHub list endpoint-а
- R00teeProxyListProviderOptions.RequestsPerSecondLimit настраивает тот же механизм для raw GitHub list endpoint-а
- VakhovProxyListProviderOptions.RequestsPerSecondLimit настраивает тот же механизм для raw GitHub list endpoint-а
- GfpcomProxyListProviderOptions.RequestsPerSecondLimit настраивает тот же механизм для raw GitHub wiki list endpoint-а
- ZaeemProxyListProviderOptions.RequestsPerSecondLimit настраивает тот же механизм для raw GitHub list endpoint-а
- ограничение применяется на уровне instance и полезно для безопасного refresh/diagnostic запуска без burst-пиков
- текущие рабочие defaults: GeoNode 4 rps, ProxyScrape 2 rps, ProxyNova 2 rps, OpenProxyList 2 rps, Proxifly 2 rps, Proxymania 1 rps, HideMyName 1 rps, Iplocate 2 rps, R00tee 2 rps, Vakhov 2 rps, Gfpcom 2 rps, Zaeem 2 rps

## OpenProxyList Notes

OpenProxyListProvider использует raw plain-text списки из roosterkid/openproxylist и входит в текущий hourly-curated набор GitHub-backed источников.

Ключевые детали:

- поддерживаются списки `https`, `socks4` и `socks5`
- ответ приходит как plain-text `host:port`
- тип proxy задается выбранным protocol, потому что feed не несет отдельного per-entry metadata

## Proxifly Notes

ProxiflyProxyListProvider использует raw mixed-scheme список из proxifly/free-proxy-list и входит в текущий hourly-curated набор GitHub-backed источников.

Ключевые детали:

- feed приходит как строки вида `scheme://host:port`
- parser сам выводит тип proxy из URI scheme и может дополнительно фильтровать результаты по options.Protocol
- текущий raw endpoint единый для всех протоколов, поэтому protocol применяется на этапе нормализации, а не выбора URL

## Proxymania Notes

ProxymaniaProxyListProvider использует HTML-таблицу из proxymania.su и является текущим не-GitHub HTML-backed источником в provider stack.

Ключевые детали:

- upstream surface публикуется как таблица `tbody#resultTable` со строками `td.proxy-cell`, country flag, type, anonymity, speed и `data-timestamp` для последней проверки
- provider парсит из HTML не только `host:port`, но и тип прокси, уровень анонимности, страну по flag-code и время последней проверки
- `FetchAllPages` делает последовательный page-walk по ссылке `Next`, поэтому источник можно использовать как для одной HTML-страницы, так и для полного прохода по выборке
- practically `FetchAllPages` стоит считать inventory/diagnostic режимом: в текущем live срезе полный проход даёт заметно больше уникальных endpoint-ов, но не открывает новых host-level источников и стоит существенно дороже по числу запросов и времени
- `Protocol`, `Country` и `MaximumSpeedMilliseconds` выражаются в query string upstream-а до начала загрузки, а не только post-filtering на стороне фабрики
- текущий live overlap-аудит показывает, что узкие HTTPS-срезы ProxyMania почти полностью перекрываются текущим curated union, поэтому более осмысленный дефолт для provider-а и live diagnostics — неотфильтрованная первая страница `all`, а не жёсткий `https/country/speed` slice

## HideMyName Notes

HideMyNameProxyListProvider использует публичную HTML-таблицу hide-my-name.app и является HTML-backed источником с `start=`-пагинацией.

Ключевые детали:

- upstream surface публикуется как обычная server-rendered таблица с отдельными колонками `IP`, `Port`, `Страна/Город`, `Тип`, `Анонимность` и относительным временем последней проверки
- в отличие от ProxyMania одна строка может публиковать несколько protocol surfaces сразу, например `SOCKS4, SOCKS5`, поэтому provider выбирает наиболее сильный поддерживаемый transport в порядке `SOCKS5 -> SOCKS4 -> HTTPS -> HTTP`
- `FetchAllPages` использует `start=`-пагинацию и полезен, когда нужен полный inventory проход по публичной таблице
- raw export/API endpoints у hide-my-name gated платной подпиской, поэтому provider опирается именно на публичный HTML surface, а не на login-only export flow
- текущий live overlap для first-page срезов показывает, что дефолтная страница и `type=h` сейчас дают примерно одинаковый лучший endpoint-level прирост против curated union, тогда как более узкие срезы вроде `anon=4` или `type=5` заметно хуже; поэтому runtime default и live diagnostics оставлены на неотфильтрованной первой странице

## Iplocate Notes

IplocateProxyListProvider использует raw plain-text списки из iplocate/free-proxy-list и подходит под текущую политику отбора GitHub-backed feed-ов.

Ключевые детали:

- поддерживаются списки `http`, `https`, `socks4` и `socks5`
- ответ приходит как plain-text `host:port`
- сам проект заявляет в README refresh каждые 30 минут, поэтому источник подходит под текущий strict hourly filter
- тип proxy задается выбранным protocol, потому что feed не несет отдельного per-entry metadata

## R00tee Notes

R00teeProxyListProvider использует raw plain-text списки из r00tee/Proxy-List и подходит под текущую политику отбора GitHub-backed feed-ов.

Ключевые детали:

- поддерживаются списки `https`, `socks4` и `socks5`
- ответ приходит как plain-text `host:port`
- сам проект заявляет в README refresh каждые 5 минут, поэтому источник подходит под текущий strict hourly filter
- тип proxy задается выбранным protocol, потому что feed не несет отдельного per-entry metadata

## Vakhov Notes

VakhovProxyListProvider использует raw plain-text списки из vakhov/fresh-proxy-list и подходит под текущую политику отбора GitHub-backed feed-ов.

Ключевые детали:

- поддерживаются списки `http`, `https`, `socks4` и `socks5`
- ответ приходит как plain-text `host:port`
- сам проект заявляет в README refresh каждые 5-20 минут, поэтому источник подходит под текущий strict hourly filter
- тип proxy задается выбранным protocol, потому что feed не несет отдельного per-entry metadata

## Gfpcom Notes

GfpcomProxyListProvider использует raw GitHub wiki-списки из gfpcom/free-proxy-list и подходит под текущую политику отбора GitHub-backed feed-ов.

Ключевые детали:

- поддерживаются списки `http`, `https`, `socks4` и `socks5`
- upstream surface опубликован как protocol-specific wiki raw links, поэтому provider выбирает отдельный endpoint по options.Protocol
- parser принимает как plain-text `host:port`, так и строки вида `scheme://host:port` или `scheme://user:pass@host:port`
- сам проект заявляет hourly workflow и 30-minute refresh cadence, поэтому источник подходит под текущий strict hourly filter

## Zaeem Notes

ZaeemProxyListProvider использует raw plain-text списки из Zaeem20/FREE_PROXIES_LIST и возвращён в curated stack только на свежем surface, который сейчас проходит strict hourly filter.

Ключевые детали:

- поддерживаются только `http`, `https` и `socks4`
- ответ приходит как plain-text `host:port`
- сам проект заявляет refresh каждые 10 минут, а file history сейчас подтверждает свежесть для `http.txt`, `https.txt` и `socks4.txt`
- `socks5.txt` намеренно не включён, потому что текущая история файла выглядит stale и не проходит policy bar

## Shared Endpoint Helper

Общая логика сборки query string и простой нормализации значений вынесена в internal helper ProviderEndpointBuilder.

Его задача:

- не дублировать Uri.EscapeDataString и string.Join в каждом provider-е
- централизовать простые правила PositiveOrDefault и string normalization
- сохранить public API provider-ов маленьким, а внутреннюю сборку endpoint-ов единообразной

## GitHub Feed Freshness Policy

Для GitHub-backed plain-text и mixed-scheme feed-ов в поддерживаемый stack включаются только источники, которые проходят живую проверку свежести.

Это правило относится именно к raw feed-источникам на GitHub. Для API-backed provider-ов вроде GeoNode, ProxyScrape и ProxyNova основными health-сигналами считаются доступность endpoint-а, форма ответа, живые timestamp-поля или runtime-индикаторы свежести, а также поведение под refresh и throttle.

Текущие правила отбора:

- feed должен быть доступен по raw endpoint без HTML-парсинга и без нестабильных обходных путей
- по истории коммитов конкретного feed-файла источник должен обновляться не реже чем примерно раз в час
- источники с явно устаревшим file history, редкими обновлениями или неоднозначной cadence не входят в curated default stack

Дополнительное правило для fresh, но пересекающихся feed-ов:

- freshness сам по себе не гарантирует включение в curated stack
- перед добавлением нового GitHub-backed source нужно проверить overlap на том protocol surface, который реально будет включён в provider
- если источник не даёт meaningful unique coverage против текущего curated union и не приносит другой явной пользы вроде нового protocol surface, более простого raw contract или осознанной резервной избыточности, его не стоит держать в default stack
- уже добавленный источник с нулевой additive value может быть удалён после overlap-аудита, даже если он остаётся fresh по file history

По этой причине текущий curated GitHub-backed набор ограничен OpenProxyListProvider, ProxiflyProxyListProvider, IplocateProxyListProvider, R00teeProxyListProvider, VakhovProxyListProvider, GfpcomProxyListProvider и ZaeemProxyListProvider.

## ProxyNova Notes

ProxyNova выделен в отдельный каталог Services/ProxyNova и организован по ролям:

- provider
- parsing
- models
- serialization

Текущая реализация ProxyNova использует JSON contract через api.proxynova.com/proxylist и может расширять общий каталог проходом по опубликованным country filters с HTML-индекса ProxyNova.

Ключевые детали:

- IP адрес по-прежнему приходит в obfuscated expression и декодируется отдельным parser-ом
- parser покрывает текущие живые country-sliced patterns вроде atob, substring(start[, end]), split/reverse/join, repeat и map(String.fromCharCode)
- malformed upstream expression без валидного IPv4 результата считается мусорной записью feed-а и пропускается, а не восстанавливается эвристиками
- JSON parsing переведен на explicit DTO + source-generated JsonSerializerContext
- это сделано для совместимости с NativeAOT и чтобы не зависеть от reflection-based serialization

## Validation Status

Provider-level и factory-level surface refactor проверяется редакторной диагностикой и proxy tests. Текущая public surface ProxyFactory использует Providers вместо Services, фильтр стран принимает Country, а лишние convenience-алиасы подключения и валидации не экспортируются. Если полная сборка пакета блокируется ошибками вне proxy-проекта, сначала нужно снять внешний blocker, а затем повторить end-to-end test run.
