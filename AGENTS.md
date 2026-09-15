# AGENTS.md — WebParser

## Проект
Консольный **.NET (net10.0)** парсер: скачивает HTML-страницы, извлекает видимый текст, считает частоту слов. Цель эпика — выдержать нагрузку ~100 000 сайтов/день (≈1.2 req/s в среднем, проектируем с запасом на пики).

## Стек
- C# (records, init-свойства, `ObjectDisposedException.ThrowIf`, nullable + implicit usings включены), `net10.0`.
- Внешние пакеты сведены к минимуму: `Microsoft.Extensions.DependencyInjection` (DI-контейнер) в основном проекте;
  самописный стриминговый HTML-разбор; конфиг — `System.Text.Json` из BCL.
  В тестовом проекте `WebParser.Tests` (xUnit) пакеты разрешены.
- HTTP: BCL `System.Net.Http.HttpClient`.

## Структура
- `Program.cs` — точка входа: конфиг из `appsettings.json` (см. `WebParser.Config`), композиция сервисов
  в `ServiceCollection` (composition root: `ILogger`, `IHttpFetcher`, `IContentParser`), чтение списка URL
  (файл/аргумент или stdin), `Parallel.ForEachAsync` по `MaxConcurrency`, Ctrl+C → graceful shutdown
  (стоп планирования, текущие запросы доживают до 5 с, затем жёсткая отмена), прогресс-цикл (`\r`,
  не-verbose), итоговая строка.
- `Config/AppOptions.cs`, `Config/CrawlerOptions.cs`, `Config/LoggingOptions.cs`, `Config/ConfigLoader.cs`, `Config/LogLevelConverter.cs` — POCO опции (Crawler/Logging),
  загрузка + кламп; конвертер `LogLevel` терпим к опечаткам/числам (fallback → `Info`).
- `Stats/RunStats.cs` — метрики: обработано / ошибок / RPS / среднее время / прошедшее время.
- `Parser/IContentParser.cs`, `Parser/ParseResult.cs`, `Parser/ParseOutcome.cs`, `Parser/ContentParser.cs` — оркестрация обработки
  одной страницы: валидация адреса, per-host гейт (`HostRateLimiter`), лимит размера страницы (из опций),
  стриминговое чтение тела без буферизации в `byte[]`; `ParseAsync` возвращает `Task<ParseResult>`; OCE
  перевыбрасывается только при реальной отмене токена, сетевой таймаут даёт `Failed`;
  `ContentParser : IDisposable` — владеет HTTP-клиентом только если фетчер создан им самим, инжектированный фетчер освобождает вызывающий (DI-контейнер).
- `Parser/IHttpFetcher.cs`, `Parser/HttpFetcher.cs` — скачивание страницы с повторами из опций
  (backoff+jitter, `Retry-After`); `HttpFetcher` владеет `HttpClient`.
- `Parser/HostRateLimiter.cs`, `Parser/IHostRateLimiter.cs` — per-host лимит одновременных операций
  (семафор на хост из опций) с вычисткой простаивающих хостов.
- `Parser/CharsetDetector.cs` — определение кодировки из заголовка Content-Type и `<meta charset>`.
- `Parser/BoundedReadStream.cs` — стрим, ограничивающий суммарный объём чтения (`PageTooLargeException`).
- `Parser/HtmlWordCounter.cs` — стриминговый single-pass разбор текста (state machine, без DOM): блок-разделители, пропуск script/style/textarea, комментарии, entities; `WordFrequency` — в `Parser/WordFrequency.cs`.
- `Parser/PageTooLargeException.cs` — выбрасывается при превышении лимита страницы.
- `Logger/ILogger.cs`, `ILogMessage.cs`, `LogMessage.cs` (`required Message`, `Exception?`, `Level`), `AsyncFileLogger.cs` — Channel-логгер из опций (ротация по размеру, уровни). `LogLevel` — отдельный файл `LogLevel.cs`.
- `WebParser.Tests/` (xUnit) — юнит-тесты `HtmlWordCounter`/`ConfigLoader`/`RunStats`/`AsyncFileLogger`,
  интеграционные `ContentParser` + `StubHttpServer` (локальный `TcpListener`-сервер), бенчмарк-прогон;
  helpers: `TestLogger`, `NullLogger`, `StubHttpServer`. Основной csproj исключает `WebParser.Tests\**` из компиляции.
- `appsettings.json` — шаблон конфигурации (факультатив; отсутствие файла → дефолты).

## Конвенции
- Комментарии в коде — только для неочевидных мест, где у читателя может возникнуть вопрос «а почему так сделано?»; не комментировать очевидный код.
- Каждый top-level класс, интерфейс, структура и енам — в отдельном файле (приватные вложенные типы остаются в файле владельца).
- Все публичные типы и их публичные члены (методы, свойства, конструкторы и т.п.) снабжаются кратким xmldoc на русском языке.
- При реализации интерфейса с xmldoc не дублировать его дословно в классе — использовать `/// <inheritdoc />`; собственный xmldoc пишется только если описание поведения реализации отличается от контракта.
- UI-строки на русском, как в существующем коде.
- Интерфейсы для ключевых компонентов (`ILogger`, `IContentParser`, `IHttpFetcher`, `IHostRateLimiter`).
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