# Roadmap: 100% Browser Mimicry

## Purpose

Этот документ фиксирует подробный roadmap по достижению максимально полной мимикрии под современные браузеры для сетевого стека Atom.Net.

Цель документа не в абстрактном списке идей, а в рабочем плане:

- разбить задачу на независимые слои;
- для каждого слоя определить наблюдаемое поведение на проводе;
- для каждого слоя заранее задать тестовую стратегию и критерии готовности;
- двигаться малыми точечными шагами без размазывания изменений по всей системе.

## Status Legend

- planned: фаза еще не начиналась.
- in-progress: по фазе уже идут изменения в active path.
- blocked: фаза уперлась в внешний dependency или недостающий reference harness.
- done: acceptance criteria фазы выполнены.
- partial: часть deliverables закрыта, но exit criteria фазы еще не достигнуты.

## Progress Snapshot

| Area | Snapshot |
| --- | --- |
| H1 header order/casing | partial |
| H1 value serialization | partial |
| fetch metadata | partial |
| referrer policy | partial |
| rich request context | planned |
| browser family divergence | planned |
| TLS fingerprint | partial |
| H2/H3 parity | planned |
| stateful browser model | planned |

## Tracking Fields

Каждая фаза ниже должна сопровождаться кратким operational block:

- Status
- Owner
- Started
- Last Updated
- Exit Gate
- Open Risks

Если фаза дробится на подзадачи, статус нужно обновлять в документе сразу после завершения regression gate.

## Critical Note

Если говорить строго, 100% мимикрия под все современные браузеры не может быть достигнута одним только H1 path.

Причина простая:

- современные браузеры в реальном мире в основном живут на HTTP/2 и HTTP/3;
- transport fingerprint задается не только заголовками H1, но и TLS 1.3, ALPN, H2/H3 settings, connection reuse, request scheduling, priority behavior и browser state machine;
- даже идеальный H1 wire image останется только одним из слоев полной мимикрии.

Поэтому roadmap разделен на две большие зоны:

1. Максимальная H1-мимикрия.
2. Полная browser-паритетная мимикрия, выходящая за пределы H1.

## Current Baseline

На текущем активном path уже есть:

- browser-shaped H1 header order и casing;
- profile-driven request shaping по browser family;
- улучшенная H1 сериализация User-Agent, Accept, Accept-Language;
- sec-fetch-site с scheme-aware и registrable-domain-aware логикой;
- базовая browser-like логика Origin / Referer / Service-Worker;
- preload destination inference по URI;
- preload-specific Accept и mode для части destinations;
- Chromium-like priority header heuristics;
- family-specific TLS extension ordering в активном TLS 1.2 path;
- tests на активный H1/TLS slice.

Это уже хороший фундамент, но это еще не browser parity.

## Execution Board

| Phase | Name | Priority | Status | Main Dependency | Main Output |
| --- | --- | --- | --- | --- | --- |
| 0 | Reference Harness | P0 | planned | none | browser capture fixtures |
| 1 | Referrer Policy Layer | P0 | partial | 0 | correct Referer / Origin policy |
| 2 | Rich Request Context Model | P0 | partial | 1 | internal context surface |
| 3 | Full Fetch Metadata Matrix | P1 | partial | 2 | sec-fetch parity |
| 4 | Accept Family Tables | P1 | planned | 2, 3 | browser-like Accept surface |
| 5 | Client Hints Surface | P1 | planned | 2, 7 | Chromium family hints parity |
| 6 | Priority Model | P1 | in-progress | 2, 4 | destination-aware Priority surface |
| 7 | Browser Family Divergence Layer | P1 | planned | 2, 4, 5, 6 | non-Chromium parity |
| 8 | Redirect / Cookie / Conditional Chains | P2 | planned | 1, 2, 4 | chain parity |
| 9 | Authentication / Proxy / Challenge Surface | P2 | planned | 8 | auth/proxy parity |
| 10 | TLS 1.3 And ClientHello Parity | P0 | planned | 0 | transport fingerprint parity |
| 11 | HTTP/2 And HTTP/3 Reality Layer | P0 | planned | 10 | protocol parity |
| 12 | Stateful Browser Network Model | P2 | planned | 1-11 | session-level parity |

## Phase Progress Overview

- [ ] Phase 0. Reference Harness
- [ ] Phase 1. Referrer Policy Layer
- [ ] Phase 2. Rich Request Context Model
- [ ] Phase 3. Full Fetch Metadata Matrix
- [ ] Phase 4. Accept Family Tables
- [ ] Phase 5. Client Hints Surface
- [ ] Phase 6. Priority Model
- [ ] Phase 7. Browser Family Divergence Layer
- [ ] Phase 8. Redirect / Cookie / Conditional Chains
- [ ] Phase 9. Authentication / Proxy / Challenge Surface
- [ ] Phase 10. TLS 1.3 And ClientHello Parity
- [ ] Phase 11. HTTP/2 And HTTP/3 Reality Layer
- [ ] Phase 12. Stateful Browser Network Model

## Dependency Map

Высокоуровневые зависимости между фазами:

- Phase 0 нужна почти всем остальным фазам как источник truth data.
- Phase 1 и Phase 2 являются главным основанием для всей request-level мимикрии.
- Phase 3, 4, 5 и 6 не должны масштабироваться дальше без завершения хотя бы минимального слоя Phase 2.
- Phase 7 нельзя делать качественно, пока не закрыты family-agnostic primitives в Phase 3-6.
- Phase 10 обязателен до честных заявлений о browser parity.
- Phase 11 обязателен до честных заявлений о parity с современными браузерами в production reality.
- Phase 12 нужно начинать только после того, как одиночные request snapshots стали достаточно точными.

## Mandatory Regression Gates

После каждой прикладной итерации обязательно должны быть зелеными:

- focused HTTPS request-shaping slice;
- focused H1 serialization slice;
- focused TLS settings slice, если менялся transport layer;
- новые точечные tests для закрываемого gap;
- negative tests на отсутствие нежелательных side effects.

Если меняется request shaping, минимальный обязательный gate должен подтверждать:

- exact header presence;
- exact header absence;
- exact serialized value;
- relative header ordering;
- family-specific behavior, если слой зависит от browser family.

## Iteration Checklist

Каждая отдельная задача в roadmap должна проходить через один и тот же чеклист.

### Before Code

- Есть browser reference capture.
- Есть сформулированный observable gap в одном предложении.
- Понятно, какие active files нужно менять.
- Известно, какой focused regression slice должен оставаться зеленым.

### During Code

- Меняется только один слой поведения.
- Новая логика не живет только в profile config без runtime wiring.
- Header value и header order проверяются отдельно.
- Временные эвристики отмечены как временные и локализованы.

### After Code

- Добавлен positive test.
- Добавлен negative test.
- Regression slice зеленый.
- Зафиксирован remaining gap, если слой закрыт не полностью.

## Milestone Slices

Чтобы roadmap был исполнимым, удобно разбить его на milestone-группы.

### Milestone A. H1 Policy Correctness

Сюда входят:

- Phase 1;
- минимально необходимая часть Phase 2;
- базовая часть Phase 3.

Результат milestone:

