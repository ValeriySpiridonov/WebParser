# AGENTS.md — WebParser

## Проект
Консольный **.NET (net10.0)** парсер: скачивает HTML-страницы, извлекает видимый текст, считает частоту слов. Цель эпика — выдержать нагрузку ~100 000 сайтов/день (≈1.2 req/s в среднем, проектируем с запасом на пики).

## Стек
- C# (records, init-свойства, `ObjectDisposedException.ThrowIf`, nullable + implicit usings включены), `net10.0`.
- Внешних пакетов нет (самописный стриминговый HTML-разбор; конфиг — `System.Text.Json` из BCL).
  Единственное исключение — тестовый проект `WebParser.Tests` (xUnit): пакеты только там.
- HTTP: BCL `System.Net.Http.HttpClient`.

## Структура
- `Program.cs` — точка входа: конфиг из `appsettings.json` (см. `WebParser.Config`), чтение списка URL
  (файл/аргумент или stdin), `Parallel.ForEachAsync` по `MaxConcurrency`, Ctrl+C → graceful shutdown
  (стоп планирования, текущие запросы доживают до 5 с, затем жёсткая отмена), прогресс-цикл (`\r`,
  не-verbose), итоговая строка.
- `Config/AppOptions.cs`, `Config/ConfigLoader.cs`, `Config/LogLevelConverter.cs` — POCO опции (Crawler/Logging),
  загрузка + кламп; конвертер `LogLevel` терпим к опечаткам/числам (fallback → `Info`).
- `Stats/RunStats.cs` — метрики: обработано / ошибок / RPS / среднее время / прошедшее время.
- `Parser/IContentParser.cs`, `Parser/ParseResult.cs`, `Parser/ContentParser.cs` — парсинг одной страницы:
  retry (из опций, backoff+jitter, `Retry-After`), per-host лимит (из опций), лимит размера страницы (из
  опций), charset из заголовка + `<meta charset>`, стриминговое чтение тела без буферизации в `byte[]`;
  `ParseAsync` возвращает `Task<ParseResult>`; OCE перевыбрасывается только при реальной отмене токена,
  сетевой таймаут даёт `Failed`; `ContentParser : IDisposable` (владеет `HttpClient`).
- `Parser/HtmlWordCounter.cs` — стриминговый single-pass разбор текста (state machine, без DOM): блок-разделители, пропуск script/style/textarea, комментарии, entities; `WordFrequency`.
- `Parser/PageTooLargeException.cs` — выбрасывается при превышении лимита страницы.
- `Logger/ILogger.cs`, `ILogMessage.cs`, `LogMessage.cs` (`required Message`, `Exception?`, `Level`), `AsyncFileLogger.cs` — Channel-логгер из опций (ротация по размеру, уровни). `LogLevel` определён в `ILogMessage.cs`.
- `WebParser.Tests/` (xUnit) — юнит-тесты `HtmlWordCounter`/`ConfigLoader`/`RunStats`/`AsyncFileLogger`,
  интеграционные `ContentParser` + `StubHttpServer` (локальный `TcpListener`-сервер), бенчмарк-прогон;
  helpers: `TestLogger`, `NullLogger`, `StubHttpServer`. Основной csproj исключает `WebParser.Tests\**` из компиляции.
- `appsettings.json` — шаблон конфигурации (факультатив; отсутствие файла → дефолты).

## Конвенции
- Без комментариев в коде, если не просили.
- UI-строки на русском, как в существующем коде.
- Интерфейсы для ключевых компонентов (`ILogger`, `IContentParser`).
- Namespaces: `WebParser.Parser`, `WebParser.Logger`, `WebParser.Config`, `WebParser.Stats`.

## Команды
- Сборка: `dotnet build` (должна быть без предупреждений)
- Запуск: `dotnet run -- <файл со списком URL>` или ввод из stdin; `--verbose`/`-v` — вывод слов в консоль и в лог.
- Тесты: `dotnet test` (проект `WebParser.Tests`, xUnit)

## Статус
Эпик завершён (этапы 1–6 + review-проход, см. **EPIC.md** как историю работ). Новый код — по
запросам пользователя; каждая завершённая задача требует: собрать (`dotnet build`, 0 предупреждений),
при наличии тестов — прогнать (`dotnet test`), по просьбе пользователя закоммитить и дополнить журнал
в `EPIC.md`.