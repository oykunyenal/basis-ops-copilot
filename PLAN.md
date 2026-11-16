# Basis Ops Copilot: Yapılacaklar

SAP Basis ekiplerinin her gün elle yaptığı işleri otomatikleştiren, local çalışan bir .NET + n8n + yerel LLM projesi.
Hem portfolyo (GitHub) hem de teknik mülakatta anlatılacak somut örnek.

## 1. Sorun ve çözüm

**Sorun:** Basis ekibi her sabah aynı kontrolleri elle yapar ve gün içinde aynı talepleri tekrar tekrar karşılar.

| Elle yapılan iş | SAP'de nerede | Bu projede |
|---|---|---|
| İptal olan arka plan job'ları | SM37 | Otomatik tarama + bulgu |
| Kısa dump'lar (ABAP runtime error) | ST22 | Tarama + AI ile analiz |
| Kilitlenen kullanıcılar | SU01 | Tarama + self-servis kilit açma (onaylı) |
| Transport kuyruğu, hatalı import | STMS | Tarama + onaylı import |
| Süresi dolacak sertifikalar | STRUST | Tarama + uyarı |
| Work process doluluğu | SM50 / SM66 | Tarama + eşik uyarısı |

**Çözüm:**
1. Worker sistemi periyodik tarar, kurallara göre bulgu üretir.
2. Yerel LLM her sabah bulguları özetler, n8n raporu Telegram'a gönderir.
3. Kullanıcı sohbetten "kullanıcım kilitlendi" der, AI talebi oluşturur, insan onaylar, sistem uygular. Her adım audit log'a yazılır.

> **Dürüstlük notu:** Gerçek SAP'ye erişim yok. `FakeSap` servisi SAP'yi taklit eder.
> Kod bir `ISapGateway` arayüzü üzerinden konuşur. Gerçekte bu arayüzün arkasına **SAP .NET Connector (NCo) ile RFC/BAPI**
> (`BAPI_USER_UNLOCK`, `BAPI_XBP_JOB_*`, `TMS_*`) ya da SAP OData servisleri takılır. README'de ve mülakatta bu açıkça söylenir.

## 2. Mimari

```
                       ┌──────────────── Aspire AppHost (dotnet run) ─────────────────┐
                       │                                                              │
  n8n (mevcut, :5678) ─┼─HTTP─► BasisOps.Api (:5081) ──► PostgreSQL (Aspire container)│
   ▲  Schedule/Chat    │            ▲                        ▲                        │
   │  Telegram         │            │ aynı DB                │                        │
   │                   │      BasisOps.Worker ───────────────┘                        │
   │                   │        │ tarama + onaylı aksiyonları uygular                 │
   │                   │        ▼                                                     │
   │                   │      FakeSap (:5090)  ← SM37/ST22/SU01/STMS/STRUST/SM50 taklidi│
   │                   └──────────────────────────────────────────────────────────────┘
   └──── Ollama (mevcut, native, :11434, qwen3) ◄── Api (Microsoft.Extensions.AI)
```

- **n8n ve Ollama tekrar kurulmaz.** `~/ai-assistant` içindeki mevcut kurulum kullanılır.
  - n8n container'dan API'ye adres: `http://host.docker.internal:5081`
  - Api'den Ollama'ya adres: `http://localhost:11434`
- Aspire sadece projenin kendi parçalarını kaldırır: Postgres, FakeSap, Api, Worker. Dashboard'da log, trace ve metrikler görünür.

### Klasör yapısı

