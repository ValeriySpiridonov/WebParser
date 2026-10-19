# EPIC: WebParser под нагрузку ~100 000 сайтов/день

Текущий код: один URL на запуск, синхронный логгер, парсинг через HtmlAgilityPack с двойной материализацией HTML.

Целевое состояние: пакетный асинхронный конвейер с ограниченным параллелизмом, per-host politeness,
стриминговый парсинг с bounded-памятью, асинхронный буферизованный логгер, метрики и тесты.

## Статусы
- `[ ]` — не начат
- `[~]` — в работе
- `[x]` — сделан

---

## Этап 1. Пакетный конвейер вместо «один URL на запуск» `[x]`
**Задача:** координатор читает список URL (файл/stdin) и обрабатывает их с `SemaphoreSlim(maxConcurrency)`.
**Критерии:**
- Программа принимает список URL (не один) и прогоняет их через ограниченный параллелизм.
- Один общий `HttpClient` (сейчас static; оформить через `IHttpClientFactory` при необходимости).
- Аккуратный `using`/`Dispose` логгера.

## Этап 2. Async + politeness `[x]`
**Задача:** retry с backoff+jitter, `CancellationToken`, per-host rate limiter, лимит размера страницы.
**Критерии:**
- Параллельные воркеры не долбят один домен (лимит на `uri.Host`).
- Ограниченный retry (не «попробуйте позднее» в лог).
- Списывается «висящий» сайт по таймауту; Ctrl+C — graceful shutdown.
- Лимит размера ответа; `GetEncoding` ловит `NotSupportedException`, fallback на `<meta charset>`.

## Этап 3. Парсинг — память/CPU `[x]`
**Задача:** убрать двойную материализацию HTML, потоковый проход, `Dictionary<string,int>` вместо `GroupBy`.
**Критерии:**
- HTML не загружается в строку целиком перед парсингом (один проход, `ArrayPool`/`StringBuilder`).
- Нет `ToUpperInvariant` на всём тексте; пустые токены фильтруются до подсчёта.
- Исправлена склейка слов через блочные элементы (`InnerText` давал `DomainThis`).

## Этап 4. Логгер `[x]`
**Задача:** асинхронная очередь (`Channel`) + фоновый писатель, буферизация, ротация, уровни.
**Критерии:**
- Воркеры не блокируются на дисковом I/O; `AutoFlush` убран.
- Ротация по размеру/дате; детальные строки по словам — только во verbose-режиме.
- `FileLogger` (мёртвый код) удалён; остался один логгер.

## Этап 5. Управляемость `[x]`
**Задача:** конфиг (`appsettings.json` + Options), метрики/прогресс, graceful shutdown.
**Критерии:**
- Concurrency, таймауты, лимиты, пути логов настраиваются без пересборки.
- Прогресс: обработано / ошибок / RPS / среднее время.

## Этап 6. Тесты и замеры `[x]`
**Задача:** юнит-тесты парсинга + интеграционный тест с локальным HTTP-сервером + замеры.
**Критерии:**
- `dotnet test` зелёный: charsets, границы блоков, лимиты, retry.
- Замер прогона N URL (цель-ориентир: стабильная память, время идёт по I/O).

---
## Эпик завершён
Дата: 2026-09-14. Финальный коммит: `8e2534b` (последняя запись журнала — `5673174`).
Все этапы 1–6 закрыты, review-замечания отработаны. Файл служит историей работ; для дальнейших
задач см. `README.md` и `AGENTS.md`.

---

## Лог выполнения
- Этап 1 — чтение списка URL из файла (аргумент) или stdin (до пустой строки), ввод валидируется построчно;
  прогон через `Parallel.ForEachAsync` c `MaxDegreeOfParallelism = 10`, логгер обёрнут в `using`;
  коммит `4d3f7d2`.
- Этап 2 — `CancellationToken` в `ParseAsync` и `Parallel.ForEachAsync`, Ctrl+C отменяет обработку;
  retry 3 попытки (backoff 0.5с*2^n + jitter ±50%, уважение `Retry-After` c капом 30с, статусы 429/408/5xx);
  per-host лимит 2 одновременных через `SemaphoreSlim` по `uri.Host`; лимит страницы 5 МБ
  (`PageTooLargeException`, чтение с `ResponseHeadersRead`), charset: header → `<meta charset>` → UTF-8,
  `GetEncoding` ловит `ArgumentException` и `NotSupportedException`; включены `<Nullable>/<ImplicitUsings>`;
  коммит `a339af5`.
