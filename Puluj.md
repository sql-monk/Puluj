# 1. Призначення

Розробити вебсистему цивільного ситуаційного оповіщення, яка в реальному часі:

- збирає інформацію про повітряні загрози з відкритих джерел;
- зберігає оригінальні повідомлення;
- нормалізує, геокодує та дедуплікує їх;
- класифікує тип та, якщо можливо, конкретну модель загрози;
- зв'язує окремі повідомлення в логічні `ThreatTrack`;
- відображає їх на карті;
- показує напрямок/історію руху;
- оцінює орієнтовний ETA до заданої користувачем точки;
- дозволяє відкрити всі джерела, на яких базується об'єкт.

Система не повинна видавати розрахункові координати за фактичні.

---

# 2. Географічне покриття

Основна область:

- Україна;
- Чорне море;
- прилеглі райони РФ;
- Білорусь;
- Молдова/Придністров'я за необхідності.

---

# 3. Джерела

Кожне джерело реалізується окремим `Collector`.

Підтримати:

- REST API;
- WebSocket/SSE;
- Telegram MTProto;
- RSS/Atom;
- дозволений web-scraping;
- інші відкриті OSINT-джерела.

Початкові джерела:

- alerts.in.ua;
- Kyiv Digital;
- офіційні Telegram-канали;
- ОВА;
- Повітряні сили;
- перевірені моніторингові Telegram-канали.

Кожне джерело має:

```text
SourceId
Name
Type
URL
TrustLevel
Priority
Enabled
PollingInterval
```

---

# 4. Потік обробки

```text
Collectors
    ↓
RawMessage
    ↓
Normalizer
    ↓
Parser / NLP
    ↓
Geocoder
    ↓
Observation
    ↓
Deduplicator
    ↓
Classifier
    ↓
Threat Correlator
    ↓
ThreatTrack
    ↓
ETA / Confidence Engine
    ↓
REST + SignalR
    ↓
Web UI
```

`RawMessage` після отримання є immutable.

---

# 5. RawMessage

Зберігає первинну інформацію без втрат:

```text
RawMessageId
SourceId
SourceMessageId
PublishedAt
ReceivedAt
RawText
RawPayload
URL
Hash
```

`SourceId + SourceMessageId` та/або `Hash` використовуються для idempotency.

---

# 6. Observation

Окремий факт, отриманий із повідомлення:

```text
ObservationId
ObservedAt
ThreatCategory
ThreatClass
ThreatModelId
ModelConfidence
ObjectCount
Location
LocationAccuracy
Direction
SourceId
RawMessageId
Confidence
```

`Location` може бути:

```text
Point
Area
City
District
Region
DirectionOnly
Unknown
```

Не перетворювати нечіткий текст на псевдоточну координату.

Наприклад:

```text
"БпЛА з Чернігівщини курсом на Київщину"
```

зберігати як області/географічні зони, а не випадково вибрану точку.

---

# 7. Класифікація загроз

Використовувати ієрархію:

```text
ThreatCategory
    ↓
ThreatClass
    ↓
ThreatFamily
    ↓
ThreatModel
```

Наприклад:

```text
UAV
 └─ StrikeUAV
     └─ ShahedFamily
         ├─ Shahed-131
         ├─ Shahed-136
         └─ інша/невідома модифікація

Missile
 ├─ CruiseMissile
 │   ├─ Kh-101/555 family
 │   ├─ Kalibr family
 │   ├─ Kh-59/69 family
 │   └─ Unknown
 │
 ├─ BallisticMissile
 │   ├─ Iskander family
 │   ├─ KN-family
 │   └─ Unknown
 │
 ├─ AeroBallisticMissile
 │
 └─ Hypersonic/Cruise
```

Класифікація повинна бути розширюваною через БД, а не через зміни коду.

---

# 8. ThreatModel

Довідник конкретних типів/моделей:

```text
ThreatModelId
ThreatClassId
CanonicalName
Family
Manufacturer
Country
Enabled
Metadata
```

Окремо:

```text
ThreatModelAlias
----------------
ThreatModelId
Alias
Language
SourceId nullable
```

Наприклад aliases:

```text
Shahed-136
Shahed 136
Герань-2
Geran-2
шахед
Shahed-type UAV
```

можуть бути пов'язані з однією моделлю або сімейством залежно від точності повідомлення.

---

# 9. Точність класифікації

Для моделі обов'язково зберігати:

```text
ModelConfidence
IdentificationSource
IdentificationMethod
```

Рівні:

```text
Confirmed
High
Medium
Low
Unknown
```

Якщо джерело повідомляє просто:

```text
"крилата ракета"
```

система не повинна самостійно називати її `Kh-101`.