```
basis-ops-copilot/
├── BasisOps.slnx
├── src/
│   ├── BasisOps.AppHost/            Aspire orkestrasyonu
│   ├── BasisOps.ServiceDefaults/    OpenTelemetry, health check, resilience (Aspire şablonu)
│   ├── BasisOps.Domain/             Entity'ler, kurallar, durum makinesi. Hiçbir pakete bağlı değil
│   ├── BasisOps.Application/        Use case'ler (CQRS handler'ları), portlar (ISapGateway, IAiAnalyzer)
│   ├── BasisOps.Infrastructure/     EF Core + Npgsql, FakeSapGateway (HttpClient), OllamaAnalyzer
│   ├── BasisOps.Api/                Minimal API, /api/v1
│   ├── BasisOps.Worker/             Tarama + aksiyon yürütücü (BackgroundService)
│   └── FakeSap/                     SAP taklidi, hata enjeksiyonu (chaos) ayarlı
├── tests/
│   ├── BasisOps.UnitTests/          Kurallar, durum makinesi
│   └── BasisOps.IntegrationTests/   Testcontainers (Postgres) + WebApplicationFactory
├── n8n/workflows/                   Export edilmiş workflow JSON'ları
├── docs/
│   ├── architecture.md
│   ├── interview-notes.md           Mülakatta anlatılacaklar
│   └── runbooks/                    Basis runbook'ları (n8n RAG için)
└── .github/workflows/ci.yml
```

### Temel kararlar (README'de "Neden?" bölümü olacak)

| Karar | Neden |
|---|---|
| Clean / Onion katmanlar | Domain SAP'den ve veritabanından bağımsız; FakeSap gerçek SAP ile değişebilir |
| CQRS, **MediatR'sız** | MediatR 2025'te ticari lisansa geçti. Basit `ICommandHandler<T>` / `IQueryHandler<T,R>` arayüzleri yeterli |
| `ISapGateway` = anti-corruption layer | SAP'nin terimleri (BNAME, JOBNAME, TRKORR) domain'e sızmaz |
| Aksiyonlar = onaylı durum makinesi | Prod sistemde AI ya da otomasyon tek başına değişiklik yapmaz; insan onaylar |
| Aksiyon tablosu kuyruk olarak (`FOR UPDATE SKIP LOCKED`) | Ayrı mesaj kuyruğu kurmadan güvenli, çoklu worker'a uygun iş dağıtımı |
| Idempotency-Key | n8n ya da istemci aynı talebi iki kez gönderirse tek aksiyon oluşur |
| Yerel LLM (Ollama) | Müşteri verisi dışarı çıkmaz, maliyet sıfır |
| LLM öncesi maskeleme | Kullanıcı adları ve hostlar modele gitmeden maskelenir (`USER_1`, `HOST_1`) |

## 3. Ön koşullar (Mac)

```bash
dotnet --list-sdks          # .NET 10 SDK olmalı
docker info                 # Docker Desktop / OrbStack çalışıyor olmalı
ollama list                 # qwen3 modeli var mı
curl -s localhost:5678 >/dev/null && echo "n8n ayakta"
```

- Aspire şablonları: `dotnet new install Aspire.ProjectTemplates` (Claude Code güncel kurulum yöntemini kontrol etsin).
- GitHub'da boş bir `basis-ops-copilot` reposu aç (README eklemeden). Push'u sen yapacaksın.

## 4. Adım adım yapılacaklar

Her adım ayrı bir commit (hatta ayrı bir PR) olsun. Her adımın sonunda **"Bitti sayılır"** maddelerini kontrol et.
Claude Code'a her adımda şunu söyle: *"PLAN.md'de Adım N'yi yap. Önce planını kısaca anlat, sonra uygula, sonunda build ve test çalıştır."*

### Adım 0: Repo ve iskelet
- [x] `git init`, `.gitignore` (dotnet), `README.md` taslağı, `PLAN.md` ve `CLAUDE.md` repoya.
- [x] Solution (`.slnx`) ve tüm projeler, proje referansları katman kurallarına göre:
  - Domain ← Application ← Infrastructure ← Api / Worker.
  - Api ve Worker, ServiceDefaults'a referans verir.
- [x] Aspire AppHost: Postgres (`basisops` veritabanı), FakeSap, Api (sabit port **5081**), Worker. Api ve Worker Postgres'i bekler (`WaitFor`).
- [x] `Directory.Build.props`: `net10.0`, `Nullable enable`, `TreatWarningsAsErrors`, `ImplicitUsings`.
- [x] `Directory.Packages.props` ile merkezi paket versiyonu.

**Bitti sayılır:** `dotnet build` hatasız. `dotnet run --project src/BasisOps.AppHost` dashboard'u açıyor, tüm kaynaklar "Running".
**Commit:** `chore: solution skeleton with Aspire AppHost`