- Этап 3 — убран HtmlAgilityPack (0 внешних пакетов); новый `HtmlWordCounter` (state machine, single-pass):
  HTML декодируется из bounded `byte[]` чанками через `ArrayPool<char>` и кормится счётчику без сборки
  полной строки/DOM; lowercasing посимвольно (нет `ToUpperInvariant`); `Dictionary<string,int>` вместо
  `GroupBy`/`Split`; пустые токены не создаются; исправлена склейка слов — блочные теги вставляют
  разделитель, провалены script/style/textarea/комментарии, entities (nbsp/amp/числовые), голый `&`
  не глотает текст; стриминг устойчив к границам чанков; валидация 13+ сценариев через временный харнесс;
  финальная сортировка по убыванию частоты; коммит `05cb2c0`.
- Fix (после review, до этапа 4) — декомпрессия GZip/Deflate/Brotli + default User-Agent (`HttpClientHandler`),
  skip-режим: кавычки в JS-строке больше не «вешают» поиск `</script>` (доп. фикс: `<` внутри скана тега
  отменяет его), `PageTooLargeException(int)`, `ContentParser` → `sealed`, exit code 1 при ненайденном файле;
  terminal `throw` в ретраях оставлен (требуется компилятором); коммит `2a42962`.
- Этап 4 — новый `AsyncFileLogger` (`Channel<string>` bounded 10k, `FullMode.Wait`) + фоновый писатель
  с буферизованной записью (flush каждые 200 строк, никакого `AutoFlush`); воркеры не касаются дискового I/O;
  ротация по размеру 8 МБ (`log.txt` → `log.txt.old`); уровни `Verbose/Info/Error`, фильтрация на продуцентной
  стороне; строки по словам — только `LogLevel.Verbose`, флаг `--verbose`/`-v` открывает их (в дефолте лог
  перестал раздуваться построчно; устранён пер-адресный флуд «прервана» при Ctrl+C); `FileLogger` и
  `DisposableFileLogger` удалены — остался один логгер; ротация/запись — единственный
  читатель канала (без lock); коммит `e866242`.
- Fix (после review этапа 4) — канал держит `ILogMessage`, форматирование (вкл. `DateTime.Now`) перенесено
  в фоновый писатель (единый «хронометр», минимум работы в горячем пути); при переполнении канала
  `TryWrite` → fallback на блокирующий `WriteAsync` (доставка гарантирована, память ограничена — раньше
  строки молча терялись); `_disposed` → `volatile`; убран мёртвый try/catch в `Dispose` (таск никогда не
  фолтится); удалён неиспользуемый `using System.Threading;`; коммит `e778d2d`.
- Этап 5 — конфиг `appsettings.json` (факультатив, шаблон в корне) + `WebParser.Config`:
  `AppOptions`/`CrawlerOptions`/`LoggingOptions`, парсинг `System.Text.Json` из BCL (без новых пакетов);
  поиск CWD → AppContext.BaseDirectory, tolerant-json (комментарии/хвостовые запятые), валидация/кламп значений
  с fallback на дефолты; concurrency/таймауты/лимиты/retry/UA/путь и ротация лога/уровень — из конфига;
  `ContentParser` — instance `HttpClient` из опций, `ParseAsync` → `Task<ParseResult>`, OCE перевыбрасывается
  (Ctrl+C корректно обрывает конвейер), вывод слов в консоль только в verbose; `HostSems` ограничены по размеру
  (prune при >5000 хостов, idle >10 мин) — устранена проблема «растут без очистки»; метрики `WebParser.Stats.RunStats`
  (обработано/ошибок/RPS/среднее/время), живой прогресс-цикл через `\r` (не-verbose), итог — в консоль и лог
  (консоль больше не дублирует пер-словные строки); graceful shutdown: Ctrl+C останавливает планирование,
  текущие запросы доживают до 5 с, затем жёсткая отмена; коммит `dff5b90`.
- Fix (после review этапа 5) — повторный Ctrl+C во время grace-периода теперь не гасится обработчиком
  (`e.Cancel` не ставится) → процесс завершается немедленно; таймер grace стал наблюдаемым task'ом и
  ожидается перед итогом (не fire-and-forget, `hardCts.Cancel()` гарантирован до dispose); прогресс-строка
  паддится по длине предыдущей (без остаточных хвостов при смене RPS); `RunStats` — единый атомарный
  `Snapshot()` для RPS/среднего/времени (вместо несогласованных чтений), геттеры-скаляры убраны;
  избыточный `[JsonConverter]` на `MinLevel` удалён (глобальный конвертер уже есть); `HostLastUse`
  обновляется после успешного `WaitAsync` (без «свежести» у прерванных ожиданий); лишний `using System;`
  в `RunStats` убран (покрыт ImplicitUsings); коммит `bb85da5`.