Якщо:

```text
"ймовірно Х-101"
```

зберігати:

```text
ThreatClass = CruiseMissile
ThreatModel = Kh-101
ModelConfidence = Low/Medium
```

---

# 10. ThreatTrack

`ThreatTrack` — логічний об'єкт, сформований із одного або декількох observations.

```text
Observation #141 ─┐
Observation #146 ─┼── ThreatTrack #37
Observation #151 ─┘
```

Correlator враховує:

```text
тип
модель/сімейство
час
географію
напрямок
кількість об'єктів
попередній рух
джерела
confidence
```

Зв'язок:

```text
ThreatTrackObservation
----------------------
ThreatTrackId
ObservationId
Sequence
AssociationConfidence
```

---

# 11. Типи подій

Мінімально:

```text
UAV
Missile
Aircraft
GuidedBomb
UnknownThreat

AirRaidAlert
AlertCancelled
ThreatCancelled
ExplosionReport
AirDefenseActivity
```

Підтипи ракет і БпЛА визначаються через `ThreatClass/ThreatModel`.

---

# 12. Відображення: БпЛА

Показувати:

```text
● останній відомий район
→ повідомлений напрямок
```

Маркер старіє візуально:

```text
новий           ██████████
5 хв            ███████
10 хв           ████
20 хв           ██
```

Функція fading конфігурована для кожного класу загрози.

Біля маркера:

```text
Shahed-type UAV
~7 хв тому
курс: SW
ETA до вас: ~15–25 хв
Confidence: High
```

---

# 13. Відображення: крилаті ракети

Показувати:

```text
○──────→○──────→●
old             last
```

Відображаються:

- попередні observations;
- останній observation;
- напрямок;
- часові мітки;
- широкий прогнозний коридор, якщо даних достатньо.

Фактичні observations і прогноз повинні мати різне оформлення.

---

# 14. Відображення: балістична загроза

Показувати лише публічно повідомлену узагальнену інформацію:

```text
район пуску
      ╲
       ╲ - - - - >
                  район загрози
```

Не обчислювати точну позицію пускової установки або точну прогнозовану точку удару.

---

# 15. Confidence

Окремо оцінюються:

```text
ObservationConfidence
ClassificationConfidence
TrackConfidence
DirectionConfidence
ETAConfidence
```

UI повинен відрізняти:

```text
факт із джерела
результат нормалізації
результат кореляції
прогноз
```

---

# 16. Місцезнаходження користувача

Варіанти:

```text
точка на карті
пошук населеного пункту
браузерна Geolocation API
```

Точні координати отримувати лише за згодою.

За можливості зберігати `HomeLocation` локально в браузері.

---

# 17. ETA

Для кожного активного `ThreatTrack`:

```text
ETA до вас: ~12–20 хв
Confidence: Medium
```

або:

```text
ETA невідомий
```

Вхідні дані:

```text
остання зона
час
напрямок
історія observations
клас загрози
доступна інформація про швидкість
```

ETA завжди показувати діапазоном.

Не створювати хибну точність типу:

```text
11 хв 37 секунд
```

---

# 18. Деталі загрози

Клік по об'єкту:

```text
Тип: UAV
Модель: Shahed-family
Model confidence: Medium

Останнє повідомлення: 01:37
Напрямок: SW
Track confidence: High

ETA до вас:
~12–20 хв
```

Кнопка:

```text
Джерела / Деталі
```

показує:

```text
01:21 Source A
      original text

01:29 Source B
      original text

01:37 Source C
      original text
```

та посилання на оригінали.

---

# 19. Web UI

Основний екран — карта.

Фільтри:

```text
[ UAV              ✓ ]
[ Cruise missiles  ✓ ]
[ Ballistic        ✓ ]
[ Aircraft           ]
[ Alerts           ✓ ]

[ Active only      ✓ ]

Моя точка: ...
```

Підтримати:

```text
desktop
mobile
dark mode
SignalR realtime updates
фільтрацію
кластеризацію маркерів
історичний режим
```

---

# 20. Історичний режим

Timeline:

```text
00:00 ─────────●──────── 06:00
```

Дозволяє відтворити стан системи на будь-який момент часу.

Зберігаються всі observations і зміни `ThreatTrack`.

---

# 21. Сховище даних

Основна БД:

```text
PostgreSQL
+
PostGIS
```

Основні таблиці:

```text
Source
RawMessage

ThreatCategory
ThreatClass
ThreatModel
ThreatModelAlias

Observation

ThreatTrack
ThreatTrackObservation

AirAlert
UserLocation
CollectorState
ProcessingError
```

---

# 22. Географічні дані

Використовувати PostGIS:

