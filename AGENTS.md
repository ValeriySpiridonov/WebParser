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
- `Program.cs` — точка входа: разбор аргументов (`CliArgs`), конфиг из `appsettings.json` (см. `WebParser.Config`),
  композиция сервисов в `ServiceCollection` (composition root: `ILogger`, `IHttpFetcher`, `IContentParser`),
  Ctrl+C → graceful shutdown (стоп планирования, текущие запросы доживают до 5 с, затем жёсткая отмена),
  итоговая строка.
- `CliArgs.cs` — аргументы командной строки: `--verbose`/`-v` в любом порядке, первый аргумент без дефиса —
  файл со списком URL; неизвестная опция → ошибка + exit code 1.
- `UrlSource.cs` — чтение списка URL (файл или stdin) с фильтрацией через `UrlValidator`.
- `CrawlRunner.cs` — `Parallel.ForEachAsync` по `MaxConcurrency`, запись метрик, verbose-вывод слов
  (консоль + лог через `WordFrequency.FormatLine`), возвращает факт прерывания пользователем.
- `ProgressReporter.cs` — прогресс-цикл (`\r`, не-verbose), останавливается через `Dispose`.
- `Config/AppOptions.cs`, `Config/CrawlerOptions.cs`, `Config/LoggingOptions.cs`, `Config/ConfigLoader.cs`, `Config/LogLevelConverter.cs` — POCO опции (Crawler/Logging),
  загрузка + кламп; конвертер `LogLevel` терпим к опечаткам/числам (fallback → `Info`).
- `Stats/RunStats.cs` — метрики: обработано / ошибок / RPS / среднее время / прошедшее время.
- `Parser/IContentParser.cs`, `Parser/ParseResult.cs`, `Parser/ParseOutcome.cs`, `Parser/ContentParser.cs` — оркестрация обработки
  одной страницы: валидация адреса (`UrlValidator`), per-host гейт (`HostRateLimiter`), лимит размера страницы (из опций),
  стриминговое чтение тела без буферизации в `byte[]`; `ParseAsync` возвращает `Task<ParseResult>`; OCE
  перевыбрасывается только при реальной отмене токена, сетевой таймаут даёт `Failed`;
  единственный конструктор — с инжектированными зависимостями, владение фетчером — у его владельца
  (DI-контейнер или тестовый `ParserScope`).
- `Parser/UrlValidator.cs` — единая проверка «строка — полный HTTP(S) адрес» (используется и парсером, и `UrlSource`).
- `Parser/IHttpFetcher.cs`, `Parser/HttpFetcher.cs` — скачивание страницы с повторами из опций
  (backoff+jitter, `Retry-After`); `IDisposable` — на конкретном `HttpFetcher`, не на интерфейсе
  (синглтон освобождает DI-контейнер).
- `Parser/HostRateLimiter.cs`, `Parser/IHostRateLimiter.cs` — per-host лимит одновременных операций
  (семафор на хост из опций) с вычисткой простаивающих хостов: запись хоста помечается на удаление
  только из состояния `RefCount == 0` (удерживаемые/ожидаемые хосты не вычищаются).
- `Parser/CharsetDetector.cs` — определение кодировки из заголовка Content-Type и `<meta charset>`.
- `Parser/BoundedReadStream.cs` — стрим, ограничивающий суммарный объём чтения (`PageTooLargeException`).
- `Parser/HtmlWordCounter.cs` — стриминговый single-pass разбор текста (state machine, без DOM): блок-разделители, пропуск script/style/textarea, комментарии, entities; разделители — `char.IsWhiteSpace/IsPunctuation/IsSymbol` (в т.ч. NBSP); `WordFrequency` — в `Parser/WordFrequency.cs` (+ `FormatLine`).
- `Parser/PageTooLargeException.cs` — выбрасывается при превышении лимита страницы.
- `Logger/ILogger.cs`, `ILogMessage.cs`, `LogMessage.cs` (`required Message`, `Exception?`, `Level`), `AsyncFileLogger.cs` — Channel-логгер из опций (ротация по размеру, уровни; при переполнении очереди блокируются только Error, прочие отбрасываются с отчётом в errorSink). `LogLevel` — отдельный файл `LogLevel.cs`.
- `WebParser.Tests/` (xUnit) — юнит-тесты `HtmlWordCounter`/`ConfigLoader`/`RunStats`/`AsyncFileLogger`/`HostRateLimiter`/`CliArgs`,
  интеграционные `ContentParser` + `StubHttpServer` (локальный `TcpListener`-сервер), бенчмарк-прогон;
  helpers: `TestLogger`, `NullLogger`, `StubHttpServer`, `ParserScope` (парсер с собственным фетчером).
  Основной csproj исключает `WebParser.Tests\**` из компиляции; internals видны тестам через `InternalsVisibleTo`.
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