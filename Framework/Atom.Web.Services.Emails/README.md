# Atom Framework Temporary Emails

## Overview

Этот пакет содержит доменную модель, общие базовые классы и concrete adapters для провайдеров временной почты.

Текущее состояние модуля уже покрывает две архитектурные семьи backend-ов:

- Hydra-compatible HTTP API с регистрацией аккаунта, bearer token и mailbox endpoints.
- Query-style HTTP API без регистрации аккаунта и без bearer auth.

Это означает, что пакет больше не завязан на один конкретный upstream-стиль и может принимать новые провайдеры без переписывания базового контракта.

## Model

Базовые доменные типы:

- IMail и Mail в Atom.Web: общий контракт письма, включая MarkAsReadAsync и DeleteAsync.
- IMailAccount и MailAccount в Atom.Web: общий lifecycle mailbox-сущности, включая ConnectAsync, DisconnectAsync, RefreshInboxAsync, SendAsync, Inbox, Count и MailReceived.
- ITemporaryEmailAccount и TemporaryEmailAccount: временный почтовый ящик поверх общего MailAccount с deduplicated inbox snapshot и синхронным событием появления новых писем.

Типичный поток работы выглядит так:

1. concrete provider создаёт временный ящик и возвращает ITemporaryEmailAccount.
2. аккаунт загружает inbox через RefreshInboxAsync, обновляет общий Inbox и Count и поднимает событие MailReceived для новых писем.
3. TemporaryEmailFactory владеет выбором провайдера и lifecycle выданных аккаунтов.

## Factory And Ownership

Основные orchestration-типы:

- TemporaryEmailFactory: агрегирует несколько ITemporaryEmailProvider и выбирает, через какой провайдер создать новый аккаунт.
- TemporaryEmailProvider: минимальный общий provider surface.
- TemporaryEmailProviderOptions: общие настройки генерации alias/password и политики refresh доменов.

Практическая конвенция:

- фабрика владеет только провайдерами
- провайдеры остаются компонентами Atom
- аккаунты не являются компонентами и остаются доменными объектами с lifecycle через IMailAccount и IDisposable
- наружу публикуется только общий surface без provider-specific методов вида UseMailTm или UseMailGw

## Shared Layers

Общие слои, которые уже выделены:

- TemporaryEmailCredentialGenerator: общая генерация alias и password.
- TemporaryEmailAddressUtility: compose и parse email address.
- TemporaryEmailMailMapper: stable Guid mapping и нормализация upstream mail models.
- HttpTemporaryEmailProviderOptions: общий HTTP layer с BaseUrl.
- HttpTemporaryEmailProvider: общие request helpers, JSON content helpers и send/read workflow.
- DomainRefreshingTemporaryEmailProvider: общий refresh gate доменов, cached AvailableDomains, выбор домена и построение address.

## Hydra Stack

Для провайдеров семейства mail.tm-like API используется отдельный shared stack:

- HydraTemporaryEmailJsonContext и Hydra-compatible DTO models.
- HydraTemporaryEmailProvider: общий provider workflow для account creation, token auth, inbox loading и upstream mail operations.
- IHydraTemporaryEmailProviderOperations: bridge между Hydra provider и Hydra account.
- HydraTemporaryEmailAccount: общий account layer для auth, refresh inbox и upstream operations.
- HttpTemporaryEmailMail и IHttpTemporaryEmailMailOperations: общий mail layer для upstream id и generic delete or mark-as-read delegation.

Concrete Hydra-compatible adapters:

- MailTmProvider
- MailGwProvider

Оба этих провайдера теперь являются тонкими адаптерами вокруг service identity, default BaseUrl и concrete account or mail factories.

## Non-Hydra Stack

Для query-style API без регистрации аккаунта и без bearer auth используется отдельный adapter путь:

- OneSecMailProvider
- OneSecMailAccount
- OneSecMailMail

Этот provider подтверждает, что модуль способен работать не только с Hydra-compatible upstream API.

## Current Providers

Сейчас пакет содержит уже не только proof-провайдеры, но и расширенную публичную матрицу receive-only адаптеров.

Базовые провайдеры:

- MailTmProvider: Hydra-compatible provider поверх публичного API mail.tm.
- MailGwProvider: Hydra-compatible provider поверх публичного API mail.gw.
- OneSecMailProvider: query-style provider без account registration и bearer auth.

Дополнительно в модуле уже есть публичные no-registration/fixed-domain адаптеры, включая MailCatchProvider, FakeMailProvider, FakeMailGeneratorProvider, GeneratorEmailProvider, InboxesProvider, EmailFakeProvider, TempailProvider, MoaktProvider, MailgenProvider и DisposableMailProvider.

Текущие concrete accounts остаются receive-only и опираются на верхнеуровневый контракт IMailAccount.CanSend == false.

## Account Event Surface

Каждый TemporaryEmailAccount публикует событие MailReceived.

