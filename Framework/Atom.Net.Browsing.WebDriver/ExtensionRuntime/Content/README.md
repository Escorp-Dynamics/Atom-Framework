# Content

Content слой живёт внутри вкладки и общается с background через persistent runtime.connect канал.

Reference runtime уже задаёт минимальный bootstrap path:

- discovery-документ читает atom-bridge-port и atom-bridge-secret из meta и отправляет configure в background
- после старта документа content заново подтягивает tab context и применяет override до пользовательских команд
- content публикует DomContentLoaded, PageLoaded, ScriptError и ConsoleMessage как tab-local события
- body override, main-world bridge и command dispatch остаются внутренней задачей content слоя
- executeInMain и mainWorldResult являются отдельной внутренней веткой обмена для main-world script path