- Этап 6 — из review этапа 5 применены отложенные правки: `ParseResult` → гранулярный `ParseOutcome`
  (`Success/InvalidUrl/HttpError/PageTooLarge/Failed`) + `Words` (`IReadOnlyList<WordFrequency>?`) для проверок;
  `HostSems`/`HostLastUse` переведены в instance-поля `ContentParser` (соответствие per-instance опциям);
  добавлены `WebParser.sln` + проект `WebParser.Tests` (xUnit, единственный носитель пакетов — продакшн
  остаётся без внешних зависимостей; основной csproj исключает каталог тестов из компиляции).
  Тесты (47 шт., `dotnet test` зелёный / 7 s):
  `HtmlWordCounter` — блок-разделители, inline-склейка не-блоковых узлов, script (в т.ч. `</div>` в JS-строке
  и литерал `<script>` в строке), style/textarea, комментарии, doctype, entities (`&amp;`, `&#…;`, `&#x…;`,
  `&nbsp;`), голый `&`, регистр, кириллица, пунктуация, кавычки в `title=">"`, сортировка по частоте,
  обработка по-символьными чанками = целиком;
  `ConfigLoader` — отсутствие файла, сломанный JSON, валидный (strig-значения в т.ч. `minLevel: "Error"`),
  комментарии/хвостовые запятые, кламп (min-concurrency/page-size/queue/file-size);
  `RunStats` — агрегирование/пустой снапшот/русская итоговая строка;
  интеграционные `ContentParser` + `StubHttpServer` (TcpListener, подсчёт hits по пути, peak-конкурентность,
  отложенные ответы) — успех со словами, charset из заголовка windows-1251, `<meta charset>` UTF-8,
  приоритет заголовка над meta, `PageTooLarge` (1 запрос), retry 503→200 (2 запроса), исчерпание ретраев
  (hits == MaxAttempts), 404 без ретраев (1 запрос), невалидный URL, таймаут → OCE пробрасывается
  (семантика «сетевой таймаут = отмена»), недоступный хост → `Failed`, per-host лимит (peak ≤ 1);
  бенчмарк 500 URL на локальный сервер: `elapsed=0.21s`, `RPS≈2425`, `avg=0.4ms/URL`,
  рост managed-памяти `≈250KB` (рыхлые границы: <30s, <512MB) — цель ~100 000 сайтов/день (1.2 req/s)
  превышена на порядки; коммит `983e057`.
- Fix (после review эпика) — полная проработка замечаний ревью (53 теста, `dotnet test` зелёный / 7 s):
  M1: сетевой таймаут отделён от отмены — повторный `TaskCanceledException` без активного
      `cancellationToken` теперь даёт `ParseResult(ParseOutcome.Failed)`, а не пробрасывается как OCE
      (один «висящий» сайт больше не обрывает весь батч как «прервано пользователем»; реальная отмена
      токена по-прежнему перевыбрасывается, проверено тестом `CallerCancellation_StillThrows`);
  M2: prune пери-хост семафоров работает не на каждый запрос, а раз в 30 с (в горячем пути — O(1),
      при 100k хостах нет суперлинейной деградации);
  M3: `RunStats.AverageMs` = сумма длительностей успешных / число успешных (длительности ошибок
      больше не смещают среднее);
  L2: `LogLevelConverter` — терпимый декод `minLevel`: опечатка/странное значение даёт `Info`,
      а не роняет весь конфиг в дефолты (`JsonStringEnumConverter` убран; тест `TypoInMinLevel...`);
  L3: поведение «BOM декодера важнее charset заголовка» зафиксировано тестом `CharsetBom_OverridesHeader`;
  L4: `ContentParser : IDisposable` — `HttpClient` освобождается явно (`using` в `Program` и бенчмарке),
      а не живёт до GC;
  L5: по-настоящему стриминговый `CountWordsAsync` — тело больше не буферизуется в `byte[]` целиком
      (раньше MemoryStream + `ToArray()` дважды): читается префикс ≤4 КБ для meta-charset, остаток
      идёт через `BoundedReadStream` (склеивает префикс с сетевым потоком, лимит по байтам
      `MaxPageSizeBytes`, inner-потоком не владеет) в `StreamReader(detectBOM: true)` и `HtmlWordCounter`
      чанками через `ArrayPool`; пиковая память сайта ≈ префикс + один чанк;
  L6: счётчик записанных в лог байт считает фактический `Environment.NewLine`, а не хардкод `+2`;
  L7: детект `<!--` сравнением индексов (`Length==3`, `[0]=='!'`, ...) вместо `ToString()=="!--"`
      (без выделения строки на каждый символ тега);
  L8: из `WebParser.csproj` убраны остатки метаданных `TravelLine.CodeReview`
      (`PackageId/Authors/Company/Product`);
  L1: добавлены `AsyncFileLoggerTests` — запись + flush на `Dispose`, фильтрация сообщений ниже
      `MinLevel`, ротация по размеру (`.old` появляется, старые записи уезжают туда);
  коммит `bf2f8ee`.