- Referer / Origin / sec-fetch-* перестают быть грубыми эвристиками и становятся policy-driven.

### Milestone B. H1 Browser Surface

Сюда входят:

- оставшаяся часть Phase 3;
- Phase 4;
- Phase 5;
- Phase 6.

Результат milestone:

- H1 wire image начинает походить на реальный browser output не только по наличию заголовков, но и по связанным tables значений.

### Milestone C. Browser Family Split

Сюда входят:

- Phase 7.

Результат milestone:

- Chromium, Firefox и Safari перестают выглядеть как вариации одного и того же запроса.

### Milestone D. Stateful H1 Behavior

Сюда входят:

- Phase 8;
- Phase 9.

Результат milestone:

- request chains и challenge flows становятся более браузероподобными.

### Milestone E. Beyond H1

Сюда входят:

- Phase 10;
- Phase 11;
- Phase 12.

Результат milestone:

- можно всерьез обсуждать parity не только на H1 snapshot level, но и на уровне полноценного browser transport/runtime model.

## Definition Of Done

Промежуточная готовность слоя считается достигнутой только если одновременно выполнены все условия:

- поведение выражено в коде активного runtime path, а не только в profile config или dead code;
- наблюдаемое поведение можно увидеть на проводе или в transport handshake;
- есть точечные тесты на позитивные и негативные сценарии;
- новые тесты не флапают и не висят бесконечно;
- изменения не ломают соседний HTTPS regression slice;
- поведение согласовано между browser families, а не только для Chromium.

Финальная готовность всей задачи возможна только когда совпадают:

- request headers;
- header ordering/casing/serialization;
- connection behavior;
- TLS fingerprint;
- protocol negotiation;
- browser-family differences;
- stateful browser policy behavior.

## Test Strategy

Работа должна идти через четыре уровня тестов.

### 1. Pure Logic Tests

Назначение:

- проверка site/origin/referrer/policy classification;
- deterministic tests без сети;
- дешевые быстрые regression checks.

Что тестировать:

- registrable domain logic;
- schemeful same-site logic;
- referrer trimming;
- destination inference;
- priority table selection;
- family-specific header omission rules.

### 2. Wire-Level H1 Tests

Назначение:

- проверка реального request head на проводе;
- порядок, casing, exact values, presence/absence.

Что тестировать:

- точный request line;
- host/connection ordering;
- sec-fetch-*;
- service-worker marker;
- accept / accept-language / priority formatting;
- family-specific omissions and additions.

### 3. TLS Surface Tests

Назначение:

- проверка активного CreateTlsSettings path;
- extension order, cipher order, signature algorithms, supported groups;
- materialization per connection.

Что тестировать:

- family-specific extension order;
- dynamic SNI injection;
- TLS version policy;
- ALPN list;
- TLS 1.2 / TLS 1.3 divergence;
- session ticket behavior.

### 4. External Differential Tests

Назначение:

- сравнение Atom.Net против реальных браузеров на эталонных сценариях;
- фиксация remaining gaps до и после каждого слоя.

Что тестировать:

- request capture через локальный probe server;
- diff Chrome / Edge / Firefox / Safari vs Atom.Net;
- serialized header diff;
- TLS capture diff;
- redirect/auth/cookie/navigation chain diff.

## Work Rules

Для каждого слоя работа должна идти одинаковым паттерном:

1. Зафиксировать реальный browser reference.
2. Описать конкретный observable gap.
3. Закрыть только этот gap.
4. Добавить точечный test gate.
5. Повторно прогнать focused HTTPS slice.
6. Только потом переходить к следующему слою.

Нельзя закрывать несколько transport и policy слоев одновременно без независимых test gates.

## Phase Map

Ниже roadmap разбит на фазы в рекомендуемом порядке.

---

## Phase 0. Reference Harness

### Tracking

- Status: partial
- Owner: unassigned
- Started: in progress
- Last Updated: 2026-03-20
- Exit Gate: browser capture fixtures are reproducible and diffable
- Open Risks: a practical Chromium reference harness now exists through loopback and trusted custom-host captures, but it still needs broader scenario expansion, non-Chromium coverage, and a separate stable TLS fingerprinting lane before Phase 0 can be considered fully done

### Goal

Создать эталонный набор браузерных reference captures, без которого невозможно говорить о 100% мимикрии.

### Tasks

- Поднять локальный capture server для raw H1 requests.
- Поднять capture server для TLS fingerprinting.
- Снять эталоны для:
  - Chrome desktop Windows;
  - Chrome desktop Linux;
  - Edge desktop Windows;
  - Firefox desktop;
  - Safari desktop.
- Зафиксировать эталон для сценариев:
  - top-level GET navigation;
  - top-level POST navigation;
  - same-origin fetch GET;
  - same-origin fetch POST;
  - cross-site fetch GET;
  - preload script;
  - preload style;
  - preload image;
  - preload font;
  - service worker script fetch;
  - redirect chain;
  - cookie-bearing request;
  - conditional request.

### Deliverables

- deterministic capture fixtures;
- markdown reference tables;
- browser-family diff snapshots.
- WebDriver-level observed request headers stream for real browser requests.

### Current Progress

