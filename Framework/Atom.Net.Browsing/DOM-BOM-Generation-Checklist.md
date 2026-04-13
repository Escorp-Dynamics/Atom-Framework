# DOM/BOM Generation Checklist

## Назначение

Этот checklist используется прямо перед первым physical file creation pass.

Он нужен, чтобы не возвращаться к policy decisions в момент генерации интерфейсов.

## 1. Scope check

- [ ] Подтвердить, что выбранный batch already belongs to the frozen first-pass modern baseline.
- [ ] Подтвердить, что batch не затягивает deferred compatibility, worker or Fetch-owned surface.
- [ ] Подтвердить, что owning folder уже определен в file map.

## 2. File planning

- [ ] Для каждого entity определить один target file.
- [ ] Для supporting types отдельно определить delegate, enum and dictionary files.
- [ ] Не создавать split interfaces ради provenance.
- [ ] Не смешивать несколько public entities в одном файле.

## 3. Naming check

- [ ] Проверить I-prefix only for interfaces.
- [ ] Проверить Html, Css, Svg, Url, Xml, MathMl casing.
- [ ] Проверить, что root namespace stays DOM or BOM according to the owning family.

## 4. Signature mapping check

- [ ] attribute -> property
- [ ] readonly attribute -> get-only property
- [ ] operation -> method
- [ ] callback -> named delegate
- [ ] dictionary -> dedicated supporting contract when needed
- [ ] enum -> dedicated enum when the source defines a closed vocabulary
- [ ] typedef -> mapped CLR type unless a later explicit wrapper decision exists

## 5. Partial merge check

- [ ] If batch touches Window or Navigator, use the accepted partial merge notes.
- [ ] Do not widen partial merge pressure beyond the current batch.
- [ ] Keep provenance in notes or XML documentation, not in folder splitting.

## 6. Validation check

- [ ] Validate edited docs or generated files for diagnostics.
- [ ] Confirm that README and planning docs still describe the current execution order correctly.
- [ ] Confirm that next batch prerequisites remain unchanged.