- Follow-up после review (55 тестов, `dotnet test` зелёный / 9 s) — оптимизация памяти, гигиена, стенд:
  `HtmlWordCounter` полностью на спанах — `ResolveEntity(ReadOnlySpan<char>)`, `int.TryParse` без
  `Substring`, именованные entity через `Span.Equals(...OrdinalIgnoreCase)` без `ToLowerInvariant()`,
  `_entity` (StringBuilder) → `char[12] + _entityLen`; чистка redundant/implicit using по всему решению;
  `AsyncFileLogger` — `Console` вынесен через `errorSink` (`TextWriter`, в `Program` — `Console.Error`),
  ctor больше не бросает при недоступном пути (fallback на `StreamWriter(Stream.Null)` + `_writerFaulted`,
  сообщение в errorSink), `Dispose` обёрнут в try/catch, при fault воркера — `_channel.Writer.TryComplete()`
  (нет ханга при полной очереди), запись без агрегатной строки: `DateTime.TryFormat` в `stackalloc char[32]`
  + сегментные `Write`, байт-счёт по сегментам (новый тест `InvalidLogPath_DoesNotThrow_AndDegradesSafely`);
  `ContentParser` — детект `<meta charset>` без декодирования всего ASCII-префикса в строку: поиск паттерна
  `charset=` прямо в `byte[]` (case-insensitive) + декод окна ≤128 байт (~8 КБ аллокаций на страницу
  fallback-пути убрано); `WebParser.csproj` — `<None Remove="WebParser.Tests\**" />`;
  `StubHttpServer` — keep-alive (цикл запросов на одной связи, раньше `Connection: close` = новая TCP-связь
  на каждый запрос), backlog 1024, `BufferedLineReader` (чтение батчами 4 КБ вместо по 1 байту);
  бенчмарк разбит на два: `Load500Urls_AllSucceed` (500 URL, 100% успех) и
  `Load500000Urls_HighThroughput_StableMemory` (500 000 URL, порог 99%, ~9 s, RPS≈53 000, рост памяти
  ≈48 МБ — стенд больше не теряет соединения на флуде, раньше 45 232 отказа на 500k);
  коммит `5673174`.
- Рефакторинг после эпика (64 теста, `dotnet test` зелёный / 9 s) — разделение ответственностей и DI:
  `ContentParser` (был ~480 строк) сужен до оркестратора: вынесены `CharsetDetector` (static, `Resolve`,
  `CodePagesEncodingProvider` в static-ctor), `BoundedReadStream`, `HttpFetcher`/`IHttpFetcher`
  (владеет `HttpClient`, retry/backoff/jitter/`Retry-After`) и `HostRateLimiter`/`IHostRateLimiter`
  (per-host семафоры + prune); контейнер `Microsoft.Extensions.DependencyInjection` (единственная
  внешняя зависимость продакшена, введена фабричнo регистрируемый внутренний `HttpFetcher`) —
  composition root в `Program.cs` (`ValidateOnBuild`/`ValidateScopes`, `provider.Dispose()` диспозит
  логгер и фетчер); правило владения «кто создал — тот и освобождает» через `_ownsFetcher`;
  `HtmlWordCounter` намеренно оставлен конкретным классом с per-страничным состоянием
  (интерфейс/DI не подходят transient-счётчику, инжектируемому чанками);
  прямой консольный вывод (`Console.WriteLine` слов) убран из парсера — печать слов и выбор
  детальности ошибок перенесены в composition root (логгер сам рендерит `Exception`); `verbose`
  флаг полностью удалён из `ContentParser` (уровень логера и консольный вывод — забота `Program.cs`);
  `code == 500` → `HttpStatusCode.InternalServerError`; новые тесты `CharsetDetectorTests`
  (header > meta > UTF-8, fallback, регистронезависимость, кавычки); `urls-*.txt` в `.gitignore`;
  коммит `29a9f0b`.