- WebDriver bridge now exposes observed request headers through a typed event surface.
- Extension-side onBeforeSendHeaders capture is wired with delayed websocket emission plus BridgeServer HTTP fallback.
- Chromium regressions for observed request headers now pass on fetch GET, top-level navigation GET, fetch POST, cookie-bearing requests, redirect chains, isolated-tab locale-driven Accept-Language shaping, and isolated-tab client hints capture.
- Client hints gap on Chromium is now closed with two layers of proof: httpbin server echo confirms `Sec-CH-UA*` really reaches the network, and the observed-header harness is enriched for the known Chromium webRequest blind spot so the capture stream reflects the verified outgoing values.
- Service worker script fetch now has a stable local network-truth fixture: a loopback origin serves both the page and `/sw.js`, and the captured request head confirms `Service-Worker: script` together with `sec-fetch-dest: serviceworker` and same-origin fetch metadata.
- Local preload network-truth coverage now closes the Chromium script/style/image/font slice on loopback origin: `link rel=preload` deterministically emits script/style/image/font requests, and the capture fixed a real H1 mismatch by proving that same-origin font preload carries `Origin` together with `sec-fetch-mode: cors`.
- Media element network-truth coverage now exists for `<audio preload=auto>` and `<video preload=auto>` on loopback origin: Chromium emits `Accept: */*`, `sec-fetch-dest: audio|video`, `sec-fetch-mode: no-cors`, no `Origin`, plus `Range: bytes=0-` and an `Accept-Encoding: identity;q=1, *;q=0` surface.
- Manifest and subtitle-track loopback truth now exist as well: Chromium requests `rel=manifest` with `Accept: */*`, `sec-fetch-dest: manifest`, `sec-fetch-mode: cors`, no `Origin`, and no `Priority`, while subtitle track requests use `Accept: */*`, `sec-fetch-dest: track`, `sec-fetch-mode: same-origin`, no `Origin`, and no `Priority`.
- The broader Chromium loopback H1 picture is now consistent for all currently captured subresource cases: script/style/image/font preload plus audio/video/track/manifest requests all omit `Priority`, so the earlier H1 preload/media priority heuristic has been narrowed out of the confirmed active path rather than extended speculatively.
- Active H1 shaping now aligns to the captured media-element truth for the currently modeled audio/video branch: same-origin `audio|video` requests with `sec-fetch-mode: no-cors` emit `Range: bytes=0-`, use media-specific `Accept-Encoding: identity;q=1, *;q=0`, and H1 request-head serialization preserves compact browser-like `Accept-Encoding` q-values instead of canonicalizing them to `q=1.0` / `q=0.0`.
- Active H1 shaping now also aligns to the newly captured manifest/track slice: manifest and track no longer emit speculative `Priority`, and track requests now use `sec-fetch-mode: same-origin` instead of the earlier preload-style heuristic.
- A new practical secure-origin-like harness now exists for Chromium capture work: a custom host mapped to loopback with `--host-resolver-rules` plus `--unsafely-treat-insecure-origin-as-secure=` becomes a secure context and can register a service worker while still sending raw requests to the local TCP fixture.
- That secure-origin-like harness is now validated for a real manifest wire capture as well: Chromium reaches `/site.webmanifest` on the trusted custom host, keeps `Accept: */*`, `sec-fetch-dest: manifest`, `sec-fetch-mode: cors`, and still omits both `Origin` and `Priority`.
- The same trusted custom-host harness is now validated for subtitle track and media-element requests too: secure-like track stays on `Accept: */*`, `sec-fetch-dest: track`, `sec-fetch-mode: same-origin`, no `Origin`, no `Priority`, while secure-like audio/video keep `Accept: */*`, `sec-fetch-dest: audio|video`, `sec-fetch-mode: no-cors`, no `Origin`, no `Priority`, and also send `Accept-Encoding: identity;q=1, *;q=0` plus `Range: bytes=0-`.
- The trusted custom-host harness now covers the next secure-only slice too: worker/sharedworker script fetches stay on `Accept: */*`, `sec-fetch-dest: worker|sharedworker`, `sec-fetch-mode: same-origin`, no `Origin`, while secure-context font preload stays on `Accept: */*`, `sec-fetch-dest: font`, `sec-fetch-mode: cors`, and carries `Origin` for the trusted custom host.
- Focused H1 worker/sharedworker/font tests remain green against that new secure-only truth, so no additional runtime adjustment is required for this slice.
- The secure custom-host truth set now also covers module worker import graphs and iframe subresources: the module worker entry request stays on `Accept: */*`, `sec-fetch-dest: worker`, `sec-fetch-mode: same-origin`, no `Origin`, no `Priority`, but the imported dependency keeps `sec-fetch-dest: worker`, switches to `sec-fetch-mode: cors`, carries `Origin`, and still omits `Priority`; iframe navigation stays on `sec-fetch-dest: iframe` with `navigate`, while iframe script/style/image/font subresources preserve their real destinations instead of collapsing to `iframe`.
- Shared worker module imports are now measured too and mirror the worker-module pattern on the trusted custom host: the entry request stays `sharedworker/same-origin` with no `Origin` and no `Priority`, while the imported dependency stays `sharedworker/cors`, carries `Origin`, and still omits `Priority`.
- Service worker module imports are now measured as well and stay closer to the worker-module pattern than to the old service-worker priority heuristic: the entry request keeps `Service-Worker: script`, `serviceworker/same-origin`, no `Origin`, and no `Priority`, while the imported dependency keeps `serviceworker/cors`, carries `Origin`, and still omits `Priority`.
- The secure worker-class captures now also close the Chromium Accept-Encoding branch for these requests: classic worker, classic sharedworker, worker module graphs, sharedworker module graphs, and serviceworker module graphs all stay on `Accept-Encoding: gzip, deflate`, so active H1 now narrows Chromium-family worker-class requests away from the broader fetch default.
- Trusted custom-host capture now also closes the Chromium `link rel=modulepreload` branch: same-origin modulepreload requests keep `Accept: */*`, `sec-fetch-dest: script`, `sec-fetch-mode: cors`, emit `Origin`, omit `Priority`, and stay on `Accept-Encoding: gzip, deflate` instead of the broader generic preload surface.
- Trusted custom-host capture now also closes the document module script graph on Chromium: both the entry module and imported dependency stay on `Accept: */*`, `sec-fetch-dest: script`, `sec-fetch-mode: cors`, emit `Origin`, omit `Priority`, and keep `Accept-Encoding: gzip, deflate`; only `Referer` advances from the document URL to the importing module URL.
- Trusted custom-host capture now also closes the document stylesheet import graph on Chromium: both the entry stylesheet and imported dependency stay on `Accept: text/css,*/*;q=0.1`, `sec-fetch-dest: style`, `sec-fetch-mode: no-cors`, omit `Origin` and `Priority`, and use `Accept-Encoding: gzip, deflate`; only `Referer` advances from the document URL to the importing stylesheet URL.
- Trusted custom-host capture now also closes the Chromium `link rel=prefetch` branch: same-origin prefetch requests keep `Accept: */*`, `sec-fetch-dest: empty`, `sec-fetch-mode: no-cors`, emit `Sec-Purpose: prefetch`, omit `Origin` and `Priority`, and use `Accept-Encoding: gzip, deflate`, so active H1 now models prefetch as a dedicated request family instead of falling through generic fetch/preload heuristics.
- Trusted custom-host capture now also covers multipart form submission on the secure-like path: Chromium navigation POST keeps `Content-Type: multipart/form-data; boundary=...`, `Origin`, `Referer`, `Upgrade-Insecure-Requests: 1`, `sec-fetch-mode: navigate`, `sec-fetch-dest: document`, and `Accept-Encoding: gzip, deflate`, while the raw body contains the expected multipart boundary and `Content-Disposition` blocks. Current WebDriver evidence uses the synthetic page-local click path, so the observed absence of `Sec-Fetch-User` should be treated as a harness limitation until a trusted physical-input path exists.
- Active Atom.Net H1 now also aligns to the measured Chromium multipart form-submit surface on the secure path: top-level form submission keeps multipart body/content headers plus `Origin`/`Referer`/`Upgrade-Insecure-Requests`/`navigate`/`document`, and Chromium-family runtime now suppresses navigation `Priority` and narrows `Accept-Encoding` to `gzip, deflate` for this scenario. `Sec-Fetch-User` remains intentionally unresolved until a non-synthetic click path exists.
- Remaining gap: the secure-origin-like custom-host harness is now enough through worker and shared-worker module imports, iframe script/style/image/font subresources, CSS font fetch, and media-element audio/video requests, and H1 iframe inference is now narrowed back to navigation-only fallback. Active callers can now supply exact context through HttpsRequestBuilder as the primary typed path or through HttpsRequestOptions as a low-level fallback, so future follow-ups should start from genuinely new browser-triggered families rather than more caller-side plumbing. Self-signed HTTPS localhost remains a dead end in the current harness and should not be the next step.
- CSS `@font-face` same-origin fetch truth is now measured on the trusted custom host too: Chromium keeps `Accept: */*`, `sec-fetch-dest: font`, `sec-fetch-mode: cors`, emits `Origin`, and still omits `Priority`, so the old preload-only font `Origin` heuristic is now closed on the active H1 path.
- Current font/media fetch branches that exist in the repo now have direct browser truth: CSS `font/cors` carries `Origin` without `Priority`, and media-element `audio|video/no-cors` keeps generic `Accept: */*` but adds `Accept-Encoding: identity;q=1, *;q=0` with `Range: bytes=0-`. Any further specialization should start from a new browser-triggered branch rather than extrapolating beyond these measured surfaces.

