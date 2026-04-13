# DOM/BOM Layout Task List

## Назначение

Этот документ разбивает physical layout creation на пошаговые задачи.

Он предназначен для перехода от planning docs к созданию каталогов и первых contract files.

## Phase 1: Root layout

- [ ] Create DOM root folder.
- [ ] Create BOM root folder.
- [ ] Confirm namespace-to-folder mapping remains DOM <-> Atom.Net.Browsing.DOM and BOM <-> Atom.Net.Browsing.BOM.

## Phase 2: Early BOM folders

- [ ] Create BOM/Networking.
- [ ] Create BOM/Scheduling.
- [ ] Create BOM/Permissions.
- [ ] Create BOM/Navigator.
- [ ] Create BOM/Windowing.

## Phase 3: Early DOM folders

- [ ] Create DOM/Abort.
- [ ] Create DOM/Geometry.
- [ ] Create DOM/Observers.
- [ ] Create DOM/Events.
- [ ] Create DOM/Core.

## Phase 4: Extended DOM/BOM planning folders

- [ ] Create DOM/Traversal.
- [ ] Create DOM/Ranges.
- [ ] Create DOM/Selection.
- [ ] Create DOM/Html.
- [ ] Create DOM/Html/Elements.
- [ ] Create DOM/Forms.
- [ ] Create DOM/Media.
- [ ] Create DOM/Cssom.
- [ ] Create DOM/Cssom/View.
- [ ] Create DOM/Svg.
- [ ] Create DOM/MathMl.
- [ ] Create DOM/Clipboard.
- [ ] Create DOM/Fullscreen.
- [ ] Create BOM/History.
- [ ] Create BOM/Location.
- [ ] Create BOM/Navigation.
- [ ] Create BOM/Storage.
- [ ] Create BOM/Messaging.
- [ ] Create BOM/Performance.
- [ ] Create BOM/Timing.
- [ ] Create BOM/Screen.

## Phase 5: Pre-file checks

- [ ] Confirm the chosen code batch has a matching file map.
- [ ] Confirm supporting types for the chosen batch have their own target files.
- [ ] Confirm deferred buckets are not being materialized accidentally.
- [ ] Confirm README and planning docs still describe the chosen entry sequence.

## Suggested execution order

1. Root layout
2. Early BOM folders
3. BOM Url batch files
4. Early DOM folders
5. DOM Abort batch files