- Мелкие правки после рефакторинга (64 теста, `dotnet test` зелёный / 10 s):
  `ContentParser`: `chunk` (80 КБ) удалён полностью — `ReadPrefixAsync` читает префикс напрямую в
  `prefix` (минус Rent/Return и `Array.Copy`, `DetectPrefixBytes` больше не используется для
  неродного буфера), `8192` вынесено в константу `ReadBufferChars = 8 * 1024`;
  `ContentParserTests`: регистрация `CodePagesEncodingProvider` в static-ctor класса — тесты с
  `Encoding.GetEncoding(1251)` больше не зависят от порядка запуска (латентный баг изоляции).
- Review-проход SOLID/DRY/KISS (74 теста, `dotnet build` без предупреждений, `dotnet test` зелёный / 9 s) —
  разбиение монолитов и правки по замечаниям ревью:
  выделены `CliArgs` (парсинг `--verbose`/`-v` и файла-источника, неизвестная опция → exit 1),
  `UrlSource` (чтение списка URL из файла/stdin через `UrlValidator`), `CrawlRunner`
  (`Parallel.ForEachAsync` + метрики + verbose-вывод слов, возвращает факт прерывания пользователем),
  `ProgressReporter` (прогресс-цикл `\r`, останавливается через `Dispose`) и `Parser/UrlValidator`
  (единый `TryParse` «строка — полный HTTP(S) адрес» для парсера и `UrlSource`);
  `HostRateLimiter` переписан: запись хоста получила `RefCount` (CAS 0→-1, prune удаляет парой
  «ключ+значение», удерживаемые/ожидаемые хосты не вычищаются) — устранена гонка prune, откат refcount
  при отмене ожидания; prune-константы перенесены в `CrawlerOptions` (`HostIdleSeconds=600`,
  `HostPruneThreshold=5000`, `HostPruneIntervalSeconds=30`) с клампом в `ConfigLoader` и записью
  в `appsettings.json`;
  `HtmlWordCounter`: `IsDelimiter` по `char.IsWhiteSpace/Punctuation/Symbol` вместо `HashSet`
  (NBSP и прочие Unicode-разделители), `AbortEntity` вынесен (поведение `&#104;&#101;` сохранено);
  `Program.Main` разбит на секции (wiring DI, Ctrl+C, итог, `progress.Dispose()`); `ContentParser` —
  один DI-конструктор, убраны `IDisposable`/`_ownsFetcher`/verbose-вывод слов, catch —
  `when (ex is not OutOfMemoryException)`; `IHttpFetcher` больше не `IDisposable` (владение фетчером —
  у DI-контейнера), `HttpFetcher` реализует `IDisposable` отдельно; `ConfigLoader.Load(path?, errors?)`
  вместо прямого `Console.Error`; `AsyncFileLogger`: строка через переиспользуемый `StringBuilder`
  (байты по факту), при переполнении очереди блокируются только `Error`, прочие отбрасываются
  со счётчиком и отчётом в errorSink при `Dispose`; `WordFrequency.FormatLine`;
  тесты: `CliArgsTests`, `HostRateLimiterTests`, `ParserScope` (парсер с собственным фетчером),
  NBSP/дефис-кейсы `HtmlWordCounter`, `InternalsVisibleTo` в csproj; `AGENTS.md` обновлён под
  новую структуру; `*.DMP` в `.gitignore`; коммит `1e9b03c`.
- Анализ дампа `WebParser.DMP` (19.10.2026, без изменений кода): «зависание» после Ctrl+C — это
  `Console.ReadKey()` в конце `Main` (грейс-шатдаун отработал: итоги напечатаны, ждёт клавишу);
  writer `AsyncFileLogger` жив и вычитал очередь — обрыв хвоста `log.txt` лишь в буфере
  `StreamWriter` (flush будет при `Dispose`); deadlock/застрявших потоков не найдено
  (`clrthreads`/`clrstack -all`/`dumpasync`).