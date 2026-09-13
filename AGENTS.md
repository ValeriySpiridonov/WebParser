# AGENTS.md — WebParser

## Проект
Консольный **.NET (net10.0)** парсер: скачивает HTML-страницы, извлекает видимый текст, считает частоту слов. Цель эпика — выдержать нагрузку ~100 000 сайтов/день (≈1.2 req/s в среднем, проектируем с запасом на пики).

## Стек
- C# (records, init-свойства, `ObjectDisposedException.ThrowIf`, nullable + implicit usings включены), `net10.0`.
- Единственный внешний пакет: `HtmlAgilityPack` 1.13.0.
- HTTP: BCL `System.Net.Http.HttpClient`.

## Структура
- `Program.cs` — точка входа: чтение списка URL (файл/аргумент или stdin), валидация, `Parallel.ForEachAsync` с `MaxDegreeOfParallelism = 10`, Ctrl+C → отмена.
- `Parser/IContentParser.cs`, `Parser/ContentParser.cs` — парсинг одной страницы: retry (3 попытки, backoff+jitter, `Retry-After`), per-host лимит (2 одновременных), лимит размера страницы (5 МБ), charset из заголовка + `<meta charset>`.
- `Parser/PageTooLargeException.cs` — выбрасывается при превышении лимита страницы.
- `Logger/ILogger.cs`, `ILogMessage.cs`, `LogMessage.cs` (`required Message`, `Exception?`), `DisposableFileLogger.cs` (используется), `FileLogger.cs` (мёртвый код).
- Конфигурационных файлов нет, всё захардкожено.

## Конвенции
- Без комментариев в коде, если не просили.
- UI-строки на русском, как в существующем коде.
- Интерфейсы для ключевых компонентов (`ILogger`, `IContentParser`).
- Namespaces: `WebParser.Parser`, `WebParser.Logger`.

## Команды
- Сборка: `dotnet build` (должна быть без предупреждений)
- Запуск: `dotnet run -- <файл со списком URL>` или ввод из stdin
- Тесты: **сейчас отсутствуют**; после этапа 6 — `dotnet test`

## Известные проблемы (фон для работы)
- Двойная материализация HTML в память: HTML загружается в `byte[]`/строку, затем в `HtmlDocument` (этап 3).
- `ToUpperInvariant` + `Split` + `GroupBy` — лишние аллокации; склейка слов между блочными тегами (`InnerText` даёт `DOMAINTHIS`) (этап 3).
- `AutoFlush = true` на каждую запись лога; запись блокирующая (этап 4).
- `FileLogger` открывает StreamWriter на каждый `Log` — мёртвый код, удалить (этап 4).
- `HostSems` (per-host семафоры) растут на всю жизнь процесса без очистки (этап 5), в лог итоговые вещи дублируются в консоль (этап 4/5).

## Статус эпика и порядок работ
См. **EPIC.md**. Одна сессия = один этап. По завершении обновить статусы в EPIC.md,
собрать (`dotnet build`, 0 предупреждений), при наличии тестов — прогнать, закоммитить с упоминанием этапа.
Старт новой сессии: «Продолжи реализацию эпика — см. AGENTS.md и EPIC.md, возьми следующий pending-этап».