### Exit Criteria

- любой новый gap можно доказать сравнением с эталоном, а не ощущением.

---

## Phase 1. Referrer Policy Layer

### Tracking

- Status: partial
- Owner: unassigned
- Started: 2026-03-19
- Last Updated: 2026-03-19
- Exit Gate: Referer and Origin match browser references for baseline navigation/fetch/preload scenarios
- Open Risks: HttpsRequestBuilder and UrlBuilder are back on the active compile surface and now materialize browser metadata into HttpsRequestMessage for the live H1 path; the remaining risk is no longer caller-side plumbing but broader uncaptured request families beyond the current iframe, worker-class, font, and media-element slices.

### Why

Сейчас это самый ценный недостающий browser policy layer для активного H1 path.

### Main Gap

Логика Referer и Origin пока эвристическая и не моделирует browser-level strict-origin-when-cross-origin semantics.

### Tasks

- Ввести internal policy model для referrer emission.
- Реализовать как минимум:
  - no-referrer;
  - origin;
  - same-origin;
  - strict-origin;
  - strict-origin-when-cross-origin;
  - unsafe-url.
- Сделать policy selection по умолчанию browser-family aware.
- Отдельно покрыть downgrade rules:
  - https -> http;
  - secure -> insecure cross-site;
  - secure -> secure cross-origin.
- Синхронизировать Origin logic с referrer policy, а не с отдельной грубой эвристикой.

### Work Breakdown

#### [x] Slice 1. Policy Surface

- Ввести внутреннее представление referrer policy как отдельного policy object или enum-like surface, не привязанное напрямую к текущим RequestKind heuristics.
- Определить единый вход для вычисления effective policy на request.
- Зафиксировать precedence model:
  - explicit request override;
  - browser-family default;
  - fallback default для internal callers.
- Реализация в active path:
  - HttpsRequestMessage хранит request-level override;
  - BrowserHeaderProfile хранит profile default;
  - HttpsClientHandler вычисляет effective policy с precedence request override -> profile default.

#### [x] Slice 2. Referer Derivation

- Вынести вычисление Referer в отдельный deterministic helper.
- Разделить отдельно:
  - source referrer extraction;
  - same-origin decision;
  - same-site decision;
  - downgrade decision;
  - trimming strategy для final serialized value.
- Не смешивать вычисление Referer и решение об Origin в одном условном блоке.
- Реализация в active path:
  - HttpsClientHandler вычисляет policy-derived Referer отдельно от source referrer;
  - sec-fetch-site продолжает использовать source referrer для site-context derivation;
  - strict-origin-when-cross-origin уже режет cross-origin referrer до origin и suppresses https -> http downgrade.

#### [x] Slice 3. Origin Coupling

- Перевести Origin emission на policy-aware rules.
- Оставить отдельные ветки для:
  - safe same-origin fetch;
  - unsafe same-origin fetch;
  - cross-origin fetch;
  - top-level unsafe navigation;
  - preload;
  - service worker.
- Убедиться, что suppression Referer не ломает required Origin scenarios.
- Реализация в active path:
  - Origin emission идет через единый helper, а не через разрозненные ветки;
  - suppressed Referer больше не ломает required cross-site Origin scenarios;
  - добавлены wire tests для no-referrer override и secure cross-origin coverage, причем secure scenario стабилизирован через переносимую request-head проверку без платформенно-зависимого TLS handshake.

#### [ ] Slice 4. Browser Defaults

- Зафиксировать browser-family defaults для active path.
- Для первого прохода целиться в realistic default policy, а не в полный UI-level parity.
- Явно отметить, какие family-specific distinctions пока отложены до следующих фаз.
- Стартовая реализация уже внесена:
  - BrowserProfileCatalog использует явные family-aware header default factories для Chromium, Firefox и Safari;
  - default policy surface больше не размазан по inline object initializers;
  - следующий шаг по Slice 4 должен вводить уже behavioral divergence, а не просто структурную нормализацию.
- Первый behavioral divergence уже добавлен:
  - Chromium и Firefox по умолчанию получают Accept-Encoding с zstd;
  - Safari остается на более консервативном Accept-Encoding surface без zstd;
  - отличие зафиксировано focused wire tests и не ломает текущий HTTPS slice.
- Следующий behavioral divergence уже добавлен:
  - Safari использует более консервативный Accept-Language default без q-tail;
  - Chromium и Firefox сохраняют свои текущие family-specific значения;
  - отличие подтверждено focused wire tests и не ломает текущий HTTPS slice.
- Еще один Accept-surface divergence уже добавлен:
  - Firefox navigation Accept теперь выровнен по локальному browser capture и остается консервативным без image hints;
  - Safari navigation Accept остается на близком консервативном surface без image hints;
  - отличие подтверждено focused wire tests и не ломает текущий HTTPS slice.
- Еще один preload Accept divergence уже добавлен:
  - Firefox image preload Accept теперь отделен от Chromium и использует более узкий surface без image/apng и image/svg+xml;
  - Chromium сохраняет более широкий image preload Accept surface;
  - отличие подтверждено focused wire tests и не ломает текущий HTTPS slice.
- Safari preload Accept fallback теперь также зафиксирован явным wire test:
  - Safari image preload остается на консервативном fallback surface без image hints;
  - это отличие больше не является неявным побочным эффектом и входит в regression gate Slice 4.
- Preload matrix regression coverage также расширена на destination inference beyond current Accept divergences:
  - active path теперь явно покрыт wire tests для audio и video preload destinations;
  - текущая URI-driven эвристика для этих cases больше не остается непроверенным побочным поведением;
  - это изменение пока про coverage и stability, а не про новый browser-family split.
- Еще один Chromium-like Accept divergence уже добавлен:
  - Chromium navigation Accept теперь включает application/signed-exchange;v=b3;q=0.7;
  - связанный H1 Accept serializer сохранен на уровне runtime path так, чтобы не терять extension parameters вроде v=b3 на проводе;
  - отличие подтверждено focused wire tests и не ломает текущий HTTPS slice.
- Phase 6 capture-backed refinement уже частично подтянут в active H1 path:
  - Firefox больше не suppresses `priority` полностью и теперь следует локальным fixtures для top-level navigation (`u=0, i`) и fetch (`u=4`);
  - изменение внесено узко, без автоматического включения неподтвержденных Firefox preload/service worker priority веток;
  - отличие подтверждено focused wire tests и не ломает текущий HTTPS slice.
- Следующий Phase 6 capture-backed correction уже добавлен:
  - Chromium-family fetch priority больше не остается на старом generic `u=4` и теперь выровнен по локальным Chrome/Edge fixtures как `u=1, i`;
  - разделение между Chromium-family fetch и Firefox fetch теперь закреплено отдельными wire tests;
  - изменение остается локальным и не трогает preload/service worker priority matrix.

