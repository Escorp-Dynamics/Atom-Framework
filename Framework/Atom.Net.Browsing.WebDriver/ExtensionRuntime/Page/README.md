# Page

Page слой остаётся второй фазой и пока не должен расползаться в transport contracts.

Его будущая зона ответственности:

- main-world hooks для callback и диагностических прокси
- безопасная установка page-side bridge emitter
- изоляция потенциально опасных override и guard-политик

До подключения live bootstrap этот слой не должен ломать уже стабилизированный staged contract.
