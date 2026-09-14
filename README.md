# WebParser

Консольный **.NET (net10.0)** парсер: пакетно скачивает HTML-страницы, извлекает видимый текст
и считает частоту слов. Проектировался под нагрузку ~100 000 сайтов/день (~1.2 req/s в среднем)
с запасом на пики; внешних пакетов в основном проекте нет.

## Возможности
- Пакетная обработка списка URL с ограниченным параллелизмом (`Parallel.ForEachAsync`).
- Retry с backoff+jitter и уважением `Retry-After`; таймауты; per-host ограничение конкурентности.
- Стриминговый single-pass разбор HTML (state machine, без DOM); charset: заголовок → `<meta charset>` → UTF-8.
- Лимит размера страницы; декомпрессия GZip/Deflate/Brotli.
- Асинхронный логгер (`Channel`) с ротацией по размеру, уровнями и буферизованной записью.
- Конфигурация через `appsettings.json` (факультативно, отсутствие файла → дефолты).
- Метрики: обработано / ошибок / RPS / среднее время; прогресс-цикл; graceful shutdown по Ctrl+C.

## Сборка и тесты
```
dotnet build    # 0 предупреждений
dotnet test     # 55 тестов (xUnit, включая интеграционные и бенчмарк)
```

## Запуск
```
dotnet run -- <файл со списком URL>
dotnet run -- --verbose <файл со списком URL>   # или -v
```
Без аргумента список URL читается из stdin по одной строке на URL.
`--verbose`/`-v` — вывод слов в консоль и в лог; в обычном режиме консоль показывает прогресс (`\r`)
и итоговую строку с метриками.

## Конфигурация
Шаблон — `appsettings.json` в корне:

- `crawler` — `maxConcurrency`, `maxAttempts`, `maxConcurrentPerHost`, `maxPageSizeBytes`,
  `timeoutSeconds`, `retryBaseDelayMs`, `retryAfterCapSeconds`, `userAgent`;
- `logging` — `filePath`, `maxFileSizeBytes` (ротация `log.txt` → `log.txt.old`), `queueCapacity`,
  `minLevel` (терпим к опечаткам, fallback → `Info`).

## Производительность
Бенчмарк на локальном стенде (`WebParser.Tests`): **500 000 URL ≈ 9 c**, ~53 000 req/s,
рост managed-памяти ≈48 МБ — целевые ~100 000 сайтов/день перекрыты на порядки.