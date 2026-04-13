# DOM/BOM Spec Sync Checklist

## Назначение

Этот checklist используется при каждом следующем sync pass по Web IDL и browser-facing specification surface.

Он нужен для того, чтобы повторная сверка не начиналась с нуля и не смешивала source collection, policy review и generation planning.

## 1. Source Collection

- [ ] Проверить, появились ли новые normative families или новые subsection owners в WHATWG, W3C, CSSWG, SVGWG и смежных спецификациях.
- [ ] Проверить, не сменился ли canonical owner уже inventoried surface.
- [ ] Проверить, не появились ли новые partial interface, mixin, callback, enum, dictionary или typedef, влияющие на public signatures.
- [ ] Для новых или изменившихся family обновить inventory, а не roadmap напрямую.

## 2. Provenance Review

- [ ] Для каждой новой family определить primary owner specification.
- [ ] Если одна сущность теперь определяется через несколько partial sources, обновить provenance notes для merge strategy.
- [ ] Если surface retired, obsolete или legacy-only, зафиксировать это явно как compatibility bucket.
- [ ] Если surface namespace-based, callback-heavy или union-heavy, пометить как review-gated, если current policy еще не покрывает его безопасно.

## 3. Policy Consistency

- [ ] Проверить, не нарушает ли новый surface уже принятые правила для mixin, partial interface, supporting types, callback delegates и deferred buckets.
- [ ] Проверить, не тянет ли deferred family свои supporting types в modern first pass транзитивно.
- [ ] Проверить, не возникли ли новые naming exceptions, которые конфликтуют с current Html, Svg, Css, Url, Xml, MathMl casing policy.
- [ ] Если policy не хватает, сначала обновить policy document, потом roadmap и только затем generation planning.

## 4. Roadmap Alignment

- [ ] Добавить в roadmap только те family и contract buckets, которые уже закрыты в inventory.
- [ ] Удалить или переписать stale roadmap wording, если решение уже принято в policy или inventory.
- [ ] Проверить, что roadmap не содержит ложных spec-shaped types без подтвержденной provenance.
- [ ] Проверить, что first-pass modern baseline и deferred buckets остаются согласованными.

## 5. Supporting Types

- [ ] Проверить, нужны ли новые enums, dictionaries, delegates или payload contracts.
- [ ] Если нужны, синхронизировать их с supporting types policy document.
- [ ] Не создавать alias-like typedef types без отдельного explicit decision.
- [ ] Не размывать closed vocabularies в string или object без причины.

## 6. Documentation Hygiene

- [ ] Проверить markdown diagnostics в inventory, roadmap и связанных reference docs.
- [ ] Убедиться, что новые reference docs не дублируют inventory, а суммируют synchronization state.
- [ ] Обновить spec-sync reference doc snapshot status, если baseline или deferred buckets изменились.

## 7. Freeze Decision

- [ ] Явно ответить, меняется ли first-pass scope.
- [ ] Если scope не меняется, обновить только provenance and checklist state.
- [ ] Если scope меняется, сначала зафиксировать новую boundary в roadmap, затем обновить supporting-type policy и spec-sync reference doc.
- [ ] Только после этого переходить к layout or generation work.
