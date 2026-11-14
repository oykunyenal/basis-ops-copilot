# CLAUDE.md: basis-ops-copilot

Bu repo bir portfolyo projesi: SAP Basis operasyon otomasyonu (.NET 10 + Aspire + n8n + Ollama).
Yapılacaklar ve adımlar `PLAN.md` içinde. Her oturumda önce `PLAN.md`'yi oku, hangi adımda olduğumuzu `git log` ile kontrol et.

## Çalışma şekli
- Bir seferde **tek adım** (PLAN.md'deki Adım N). Başlamadan önce 5–10 maddelik kısa plan yaz, sonra uygula.
- Her adım sonunda: `dotnet build` ve `dotnet test` çalıştır, hataları düzelt, sonra commit mesajı öner. Commit'i ve push'u kullanıcı onaylar.
- PLAN.md'de tamamlanan maddeleri `[x]` yap.
- Emin olmadığın paket / API versiyonunu tahmin etme; `dotnet package search` ya da resmi dokümana bak.

## Mimari kurallar
- Bağımlılık yönü içe doğru: Domain ← Application ← Infrastructure ← Api / Worker. Domain hiçbir NuGet paketine bağlı olmaz.
- MediatR **kullanma** (ticari lisans). Kendi `ICommandHandler<TCommand,TResult>` / `IQueryHandler<TQuery,TResult>` arayüzlerimiz var.
- SAP alan adları (BNAME, TRKORR, JOBNAME…) sadece FakeSap ve `Infrastructure/Sap` içindeki DTO'larda yaşar. Domain kendi isimlerini kullanır (anti-corruption layer).
- `new HttpClient()` yok: typed client + `AddStandardResilienceHandler`.
- Arka plan servislerinde DbContext'i `IServiceScopeFactory` ile scope içinde al.
- Async metotlar `CancellationToken` alır ve iletir. `.Result` / `.Wait()` yok.
- Hatalar ProblemDetails ile döner. Geçersiz durum geçişi → 409, doğrulama → 400.
- Zaman için `TimeProvider` kullan (test edilebilirlik).
- Aksiyonlar onaysız uygulanmaz. Prod sistemde talep eden kendi talebini onaylayamaz.
- LLM'e giden metinde kullanıcı adı / host / IP maskelenir. LLM hata verirse sistem AI'sız devam eder.

## Ortam
- Mac (Apple Silicon). Docker çalışıyor.
- **n8n ve Ollama zaten var** (`~/ai-assistant`): n8n `http://localhost:5678` (Docker içinde), Ollama native `http://localhost:11434`, model `qwen3`. Bunları bu projede tekrar kurma.
- n8n container'ı API'ye `http://host.docker.internal:5081` ile ulaşır. Bu yüzden Api portu **5081** sabit (5080 makinede başka bir servis tarafından kullanılıyor).
- FakeSap portu 5090.

## Güvenlik
- Repoya secret koyma. Local ayarlar `dotnet user-secrets` ya da `.env` (gitignore'da).
- API key, token, şifre gibi değerleri sohbete yazdırma; kullanıcı kendisi girer.
- `git push`, GitHub'da repo oluşturma ve dış servislere yazma işlemlerini kullanıcıya bırak.

## Kod stili
- C# son sürüm özellikleri serbest (primary constructor, collection expression, record).
- **Repo public olacak: kodda her şey İngilizce.** Değişken, sınıf, metot, yorum, log mesajı, hata/ProblemDetails metni, API cevapları, config anahtarları, commit mesajları ve README İngilizce.
- Yorumlar kısa; SAP karşılığı olan yerlerde işlem kodunu yaz (`// SM37`).
- Test isimleri: `Method_Condition_Expected` biçiminde.
- İstisna: LLM'in ürettiği kullanıcıya dönük metinler (ör. Türkçe günlük özet) ayar/prompt ile Türkçe olabilir; prompt'un kendisi kodda İngilizce yazılır.
