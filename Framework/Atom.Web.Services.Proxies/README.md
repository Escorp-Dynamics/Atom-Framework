# Atom Framework Proxies

## Overview

Этот пакет содержит абстракции и реализации для работы с пулами HTTP proxy, их ротацией, фоновым refresh и валидацией.

Основные слои:

- ProxyFactory: агрегирует несколько IProxyProvider в один container-level пул.
- ProxyProvider: базовый абстрактный provider с refresh loop, snapshot pooling и rotation.
- NetworkProxyProvider: тонкий specialization слой для провайдеров, которые грузят пул из внешнего network source.
- concrete providers в каталоге Services: GeoNode, ProxyScrape, ProxyNova и другие.

## Provider Stack

Типичный поток выглядит так:

1. concrete provider загружает внешний feed и нормализует его в ServiceProxy.
2. ProxyProvider хранит snapshot, применяет provider-level rotation и опционально валидирует прокси.
3. ProxyFactory агрегирует несколько provider-ов, применяет container-level defaults и отдает объединенный пул.

## ProxyFactory Convention

ProxyFactory помечен как ComponentOwner для IProxyProvider. Для него уже генерируется surface вида Use, UnUse, Has и related helpers через codegen.

Практическое правило:

- не добавлять ручные provider-specific методы вроде UseGeoNode или UseProxyNova в ProxyFactory
- использовать generated component-owner surface и обычный UseProvider, если нужно применить container defaults до attach

## Provider Endpoint Surfaces

Для provider-ов, которые раньше принимали только raw endpoint string, теперь используется явная конфигурация через options-модели плюс CreateEndpoint helpers.

Текущий stack:

- ProxyNovaProviderOptions: Country, Near, Limit
- ProxyNovaNearLocation: value object для near filter
- GeoNodeProxyProviderOptions: Limit, Page, SortBy, SortType
- ProxyScrapeProviderOptions: Protocol, TimeoutMilliseconds, Country, Ssl, Anonymity

Во всех случаях старый endpoint-based конструктор сохранен как escape hatch для нестандартных feed URL.

## Shared Endpoint Helper

Общая логика сборки query string и простой нормализации значений вынесена в internal helper ProviderEndpointBuilder.

Его задача:

- не дублировать Uri.EscapeDataString и string.Join в каждом provider-е
- централизовать простые правила PositiveOrDefault и string normalization
- сохранить public API provider-ов маленьким, а внутреннюю сборку endpoint-ов единообразной

## ProxyNova Notes

ProxyNova выделен в отдельный каталог Services/ProxyNova и организован по ролям:

- provider
- parsing
- models
- serialization

Текущая реализация ProxyNova использует JSON-only contract через api.proxynova.com/proxylist.

Ключевые детали:

- IP адрес по-прежнему приходит в obfuscated expression и декодируется отдельным parser-ом
- JSON parsing переведен на explicit DTO + source-generated JsonSerializerContext
- это сделано для совместимости с NativeAOT и чтобы не зависеть от reflection-based serialization

## Validation Status

Provider-level и factory-level surface refactor проверяется редакторной диагностикой и proxy tests. Если полная сборка пакета блокируется ошибками вне proxy-проекта, сначала нужно снять внешний blocker, а затем повторить end-to-end test run.