### Adım 1: FakeSap
- [x] Sistemler: `PRD` (prod), `QAS`, `DEV`. Her istek `/sap/{sid}/...` ile başlar.
  - Ek: `GET /sap/systems` (T000 / CCCATEGORY: P/T/C) ve `GET /sap/{sid}/dumps/{dumpId}` (ST22 detay, AI analizi için).
- [x] Okuma uçları (SAP'deki karşılıkları yorumda yazsın):
  - `GET /sap/{sid}/jobs?status=canceled` (SM37): JOBNAME, JOBCOUNT, STATUS, STRTDATE, user.
  - `GET /sap/{sid}/dumps?since=` (ST22): runtime error tipi (`TIME_OUT`, `DBSQL_DUPLICATE_KEY_ERROR`, `MESSAGE_TYPE_X`…), program, user, kısa metin.
  - `GET /sap/{sid}/users/locked` (SU01): BNAME, kilit nedeni (yanlış şifre / admin), zaman.
  - `GET /sap/{sid}/transports/queue` (STMS): TRKORR, sahibi, durum, return code (0/4/8/12).
  - `GET /sap/{sid}/certificates` (STRUST): PSE adı, bitiş tarihi.
  - `GET /sap/{sid}/workprocesses` (SM50): tip (DIA/BTC/UPD), toplam/meşgul.
- [x] Aksiyon uçları (idempotent; BAPIRET2 benzeri cevap):
  - `POST /sap/{sid}/users/{bname}/unlock` (BAPI_USER_UNLOCK karşılığı).
  - `POST /sap/{sid}/jobs/{jobname}/{jobcount}/restart`.
  - `POST /sap/{sid}/transports/{trkorr}/import`.
- [x] Veri, sabit bir seed ile gerçekçi ve tekrarlanabilir üretilsin. Zaman içinde yeni dump ve kilit olayları eklensin.
- [x] Chaos ayarı (`appsettings`): `FailurePercent` (503), `RateLimitPercent` (429 + Retry-After), `LatencyMs`.
- [x] SAP alan adları JSON'da büyük harf kalsın (BNAME, TRKORR). Domain'e sızmaması ACL'in işi.

**Bitti sayılır:** `FakeSap.http` dosyasındaki isteklerin hepsi cevap veriyor. Chaos %100 yapılınca 503 dönüyor.
**Commit:** `feat(fakesap): simulate SM37/ST22/SU01/STMS/STRUST/SM50 with chaos settings`

### Adım 2: Domain + Application
- [x] Domain:
  - `SapSystem` (Sid, Role: Prod/Quality/Dev).
  - `HealthScan` (zaman, sistem, süre, durum).
  - `Finding`: Category (Job, Dump, User, Transport, Certificate, WorkProcess), Severity (Info, Warning, Critical), Key (tekrarı engellemek için örn. `JOB:PRD:Z_BILLING:1234`), FirstSeen, LastSeen, Resolved.
  - `ActionRequest` durum makinesi: `Requested → Approved → Executing → Succeeded | Failed`, `Requested → Rejected`. Geçersiz geçiş exception fırlatır.
    - Ek geçiş: `Executing → Approved` (geçici hata, `NextAttemptAt` ile retry). `MaxAttempts` aşılınca `Failed`.
  - Alanlar: Type, Target, RequestedBy, ApprovedBy, Attempts, LastError, IdempotencyKey.
  - **Kural:** Prod sistemde talep eden kişi kendi talebini onaylayamaz (four-eyes principle).
  - `AuditEntry`: kim, ne, ne zaman, önceki → sonraki durum. Her geçiş kendi audit kaydını döndürür.
  - `FindingReconciler`: yeni tarama sonucunu açık bulgularla birleştirir (aç / güncelle / çözüldü). Çözülen bulgu tekrar görülürse yeni bulgu açılır.
- [x] Kurallar (`IHealthRule`, her biri ayrı sınıf, test edilebilir):
  - İptal olan job: kritik job listesi (`Z_BILLING*`, `SAP_COLLECTOR*`) → Critical, diğerleri → Warning. Aynı job'ın sonradan başarılı çalışması varsa bulgu çözülmüş sayılır.
  - Dump: son 1 saatte aynı tipten ≥ 5 → Warning, ≥ 20 → Critical.
  - Sertifika: < 30 gün → Warning, < 7 gün → Critical.
  - Transport: RC ≥ 8 → Critical, RC 4 → Info.
  - Work process: DIA doluluk > %80 → Warning, > %95 → Critical.
  - Kilitli kullanıcı: Info (tek başına alarm değil, self-servis için listelenir).
- [x] Application:
  - Portlar: `ISapGateway`, `IAiAnalyzer`, `TimeProvider`. Ek: `ISystemRegistry` (sistem + rol; four-eyes için SAP'ye gitmeden), repository'ler ve `IUnitOfWork`.
  - Komutlar: `RunHealthScan`, `RequestAction`, `ApproveAction`, `RejectAction`.
  - Sorgular: `GetSystemHealth`, `ListFindings`, `GetAction`, `GetDailyReport`. Ek (Api için): `ListActions`, `GetAuditLog`.
  - Basit `ICommandHandler<TCommand, TResult>` ve `IQueryHandler<TQuery, TResult>`. Validation handler girişinde yapılır.
  - Handler'lar `Result<T>` döner (Validation → 400, NotFound → 404, Conflict → 409, Forbidden → 403). Four-eyes ihlali → 403.

**Bitti sayılır:** UnitTests projesinde kural ve durum makinesi testleri yeşil (en az 15 test).
**Commit:** `feat(domain): findings, rules and action approval state machine`

### Adım 3: Infrastructure + Api
- [ ] EF Core + Npgsql: `BasisOpsDbContext`, migration'lar. Aspire ile Postgres bağlantısı (`AddNpgsqlDbContext`).
- [ ] `FakeSapGateway`:
  - Typed HttpClient, `AddStandardResilienceHandler` (retry + jitter, circuit breaker, timeout).
  - SAP DTO → domain dönüşümü (ACL) burada yapılır.
- [ ] Api uçları (`/api/v1`):
  - `GET /systems`, `GET /systems/{sid}/health`.
  - `GET /findings?sid=&severity=&open=true` (sayfalı).
  - `POST /actions` (`Idempotency-Key` başlığı zorunlu; aynı anahtar + farklı içerik → 409) → `202 Accepted` + Location.
  - `POST /actions/{id}/approve`, `POST /actions/{id}/reject` (gövde: kim, neden).
  - `GET /actions/{id}`, `GET /actions?status=`.
  - `GET /audit?target=`.
- [ ] Hata formatı: ProblemDetails. Doğrulama hatası → 400. Geçersiz durum geçişi → 409.
- [ ] Kimlik: n8n için basit `X-Api-Key` (local). README'de "prod'da Entra ID / OAuth2" notu.
- [ ] OpenAPI + Scalar (ya da Swagger UI) `/scalar`.

**Bitti sayılır:** `BasisOps.Api.http` ile talep oluştur → onayla → durumu sorgula zinciri çalışıyor. Aynı Idempotency-Key ikinci kez gönderilince aynı aksiyon dönüyor.
**Commit:** `feat(api): findings and approval-based actions with idempotency`

### Adım 4: Worker
- [ ] `HealthScanWorker`: `PeriodicTimer` ile her sistem için tarama (ayar: `ScanIntervalSeconds`, varsayılan 60).
  - Bulgu varsa `LastSeen` güncellenir, yoksa yenisi açılır, kaybolan bulgu `Resolved` olur.
- [ ] `ActionExecutorWorker`:
  - `Approved` aksiyonları `FOR UPDATE SKIP LOCKED` ile alır, FakeSap'e uygular.
  - Geçici hatada `Attempts++` ve backoff ile tekrar dener. `MaxAttempts` (5) aşılırsa `Failed` olur ve audit + log'a "alarm" yazar.
  - Circuit breaker açıkken denemeyi sayma, bekle.
- [ ] Her worker DbContext'i scope içinde kullanır (captive dependency yok, `IServiceScopeFactory`).
- [ ] Log'larda correlation: aksiyon id'si ve sid structured log alanı olarak geçer.

**Bitti sayılır:** Aspire dashboard'da tarama trace'leri görünüyor. FakeSap chaos %50 iken onaylı aksiyonlar retry ile sonunda `Succeeded` oluyor. %100 iken `Failed` oluyor.
**Commit:** `feat(worker): periodic health scan and approved action executor`

### Adım 5: AI (Ollama + Microsoft.Extensions.AI)
- [ ] `IChatClient` kaydı: `OllamaSharp` paketi, model ve adres ayardan (`Ai:Endpoint=http://localhost:11434`, `Ai:Model=qwen3:8b`).
- [ ] `OllamaAiAnalyzer : IAiAnalyzer`:
  - `AnalyzeDump(dump)`: olası neden, etki, Basis tarafında ilk adımlar, ilgili SAP işlem kodu. **JSON şemalı çıktı** (record'a parse edilir).
  - `SummarizeDaily(findings)`: 5–8 satır Türkçe yönetici özeti + öncelik sırası.
- [ ] Modele gitmeden önce **maskeleme**: kullanıcı adları, host ve IP'ler → `USER_1`, `HOST_1`. Cevapta geri çevrilmez.
- [ ] Prompt injection: dump metni "veri" olarak işaretlenir, talimat olarak görülmez (system prompt'ta açıkça yazılır).
- [ ] **Fallback:** Ollama kapalıysa ya da timeout olursa rapor AI'sız (kural tabanlı liste) döner, hata fırlatmaz.
- [ ] Api: `POST /dumps/{id}/analyze`, `GET /reports/daily?sid=PRD`.

**Bitti sayılır:** Ollama açıkken günlük rapor özetli geliyor. Ollama kapatılınca (`ollama stop` / uygulamayı kapat) rapor yine geliyor, `aiAvailable: false` alanıyla.
**Commit:** `feat(ai): dump analysis and daily summary with local LLM, masking and fallback`

### Adım 6: n8n workflow'ları (mevcut n8n'de)
- [ ] Credential: Header Auth (`X-Api-Key`), base URL `http://host.docker.internal:5081`.
- [ ] **Basis Sabah Raporu:** Schedule (hafta içi 08:00) → `GET /reports/daily?sid=PRD` → Telegram (HTML). Hata durumunda "Rapor alınamadı" mesajı.
- [ ] **Self-servis Asistan:** Chat Trigger → AI Agent (Ollama `qwen3`) + araçlar:
  - `kilitli_kullanicilar` → `GET /systems/{sid}/health` (ya da ayrı uç).
  - `kilit_acma_talebi` → `POST /actions` (Idempotency-Key = sohbet id + kullanıcı).
  - `talep_durumu` → `GET /actions/{id}`.
  - System prompt: **ajan asla onay vermez**, sadece talep açar ve durumu söyler.
- [ ] **Onay akışı:** yeni talep → Telegram "Send and Wait for Response" (Onayla / Reddet) → `POST /actions/{id}/approve|reject`.
- [ ] (Opsiyonel) Runbook RAG: `docs/runbooks/*.md` → pgvector → ajan "bu dump için ne yapmalıyım" sorusunda runbook'a bakar.
- [ ] Workflow'ları export et → `n8n/workflows/*.json` (credential içermez).

**Bitti sayılır:** Sohbette "PRD'de MEHMET kullanıcısı kilitlendi" yazınca talep açılıyor, Telegram'a onay düşüyor, onaylayınca birkaç saniye içinde kilit açılıyor ve audit'te görünüyor.
**Commit:** `feat(n8n): daily report, self-service agent and Telegram approval workflows`

### Adım 7: Testler ve CI
- [ ] Unit: kurallar, durum makinesi, maskeleme, idempotency hash karşılaştırması.
- [ ] Integration (Testcontainers PostgreSQL + `WebApplicationFactory`, FakeSap yerine `ISapGateway` test double'ı):
  - Talep → onay → yürütme → Succeeded.
  - Aynı Idempotency-Key → tek kayıt. Farklı içerik → 409.
  - Prod'da kendi talebini onaylama → 409 / 403.
  - İki executor aynı anda çalışınca aynı aksiyon iki kez uygulanmıyor (`SKIP LOCKED`).
  - Ollama yokken rapor fallback ile dönüyor.
- [ ] `.github/workflows/ci.yml`: push / PR → restore, build, test (ubuntu-latest, .NET 10). Test sonuç özeti.
- [ ] README'ye CI rozeti.

**Bitti sayılır:** `dotnet test` local'de ve GitHub Actions'ta yeşil.
**Commit:** `test: integration tests with Testcontainers and CI pipeline`

### Adım 8: Vitrin (portfolyo için en önemli kısım)
- [ ] README:
  - Tek cümlelik amaç, sorun → çözüm tablosu, mimari diyagram (Mermaid).
  - "5 dakikada çalıştır" bölümü, ekran görüntüleri (Aspire dashboard, Telegram raporu, onay mesajı, n8n akışları).
  - "Neden bu kararlar", "Gerçek SAP'ye nasıl bağlanır", "Prod'da neler değişir" bölümleri.
- [ ] `docs/architecture.md`: akış diyagramları (tarama, aksiyon onayı, AI).
- [ ] `docs/interview-notes.md`: aşağıdaki mülakat cümleleri.
- [ ] Kısa demo GIF'i (sohbet → onay → kilit açıldı).
- [ ] GitHub repo açıklaması + topic'ler: `dotnet`, `aspire`, `sap-basis`, `n8n`, `ollama`, `clean-architecture`.

## 5. "Prod'da neler değişir" (README ve mülakat için)

| Burada | Gerçekte |
|---|---|
| FakeSap | SAP NCo (RFC/BAPI) ya da SAP OData; teknik kullanıcı, en az yetki (S_RFC, S_USER_GRP) |
| X-Api-Key | Entra ID / OAuth2, rol bazlı onay yetkisi |
| Aksiyon tablosu kuyruk | Yük artarsa RabbitMQ / Service Bus + outbox |
| Telegram | Teams / ServiceNow / Jira entegrasyonu, ticket açma |
| Tek Ollama | Kurum içi LLM (LLMaaS) ya da Azure OpenAI, model değerlendirme seti |
| Aspire local | Aspire'ın ürettiği compose / Kubernetes manifest'leri, Key Vault, pipeline |

## 6. Mülakatta anlatılacak cümleler

- **Tek cümle:** "Basis ekibinin sabah kontrollerini ve tekrar eden kullanıcı taleplerini otomatikleştiren, onaylı ve audit'li bir sistem kurdum. Yerel LLM dump'ları ve günlük durumu özetliyor."
- **Neden onay akışı:** "Prod SAP'de otomasyon da AI da tek başına değişiklik yapmamalı. Four-eyes prensibi ve audit log, denetimde sorulan ilk şeyler."
- **Neden yerel LLM:** "Müşteri sistem verisi dışarı çıkmıyor, maliyet yok. Yine de modele gitmeden kullanıcı adlarını maskeliyorum."
- **Dayanıklılık:** "SAP bakımda ya da yavaşsa retry + jitter, devre kesici ve deneme sınırı var. Sınır aşılırsa aksiyon Failed olur ve alarm düşer, sonsuz döngü yok."
- **Gerçek SAP:** "Gateway arayüzü sayesinde FakeSap yerine SAP .NET Connector ile BAPI çağrısı takılıyor. Domain bundan etkilenmiyor."
- **Ölçüm:** "Sabah kontrolü elle ~30–45 dk sürüyorsa bu sistemle rapor 08:00'de hazır. Kilit açma talebi dakikalardan saniyelere iniyor." (Rakamları senaryo olarak söyle, ölçülmüş gibi sunma.)

## 7. Sonraki faz (opsiyonel): İK otomasyonu

Aynı altyapı (onaylı aksiyon, audit, n8n, yerel LLM) ile işe giriş / çıkış süreci:
yeni çalışan → hesap açma, ekipman, eğitim listesi, ilk gün e-postası; çıkışta tüm erişimlerin kapatılması ve kontrol listesi.
