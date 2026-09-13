# AGENTS.md — WebParser

## Проект
Консольный **.NET (net10.0)** парсер: скачивает HTML-страницы, извлекает видимый текст, считает частоту слов. Цель эпика — выдержать нагрузку ~100 000 сайтов/день (≈1.2 req/s в среднем, проектируем с запасом на пики).

## Стек
- C# (records, init-свойства, `ObjectDisposedException.ThrowIf`), `net10.0`.
- Единственный внешний пакет: `HtmlAgilityPack` 1.13.0.
- HTTP: BCL `System.Net.Http.HttpClient`.

## Структура
- `Program.cs` — точка входа: чтение URL, валидация, вызов `ParseAsync`, `Console.ReadKey()`.
- `Parser/IContentParser.cs`, `Parser/ContentParser.cs` — парсинг одной страницы.
- `Logger/ILogger.cs`, `ILogMessage.cs`, `LogMessage.cs`, `DisposableFileLogger.cs` (используется), `FileLogger.cs` (мёртвый код).
- Конфигурационных файлов нет, всё захардкожено.

## Конвенции
- Без комментариев в коде, если не просили.
- UI-строки на русском, как в существующем коде.
- Интерфейсы для ключевых компонентов (`ILogger`, `IContentParser`).
- Namespaces: `WebParser.Parser`, `WebParser.Logger`.

## Команды
- Сборка: `dotnet build`
- Запуск: `dotnet run`
- Тесты: **сейчас отсутствуют**; после этапа 6 — `dotnet test`

## Известные проблемы (фон для работы)
- Двойная материализация HTML в память (`ReadToEndAsync` + `HtmlDocument`).
- `ToUpperInvariant` + `Split` + `GroupBy` — лишние аллокации; склейка слов между блочными тегами (`InnerText`).
- `AutoFlush = true` на каждую запись лога; логгер не утилизируется.
- `GetEncoding` не ловит `NotSupportedException`; нет fallback на `<meta charset>`.
- `FileLogger` открывает StreamWriter на каждый `Log` — мёртвый код, удалить.
- Параллелизма нет: один URL на запуск, без retry/politeness/лимитов.

## Статус эпика и порядок работ
См. **EPIC.md**. Одна сессия = один этап. По завершении обновить статусы в EPIC.md,
собрать (`dotnet build`), при наличии тестов — прогнать, закоммитить с упоминанием этапа.
Старт новой сессии: «Продолжи реализацию эпика — см. AGENTS.md и EPIC.md, возьми следующий pending-этап».