#### [ ] Slice 5. Wire Validation

- Проверять не только presence Referer и Origin, но и exact serialized value.
- Проверять отсутствие лишнего Origin там, где браузер его не шлет.
- Проверять отсутствие утечки full URL в downgrade и cross-origin scenarios.

### Implementation Order

1. Выделить pure policy evaluator без wire serialization.
2. Подключить evaluator к вычислению Referer.
3. Подключить Origin к результату policy evaluator.
4. Прогнать baseline request kinds без family split.
5. Только после этого добавлять family-specific defaults.

### Targeted Scenario Matrix

| Scenario | Referer Expectation | Origin Expectation | Mandatory |
| --- | --- | --- | --- |
| same-origin navigation GET | full URL or browser-equivalent default | absent | yes |
| same-origin navigation POST | full URL or browser-equivalent default | present when browser emits it | yes |
| same-origin fetch GET | full URL or browser-equivalent default | absent | yes |
| same-origin fetch POST | full URL or browser-equivalent default | present | yes |
| same-site cross-origin fetch GET | origin or policy-trimmed value | browser-reference driven | yes |
| cross-site fetch GET | origin-only under default strict-origin-when-cross-origin behavior | browser-reference driven | yes |
| https -> http navigation | suppressed or origin-trimmed per policy | browser-reference driven | yes |
| preload same-origin | browser-reference driven | absent in safe path | yes |
| preload cross-site | browser-reference driven | browser-reference driven | optional first pass |
| service worker script fetch | browser-reference driven | browser-reference driven | yes |

### Files Likely Touched

- Framework/Atom.Net/Https/HttpsClientHandler.cs
- Framework/Atom.Net/Https/HttpsRequestMessage.cs
- Framework/Atom.Net/Https/HttpsRequestOptions.cs
- Framework/Atom.Net/Https/Profiles/BrowserProfileCatalog.cs
- Framework/Atom.Net/Https/HttpsRequestBuilder.cs
- Tests/Atom.Net.Tests/Https/HttpsClientHandlerTests.cs

### API Surface Notes Before Slice 2

- HttpsRequestMessage уже несет request-level override и effective policy.
- Active plain HttpRequestMessage path теперь тоже может нести browser-specific metadata: HttpsRequestOptions сохраняет RequestKind / HttpsBrowserRequestContext / ReferrerPolicyMode в request.Options, а PrepareRequest материализует их в HttpsRequestMessage на входе в H1 handler.
- HttpsRequestBuilder и UrlBuilder снова входят в active compile surface: builder теперь несёт RequestKind / HttpsBrowserRequestContext / ReferrerPolicyMode / Referrer и материализует HttpsRequestMessage прямо на Build path.
- Builder-first regression теперь тоже закрыт: explicit HttpsRequestBuilder coverage охватывает iframe script/font/style/image, а более широкий HTTPS regression slice HttpsClientHandlerTests + Https11ConnectionTests + BrowserProfileResolverTests прошёл 140/140 после выравнивания stale Priority expectations под capture-backed truth.
- Решение по API surface: HttpsRequestBuilder остаётся primary typed entrypoint для browser-shaped caller-side wiring, а HttpsRequestOptions остаётся low-level fallback для plain HttpRequestMessage callers, которые не хотят builder path.
- Builder default version теперь выровнен с live H1 path: по умолчанию он создаёт HTTP/1.1 request вместо старого HTTP/3.0 default, который ломал active H1 send path.
- Для active H1 path next-step work можно продолжать через HttpsRequestMessage, через HttpsRequestBuilder или через plain HttpRequestMessage + HttpsRequestOptions, не ожидая дополнительного caller-side API refactor.

### Non-Goals For This Phase

- Не расширять здесь client hints.
- Не менять здесь Priority model.
- Не пытаться закрыть redirect-chain state machine целиком.
- Не смешивать эту фазу с полной browser family divergence.

### Tests

- pure logic tests на trimming;
- wire tests для Referer / Origin при same-origin, same-site, cross-site, downgrade;
- browser reference diff tests.

### Slice 2 Trimming Test Plan

- pure logic: no-referrer suppresses Referer entirely.
- pure logic: origin trims full referrer to scheme plus authority.
- pure logic: same-origin keeps full referrer only for same-origin targets and suppresses otherwise.
- pure logic: strict-origin keeps origin for secure cross-origin targets and suppresses downgrade.
- pure logic: strict-origin-when-cross-origin keeps full same-origin referrer, trims secure cross-origin to origin, and suppresses downgrade.
- pure logic: unsafe-url preserves full referrer across same-origin and cross-origin requests.
- wire: same-origin fetch GET under strict-origin-when-cross-origin emits full Referer and no Origin.
- wire: cross-site fetch GET under strict-origin-when-cross-origin emits origin-trimmed Referer.
- wire: https -> http navigation under strict-origin-when-cross-origin suppresses Referer.
- wire: same-origin fetch POST under strict-origin-when-cross-origin keeps Referer and still emits Origin.
- wire: explicit no-referrer override suppresses Referer without suppressing sec-fetch-site derivation when site context can still be inferred.

### Exit Criteria

- Referer и Origin совпадают с browser references в базовых navigation/fetch/preload сценариях.

---

## Phase 2. Rich Request Context Model

### Tracking

- Status: partial
- Owner: unassigned
- Started: 2026-03-19
- Last Updated: 2026-03-19
- Exit Gate: request shaping consumes a richer internal context surface instead of RequestKind-only heuristics
- Open Risks: current snapshot models destination/site/mode, but initiator and frame context still remain outside the shaping surface

### Why

Текущий RequestKind слишком coarse-grained.

### Main Gap

Слишком много browser behavior сейчас выводится из одной enum plus URI heuristics.

### Current Progress

- Введен internal request context snapshot для active H1 path.
- Request shaping для Accept, priority и sec-fetch-* теперь опирается на snapshot, а не пересчитывает destination/mode в нескольких местах.
- Поверх существующего HttpsRequestMessage.Context добавлен typed browser request context override для destination, fetch mode и user-activated navigation semantics.
- End-to-end HttpsClientHandler coverage now exercises explicit iframe, worker and sharedworker contexts on the actual send path.
- Browser request context now also supports weaker semantic hints: top-level vs nested navigation and initiator type can be mapped into iframe/worker/sharedworker shaping even without an explicit destination override.
- End-to-end HttpsClientHandler coverage now also validates those inferred hints directly, not just explicit overrides.
- Service worker shaping is now destination/context-driven as well: same-origin site pinning, `Service-Worker: script`, and low-priority semantics no longer require `RequestKind.ServiceWorker` specifically when richer context is available.
- Browser request context now also models explicit reload navigation semantics, allowing top-level document reloads to suppress `sec-fetch-user` without collapsing back to iframe or non-navigation heuristics.
- Browser request context now also carries explicit form-submission intent through the internal snapshot, so the missing `isFormSubmission` Phase 2 surface exists in runtime state even before dedicated wire semantics are attached to it.
- Form-submission intent is now also wired into navigation Origin semantics: unsafe navigation still defaults to browser-like Origin emission, but explicit `IsFormSubmission = false` suppresses Origin on the navigation path without disturbing the rest of the navigation fetch metadata.
- Negative coverage around that new surface now also includes the `NoReferrer` combination: explicit `IsFormSubmission = false` plus `no-referrer` suppresses both `Referer` and `Origin` while preserving the navigation fetch metadata, so the opt-out path is covered under both policy and non-policy suppression.
- Explicit form-submission intent now also affects default user-activation semantics for iframe navigation: when callers mark an iframe navigation as a form submission and do not override `IsUserActivated`, the active H1 path now emits `sec-fetch-user: ?1` while preserving iframe destination semantics.
- Negative coverage now also pins explicit user-activation override precedence for iframe form submission: `IsFormSubmission = true` no longer forces `sec-fetch-user` when callers explicitly set `IsUserActivated = false`, so the new default remains opt-in only through absence of an override.
- Navigation policy coverage now also pins the downgrade interaction for explicit form submission: `strict-origin-when-cross-origin` suppresses the downgraded `Referer`, but unsafe navigation with `IsFormSubmission = true` still retains `Origin`, keeping policy trimming decoupled from required form-origin semantics.
- Внешний API не изменен; RequestKind по-прежнему работает как coarse fallback.