- событие поднимается только для писем, которых ещё не было в локально известном snapshot аккаунта
- событие синхронное и возникает во время RefreshInboxAsync, поэтому вызывающий код сам контролирует частоту poll-цикла и время выполнения обработчиков
- Inbox отдаёт последний deduplicated snapshot inbox как IEnumerable общих Mail-моделей из Atom.Web
- dedup выполняется по общему Mail.Id из родительского пакета Atom.Web

## Factory Selection

TemporaryEmailFactory поддерживает две стратегии выбора провайдера:

- RoundRobin: равномерно чередует доступные провайдеры
- Random: случайно перемешивает кандидатов перед попыткой создания аккаунта

Factory также умеет фильтровать провайдеры по запросу на создание аккаунта, например по желаемому домену.

## Live Receive Tests

В проекте Tests/Atom.Web.Services.Emails.Tests есть explicit integration suite для живой проверки доставки письма в созданный временный ящик.

Для запуска нужны переменные окружения:

- ATOM_EMAIL_TEST_SENDER: тип transport-а для отправки тестового письма. Сейчас поддерживаются значения smtp, http и toolsphere.

- ATOM_EMAIL_TEST_SMTP_HOST
- ATOM_EMAIL_TEST_SMTP_PORT
- ATOM_EMAIL_TEST_SMTP_SSL
- ATOM_EMAIL_TEST_SMTP_USERNAME
- ATOM_EMAIL_TEST_SMTP_PASSWORD
- ATOM_EMAIL_TEST_SMTP_FROM
- ATOM_EMAIL_TEST_SMTP_FROM_NAME

Для http transport-а:

- ATOM_EMAIL_TEST_HTTP_ENDPOINT
- ATOM_EMAIL_TEST_HTTP_AUTHORIZATION
- ATOM_EMAIL_TEST_HTTP_FROM
- ATOM_EMAIL_TEST_HTTP_FROM_NAME

Для toolsphere transport-а:

- ATOM_EMAIL_TEST_TOOLSPHERE_API_KEY
- ATOM_EMAIL_TEST_TOOLSPHERE_NAME

Отправка писем выделена через отдельный sender abstraction. Сейчас в проекте есть SMTP transport, generic HTTP transport для внешнего relay/webhook сценария и concrete toolsphere transport поверх бесплатного RapidAPI sender-а, поэтому live suite не привязана к одному конкретному способу отправки.

Эти тесты намеренно помечены как Explicit и Category("Integration"), чтобы обычный прогон test project не зависел от внешнего sender-а и реальной доставки писем.

Пример запуска live suite через Tool Sphere на Linux:

1. получить бесплатный RapidAPI key и бесплатную подписку на mail-sender-api1
2. выставить переменные окружения:

 ATOM_EMAIL_TEST_SENDER=toolsphere
 ATOM_EMAIL_TEST_TOOLSPHERE_API_KEY=ваш_rapidapi_key
 ATOM_EMAIL_TEST_TOOLSPHERE_NAME=AtomTests

1. запустить только integration suite:

 dotnet test --project Tests/Atom.Web.Services.Emails.Tests/Atom.Web.Services.Emails.Tests.csproj --filter Category=Integration

Если переменная ATOM_EMAIL_TEST_SENDER не задана, фабрика по умолчанию использует smtp transport, поэтому для Tool Sphere её нужно указывать явно.

Пример запуска live suite через Brevo SMTP на Linux:

1. создать бесплатный аккаунт в Brevo и получить SMTP credentials
2. выставить переменные окружения:

 ATOM_EMAIL_TEST_SENDER=smtp
 ATOM_EMAIL_TEST_SMTP_HOST=smtp-relay.brevo.com
 ATOM_EMAIL_TEST_SMTP_PORT=587
 ATOM_EMAIL_TEST_SMTP_SSL=false
 ATOM_EMAIL_TEST_SMTP_USERNAME=ваш_brevo_smtp_login
 ATOM_EMAIL_TEST_SMTP_PASSWORD=ваш_brevo_smtp_password
 ATOM_EMAIL_TEST_SMTP_FROM=подтверждённый_sender_email
 ATOM_EMAIL_TEST_SMTP_FROM_NAME=AtomTests

3. запустить только integration suite:

 dotnet test --project Tests/Atom.Web.Services.Emails.Tests/Atom.Web.Services.Emails.Tests.csproj --filter Category=Integration

Для live receive tests это лучший класс провайдера, чем публичные анонимные формы отправки: SMTP relay реально доставляет письма наружу и не упирается в captcha или нестабильные HTML-формы.

## Extension Guide

Если upstream API похож на mail.tm or mail.gw:

1. создать provider options с BaseUrl
2. унаследовать provider от HydraTemporaryEmailProvider
3. добавить concrete account и mail wrappers

Если upstream API query-style и не требует account registration or bearer auth:

1. унаследовать provider от DomainRefreshingTemporaryEmailProvider
2. реализовать account поверх TemporaryEmailAccount
3. реализовать concrete mail model только с теми upstream operations, которые реально поддерживает сервис

Правило для новых extraction steps:

- выносить только то, что уже повторяется минимум между двумя concrete adapters
- не поднимать провайдер-специфичную механику в общий слой до появления второго реального потребителя