```text
geometry
geography
Point
LineString
Polygon
MultiPolygon
```

Наприклад:

```text
Observation.Location
ThreatTrack.LastLocation
ThreatTrack.TrackGeometry
Location.AreaGeometry
```

Spatial indexes:

```text
GiST
SP-GiST
BRIN
```

залежно від характеру таблиці.

---

# 23. JSON

Неструктуровані payload-и джерел зберігати в:

```text
jsonb
```

Наприклад:

```text
RawMessage.RawPayload jsonb
ThreatModel.Metadata jsonb
Observation.ParserMetadata jsonb
```

Структуровані дані, за якими виконуються основні joins/filtering, залишаються нормальними реляційними колонками.

---

# 24. Часові дані

Основні таблиці залишаються звичайними PostgreSQL tables.

За значного зростання обсягу:

```text
Observation
RawMessage
ThreatTrackHistory
```

можна партиціювати за часом.

TimescaleDB розглядати як опціональне розширення, а не вимогу MVP.

---

# 25. Чому не MongoDB як основна БД

MongoDB добре підходить для:

```text
RawMessage
RawPayload
різнорідних JSON-документів
```

і підтримує GeoJSON та spatial queries.

Але ядро системи має багато зв'язків:

```text
Source
  ↓
RawMessage
  ↓
Observation
  ↓
ThreatTrack
  ↓
Classification
```

та запитів:

```text
"покажи всі джерела цього track"

"які observations сформували цю траєкторію"

"яка модель була визначена і чому"

"переграй стан карти на 01:37"

"знайди observations у цій зоні за останні 20 хв"
```

Для цього relational + spatial модель підходить краще.

MongoDB як окрема БД для MVP не потрібна.

---

# 26. Додаткові сховища

Не включати в MVP без потреби.

У майбутньому можливі:

```text
Redis
    realtime cache
    pub/sub
    distributed locks

OpenSearch
    full-text search по RawMessage

ClickHouse
    великі історичні/аналітичні масиви

S3-compatible storage
    великі raw payload/archive/media
```

PostgreSQL залишається source of truth.

---

# 27. Backend

Рекомендований стек:

```text
Backend           ASP.NET Core
Collectors        .NET
Telegram          MTProto
Database          PostgreSQL
GIS               PostGIS
ORM               EF Core + Npgsql
Realtime          SignalR
Frontend          TypeScript
Map               MapLibre GL JS
Deployment        Docker
Observability     OpenTelemetry
```

Collector-и повинні бути логічно незалежними.

Необов'язково одразу робити десяток microservices.

---

# 28. Логічні компоненти

```text
Collector Service
    ├─ API
    ├─ Telegram
    ├─ RSS
    └─ Web

Processing Service
    ├─ Normalizer
    ├─ NLP Parser
    ├─ Geocoder
    └─ Classifier

Correlation Service
    ├─ Deduplicator
    └─ Threat Correlator

Prediction Service
    ├─ ETA
    └─ Confidence

Backend
    ├─ REST API
    └─ SignalR

Frontend
    └─ MapLibre

Storage
    └─ PostgreSQL + PostGIS
```

На MVP це можуть бути 2–3 процеси, а не фізичний microservice на кожний блок.

---

# 29. Надійність

Обов'язково:

```text
retry + exponential backoff
rate-limit handling
idempotency
collector watchdog
UTC timestamps
PublishedAt != ReceivedAt
parser error logging
source latency monitoring
raw payload preservation
health checks
```

Відмова одного Collector не повинна зупиняти систему.

---

# 30. MVP

Перша версія:

```text
alerts.in.ua
      +
Telegram 5–10 каналів
      ↓
RawMessage
      ↓
Parser
      ↓
Observation
      ↓
Classification
      ↓
ThreatTrack
      ↓
PostgreSQL/PostGIS
      ↓
ASP.NET Core
      ↓
SignalR
      ↓
MapLibre
```

MVP реалізує:

```text
карта
активні тривоги
UAV
ракети
класифікація типу/моделі
останні observations
історія руху
fading старих даних
confidence
точка користувача
ETA
перегляд джерел
історичний replay
повне журналювання
```

---

# 31. Головний принцип даних

Для будь-якого об'єкта на карті повинен існувати повний provenance chain:

```text
Map Object
     ↓
ThreatTrack
     ↓
Observation(s)
     ↓
RawMessage(s)
     ↓
Source
     ↓
Original Message
```

А окремо для класифікації:

```text
ThreatModel
     ↓
Classification Confidence
     ↓
Observation
     ↓
RawMessage
```

Тобто система завжди повинна мати відповідь на питання:

**«Що саме ми показуємо, звідки це взялося і наскільки ми в цьому впевнені?»**