### Tasks

- Ввести internal request context snapshot, не ломая внешний API сразу.
- Добавить поля:
  - destination;
  - initiator type;
  - top-level vs nested navigation;
  - isFormSubmission;
  - credentials mode;
  - isReload;
  - isPrerender/prefetch if modeled;
  - referrer policy;
  - frame/site context.
- Превратить RequestKind в coarse fallback, а не единственный источник смысла.
- Для preload перестать полагаться только на extension sniffing, когда контекст известен явно.

### Tests

- internal context mapping tests;
- request shaping tests на одинаковый URI при разном initiator context;
- regression tests на backward compatibility с текущим API.

### Exit Criteria

- Основные request headers зависят от реального request context, а не только от грубого типа запроса.

---

## Phase 3. Full Fetch Metadata Matrix

### Tracking

- Status: partial
- Owner: unassigned
- Started: 2026-03-19
- Last Updated: 2026-03-20
- Exit Gate: sec-fetch-* matches references across the main destination matrix
- Open Risks: current matrix still depends on URI inference whenever richer initiator context is unavailable

### Why

sec-fetch-* сейчас уже неплох, но все еще не покрывает полную browser matrix.

### Current Progress

- Preload destination coverage now includes style, image, font, audio, video, track and manifest.
- Same-site registrable-domain wire coverage is now explicit in Https11Connection tests.
- Referer/Origin wire coverage now explicitly checks the no-referrer-policy split where Referer is suppressed but Origin remains required.
- The same URI can now produce different fetch metadata when explicit request context is provided, including nested iframe navigation and manifest preload without extension sniffing.
- Cross-scheme downgrade behavior is now covered both on low-level wire tests and on the real HttpsClientHandler send path, confirming that Referer is suppressed while Origin remains when policy requires it.
- Navigation fetch metadata now distinguishes top-level document reloads from user-activated navigations: reload keeps `navigate/document` semantics but suppresses `sec-fetch-user` through explicit request context instead of broad fallback changes.
- Origin/Referer negative coverage now also locks the same-origin safe-fetch `no-referrer` case explicitly: when policy suppresses the referrer on a safe same-origin fetch, both `Referer` and `Origin` stay absent while fetch metadata remains `same-origin`.
- Scheme-aware negative coverage now also includes same-host cross-scheme safe fetch under `no-referrer`: `Referer` stays suppressed, `Origin` is retained, and `sec-fetch-site` remains `cross-site`, confirming that scheme boundaries still dominate site classification when policy removes the referrer.
- Destination-aware subresource fetch Accept defaults now reuse the validated style/image tables when rich context declares `style` or `image`, so explicit subresource fetch contexts no longer collapse to `*/*` on Chromium and Firefox.
- Explicit image fetch coverage is now split by browser family as well: Firefox and Chromium branches are both locked with exact-value low-level and end-to-end tests instead of relying only on preload coverage.
- Safari fallback coverage now matches those explicit fetch surfaces too: `style` and `image` fetch contexts are pinned to conservative `*/*` on both low-level and end-to-end paths, completing the current family matrix for explicit style/image fetch Accept behavior.
- Explicit script fetch Accept is now also pinned on the active H1 path: Chromium and Safari both keep the conservative `*/*` surface under rich `script` fetch context, with low-level and end-to-end tests ensuring future Phase 4 changes do not accidentally over-specialize script requests.
- Firefox script fetch coverage now matches Chromium and Safari as well, so the current explicit script fetch family matrix is fully locked across the three active browser families with exact-value low-level and end-to-end tests.
- Explicit `font`, `audio`, and `video` fetch contexts are now covered on the active H1 path too: current behavior remains conservative at `*/*`, but low-level and end-to-end tests now pin their `Accept` plus destination/mode metadata so later Phase 4 changes can specialize them safely.
- Local in-repo capture assets now confirm both current font/media branches: CSS `@font-face` keeps generic fetch `Accept: */*`, emits `Origin` on `font/cors`, and omits `Priority`, while secure media-element requests keep generic `Accept: */*`, add `Accept-Encoding: identity;q=1, *;q=0`, send `Range: bytes=0-`, and omit both `Origin` and `Priority` on `audio|video/no-cors`.
- Secure browser truth now also covers `link rel=modulepreload`: Chromium keeps `sec-fetch-dest: script`, switches from generic preload `no-cors` to `cors`, emits `Origin` even on same-origin GET, and still omits `Priority`, so the active H1 path now models modulepreload as a dedicated request family instead of treating it as generic preload.
- Secure browser truth now also confirms that iframe-initiated same-origin subresources keep their own fetch destinations: iframe navigation is `iframe/navigate`, but a script inside the iframe remains `script/no-cors` with no `Origin`, and iframe font preload remains `font/cors` with `Origin`. H1 now reflects that constraint by keeping initiator-only iframe inference navigation-scoped instead of flattening non-navigation requests into `sec-fetch-dest: iframe`.
- Secure browser truth now also covers the service worker module/import branch: module entry stays `serviceworker/same-origin` with `Service-Worker: script`, no `Origin`, and no `Priority`, while the imported dependency flips to `serviceworker/cors`, carries `Origin`, and still omits `Priority`.

### Tasks

- Дотянуть sec-fetch-mode для:
  - navigate;
  - no-cors;
  - cors;
  - same-origin;
  - websocket if modeled in HTTP layer.
- Дотянуть sec-fetch-dest для:
  - document;
  - iframe;
  - script;
  - style;
  - image;
  - font;
  - audio;
  - video;
  - track;
  - manifest;
  - empty;
  - serviceworker;
  - sharedworker / worker where applicable.
- Дотянуть sec-fetch-user semantics только для реально user-activated navigation.
- Дотянуть family-specific omissions where relevant.

### Tests

- table-driven tests по destination matrix;
- wire tests для navigation, iframe, media, worker, font, image, API fetch;
- browser differential tests.

### Exit Criteria

- sec-fetch-* совпадает с эталонами для основной матрицы request contexts.

---

## Phase 4. Accept / Accept-Encoding / Accept-Language Tables

### Tracking

- Status: partial
- Owner: unassigned
- Started: 2026-03-19
- Last Updated: 2026-03-20
- Exit Gate: Accept family headers match browser-family-specific references for major request contexts
- Open Risks: Accept-Encoding and locale surfaces remain under-modeled outside the currently closed Chromium worker-class, media-element, and modulepreload branches; broader family coverage still needs dedicated follow-up before any cross-browser Accept-Encoding claims.

### Why

Даже при правильном sec-fetch-dest браузеры отличаются по Accept family surfaces.

### Tasks

- Расширить Accept tables для:
  - navigation;
  - iframe/document;
  - fetch/json;
  - image;
  - style;
  - script;
  - font;
  - media.
- Ввести browser-family specific Accept tables.
- Доделать Accept-Encoding:
  - Chromium / Edge surface с zstd;
  - Firefox-specific values;
  - Safari-specific values.
- Отвязать Accept-Language от одной захардкоженной локали профиля.
- Добавить profile locale surface.

### Tests

- exact-value wire tests;
- serialization tests на q-values;
- family-specific diff tests.

### Exit Criteria

- Accept family headers совпадают по browser family и request context.

---

## Phase 5. Client Hints Surface

### Tracking

- Status: planned
- Owner: unassigned
- Started: not started
- Last Updated: 2026-03-20
- Exit Gate: Chromium-family client hints emitted by active path align with negotiated browser references
- Open Risks: current implementation only models a minimal hint subset

### Why

Сейчас моделируется только минимальный блок client hints.

### Tasks

- Добавить support для:
  - sec-ch-ua-full-version-list;
  - sec-ch-ua-arch;
  - sec-ch-ua-bitness;
  - sec-ch-ua-model;
  - sec-ch-ua-platform-version;
  - sec-ch-ua-wow64.
- Сделать их browser-family specific.
- Сделать emission request-context aware.
- По возможности подготовить архитектуру под Accept-CH negotiation.

### Tests

- UA-to-client-hints mapping tests;
- family-specific wire tests;
- negative tests на absence where browser omits hints.

### Exit Criteria

- Chromium-family client hints surface приближен к реальному browser output.

---

## Phase 6. Priority Model

### Tracking

- Status: partial
- Owner: unassigned
- Started: 2026-03-20
- Last Updated: 2026-03-20
- Exit Gate: priority values and presence align with browser references by destination and initiator context
- Open Risks: current values are still heuristic and Chromium-centric

### Why

Priority уже появился, но пока это coarse heuristic layer.

### Current Progress

- Secure trusted-host captures now pin Chromium-family worker and sharedworker script fetches to a no-`Priority` surface, and the active H1 path no longer emits speculative `Priority` for `worker` or `sharedworker` destinations.
- The same secure capture pass now covers both worker and sharedworker module-import branches: imported same-origin dependencies keep their worker-class destination (`worker` or `sharedworker`), switch to `sec-fetch-mode: cors`, carry `Origin`, and still omit `Priority`.
- Active H1 shaping now matches those measured branches for explicit `worker + cors` and `sharedworker + cors` contexts, and the focused H1 plus WebDriver regression slices stay green after the change.

### Tasks

- Снять browser reference table для H1 priority by destination.
- Уточнить значения для:
  - document;
  - render-blocking CSS;
  - image;
  - font;
  - script;
  - async/deferred script;
  - fetch/json;
  - background API calls;
  - service worker.
- Сделать family-specific presence/absence rules.
- Увязать priority не только с destination, но и с initiator context.

### Tests

- exact-value tests;
- order-and-casing tests;
- browser diff snapshots.

### Exit Criteria

- priority values соответствуют reference captures хотя бы для Chromium family.

---

## Phase 7. Browser Family Divergence Layer

### Tracking

- Status: planned
- Owner: unassigned
- Started: not started
- Last Updated: 2026-03-20
- Exit Gate: same request context yields distinct but correct surfaces for Chrome, Edge, Firefox and Safari
- Open Risks: non-Chromium references are not yet encoded into runtime tables

### Why

Сейчас H1 path сильнее всего приближен к Chromium/Edge.

### Tasks

- Отдельно задокументировать request surface differences для:
  - Chrome;
  - Edge;
  - Firefox;
  - Safari.
- Ввести explicit family-specific tables для:
  - header presence;
  - default Accept;
  - Accept-Encoding;
  - Accept-Language;
  - client hints;
  - Priority;
  - Connection behavior;
  - service worker and preload nuances.
- Запретить Chromium-only defaults leaking into Firefox/Safari profiles.

### Tests

- profile-differentiation tests;
- side-by-side browser-family wire tests.

### Exit Criteria

- Один и тот же request context выдает различимый, но корректный wire image для каждой browser family.

---

## Phase 8. Redirect / Cookie / Conditional Request Chains

### Tracking

- Status: planned
- Owner: unassigned
- Started: not started
- Last Updated: 2026-03-20
- Exit Gate: redirect and cache-related chains behave like browser references end-to-end
- Open Risks: chain behavior depends on policy and context layers not yet completed

### Why

Полная мимикрия определяется не одним request, а цепочкой поведения.

### Tasks

- Выровнять redirect header mutation rules:
  - Referer update;
  - Origin retention/drop;
  - Authorization forwarding behavior where applicable;
  - method rewrite cases.
- Дотянуть cookie jar behavior на browser-like chains.
- Дотянуть conditional requests:
  - If-Modified-Since;
  - If-None-Match;
  - cache validators.

### Tests

- multi-step redirect tests;
- cookie continuity tests;
- cache validator tests;
- chain differential tests against real browsers.

### Exit Criteria

- Поведение request chains совпадает с browser references на основных сценариях.

---

## Phase 9. Authentication / Proxy / Challenge Surface

### Tracking

- Status: planned
- Owner: unassigned
- Started: not started
- Last Updated: 2026-03-20
- Exit Gate: auth/proxy behavior no longer exposes obvious non-browser request shaping on supported branches
- Open Risks: parts of the proxy path are intentionally not connected in the minimal handler path

### Why

Браузерный network stack отличим по challenge flows и proxy behavior.

### Tasks

- Дотянуть auth challenge behavior там, где он присутствует в active path.
- Дотянуть proxy request shaping и CONNECT surface.
- Зафиксировать, какие части intentionally unsupported.

### Tests

- auth challenge tests;
- proxy path wire tests;
- negative tests on unsupported branches.

### Exit Criteria

- Нет явных небраузерных скачков при proxy/auth handshakes на H1 path.

---

## Phase 10. TLS 1.3 And ClientHello Parity

### Tracking

- Status: planned
- Owner: unassigned
- Started: not started
- Last Updated: 2026-03-20
- Exit Gate: active transport path reaches practical browser-family TLS fingerprint parity
- Open Risks: current custom runtime is still TLS 1.2-only on the active path

### Why

Это обязательный слой для реальной parity с современными браузерами.

### Main Gap

Сейчас активный custom path по сути TLS 1.2 only.

### Tasks

- Ввести активный TLS 1.3 path.
- Снять и воспроизвести browser-family ClientHello surfaces:
  - extension order;
  - supported versions;
  - key share behavior;
  - signature algorithms;
  - cipher suites;
  - session resumption behavior;
  - ALPN negotiation.
- Дотянуть family-specific TLS divergence.

### Tests

- CreateTlsSettings unit tests;
- handshake capture tests;
- JA3/JA4-style diff harness;
- browser reference comparisons.

### Exit Criteria

- TLS fingerprint больше не выдает кастомный runtime как явно не-браузерный на основных browser families.

---

## Phase 11. HTTP/2 And HTTP/3 Reality Layer

### Tracking

- Status: planned
- Owner: unassigned
- Started: not started
- Last Updated: 2026-03-20
- Exit Gate: protocol selection and active runtime behavior align with modern browser reality
- Open Risks: H2/H3 code exists in the repository but is not the active parity path today

### Why

Без этого нельзя честно говорить о 100% мимикрии под современные браузеры.

### Tasks

- Реально включить active H2 path.
- Реально включить active H3 path.
- Снять browser-family settings surfaces:
  - H2 SETTINGS;
  - priority behavior;
  - pseudo-header ordering;
  - HPACK/QPACK dynamics;
  - H3 transport behavior.
- Дотянуть ALPN-driven path selection.

### Tests

- H2/H3 protocol-level tests;
- browser differential capture tests;
- fallback tests H3 -> H2 -> H1.

### Exit Criteria

- Активный runtime выбирает тот же protocol family и ведет себя по нему похоже на browser reference.

---

## Phase 12. Stateful Browser Network Model

### Tracking

- Status: planned
- Owner: unassigned
- Started: not started
- Last Updated: 2026-03-20
- Exit Gate: session continuity and stateful browser policy behavior no longer expose custom runtime artifacts
- Open Risks: depends on nearly all prior phases and on a stable state model

### Why

Это последний и самый тяжелый слой, без которого всегда останется gap между “хорошая имитация” и “почти настоящий браузер”.

### Tasks

- Ввести policy container / navigation context.
- Смоделировать inheritance rules для referrer policy.
- Смоделировать storage/cookie partitioning where required.
- Смоделировать connection reuse behavior как часть browser session state.
- Добавить stateful request scheduler / priority evolution if required.

### Tests

- multi-tab / multi-context tests;
- session continuity tests;
- stateful browser differential tests.

### Exit Criteria

- Поведение стека похоже не только на один request snapshot, но и на browser session over time.

## Recommended Immediate Sequence

Если двигаться по одному слою за раз, рекомендуемая очередь такая:

1. Referrer-Policy layer.
2. Rich request context model.
3. Full fetch metadata matrix.
4. Accept / Accept-Encoding / Accept-Language tables.
5. Client Hints surface.
6. Priority table refinement.
7. Firefox / Safari divergence pass.
8. Redirect / cookie / conditional chains.
9. TLS 1.3 / ClientHello parity.
10. Active H2/H3 parity.

## Recommended Next Task Queue

Если идти совсем маленькими шагами, ближайшая очередь задач может выглядеть так:

1. Ввести internal referrer policy enum и default strict-origin-when-cross-origin semantics.
2. Разнести Referer trimming и Origin emission по одной общей policy pipeline.
3. Вынести request destination в отдельный internal concept, не зависящий только от RequestKind.
4. Расширить preload matrix до script, font, style, image, audio, video с отдельными tests на каждый case.
5. Ввести browser-family specific Accept-Encoding tables.
6. Расширить Chromium client hints beyond minimal trio.
7. Привязать Priority не только к destination, но и к initiator context.

## Minimal Task Template For Each Iteration

Каждая новая итерация должна оформляться одинаково:

### Scope

- Какой один конкретный gap закрываем.

### Browser Reference

- Что делают Chrome, Edge, Firefox, Safari.

### Runtime Surface

- Какие именно active files меняются.

### Tests

- Какие pure logic tests добавляем.
- Какие wire tests добавляем.
- Какие regression tests должны остаться зелеными.

### Exit Gate

- Какое наблюдаемое поведение должно совпасть после правки.

### Notes

- Какие части intentionally не закрывались в этой итерации.
- Какие новые различия были обнаружены в browser diff.
- Нужно ли обновить next queue.

## Suggested File Ownership

Основные active files для ближайших фаз:

- [Framework/Atom.Net/Https/HttpsClientHandler.cs](Framework/Atom.Net/Https/HttpsClientHandler.cs)
- [Framework/Atom.Net/Https/HttpsRequestMessage.cs](Framework/Atom.Net/Https/HttpsRequestMessage.cs)
- [Framework/Atom.Net/Https/Connections/Https11Connection.cs](Framework/Atom.Net/Https/Connections/Https11Connection.cs)
- [Framework/Atom.Net/Https/Profiles/BrowserProfileCatalog.cs](Framework/Atom.Net/Https/Profiles/BrowserProfileCatalog.cs)
- [Framework/Atom.Net/Https/Headers/FormattingPolicies/HeadersFormattingPolicy.cs](Framework/Atom.Net/Https/Headers/FormattingPolicies/HeadersFormattingPolicy.cs)
- [Tests/Atom.Net.Tests/Https/HttpsClientHandlerTests.cs](Tests/Atom.Net.Tests/Https/HttpsClientHandlerTests.cs)
- [Tests/Atom.Net.Tests/Https/Https11ConnectionTests.cs](Tests/Atom.Net.Tests/Https/Https11ConnectionTests.cs)
- [Tests/Atom.Net.Tests/Https/BrowserProfileResolverTests.cs](Tests/Atom.Net.Tests/Https/BrowserProfileResolverTests.cs)

Дополнительные ожидаемые зоны расширения по мере движения roadmap:

- [Framework/Atom.Net/Https/RequestKind.cs](Framework/Atom.Net/Https/RequestKind.cs)
- [Framework/Atom.Net/Https/Profiles/BrowserHeaderProfile.cs](Framework/Atom.Net/Https/Profiles/BrowserHeaderProfile.cs)
- [Framework/Atom.Net/Tls/TlsSettings.cs](Framework/Atom.Net/Tls/TlsSettings.cs)
- [Framework/Atom.Net/Tls/Tls12Stream.cs](Framework/Atom.Net/Tls/Tls12Stream.cs)
- будущий активный HTTP/2 runtime path;
- будущий активный HTTP/3 runtime path.

## Anti-Pattern List

Ниже список того, чего нужно избегать.

- Не закрывать несколько policy layers одним большим patch.
- Не добавлять heuristics без wire-level tests.
- Не путать Chromium-like parity с browser-family parity.
- Не считать H1-only parity равной полной browser parity.
- Не менять dead H2/H3 code вместо active path.
- Не добавлять новые surface values без проверки их порядка и casing.
- Не добавлять флапающие integration tests без bounded timeouts.

## Final Success Condition

Эта задача может считаться реально завершенной только когда:

- H1 request wire image совпадает с browser references по browser family и request context;
- TLS fingerprint совпадает на practical detection surface;
- protocol selection H1/H2/H3 похожа на реальные браузеры;
- navigation/fetch/preload/service worker semantics совпадают по policy behavior;
- request chains и stateful session behavior больше не выдают кастомный runtime.

До этого момента корректнее считать текущую работу не “100% мимикрией”, а поэтапным приближением к browser parity.
