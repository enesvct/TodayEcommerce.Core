# TodayEcommerce.Core

[![CI](https://github.com/enesvct/TodayEcommerce.Core/actions/workflows/ci.yml/badge.svg)](https://github.com/enesvct/TodayEcommerce.Core/actions/workflows/ci.yml)

**[Türkçe](#türkçe) · [English](#english)**

---

## Türkçe

### Bu proje ne?

Bir e-ticaret altyapısının çekirdeğini, yani ürünleri, siparişleri ve stoğu yöneten kısmını sıfırdan yazdığım bir .NET 8 projesi.

Amacım bir "CRUD uygulaması" yapmak değildi. E-ticarette gerçekten karşılaşılan problemleri küçük bir kod tabanında tek tek çözmek ve neden öyle çözdüğümü anlatabilmek istedim:

- İki kişi aynı anda son ürünü almaya çalışırsa ne olur?
- Kullanıcı "Satın al" butonuna iki kez basarsa?
- Sipariş kaydedildi ama arama motoru o anda kapalıysa?
- Müşteri iptal ederken arka planda stok ayrılıyorsa?

Bu sorulara cevap verirken kullandığım yapılar aşağıda. Her birinin neden orada olduğunu da yazmaya çalıştım.

### Neler var?

- **Ürünler:** oluşturma, fiyat değiştirme, Redis cache ile hızlı okuma, Elasticsearch ile Türkçe tam metin arama ("telefn" yazınca "telefon" bulunur)
- **Siparişler:** sipariş verme, "Siparişlerim" listesi (sayfalı), iptal (onaylanmış siparişler dahil)
- **Stok:** sipariş verildikten sonra arka planda ayrılır, iptalde geri verilir
- **Kimlik doğrulama:** JWT. API sadece token'ı doğrular, kullanıcı yönetimi bir kimlik sağlayıcının işi.
- **Çift sipariş koruması:** `Idempotency-Key` header'ı
- **İzlenebilirlik:** `/health/live` ve `/health/ready`, her cevapta `X-Correlation-Id`, production'da JSON log

### Çalıştırma

Gerekenler: .NET 8 SDK, Docker ve `dotnet-ef` aracı (`dotnet tool install --global dotnet-ef`).

```bash
# 1. Altyapı: PostgreSQL, Redis, Elasticsearch
cp .env.example .env                      # içindeki şifreyi değiştir
docker compose up -d

# 2. Ayarlar (bu dosya git'e gitmez)
cp src/TodayEcommerce.Core.WebApi/appsettings.Development.example.json \
   src/TodayEcommerce.Core.WebApi/appsettings.Development.json
#    - <POSTGRES_PASSWORD> yerine .env'deki şifreyi yaz
#    - <JWT_SIGNING_KEY> yerine en az 32 karakterlik bir anahtar yaz (örn. `openssl rand -base64 48`)

# 3. Veritabanı
dotnet ef database update --project src/TodayEcommerce.Core.Infrastructure \
                          --startup-project src/TodayEcommerce.Core.WebApi

# 4. API
dotnet run --project src/TodayEcommerce.Core.WebApi --launch-profile http
```

Sonra **http://localhost:5144/swagger** adresini aç:

1. `POST /dev/token` ile `{ "user": "admin" }` için bir token al, sağ üstteki **Authorize** butonuna yapıştır.
2. Birkaç ürün oluştur.
3. Aynı şekilde `ayse` ya da `mehmet` olarak token al ve sipariş ver. Birkaç saniye içinde siparişin `Confirmed` olur, stok da düşer.

`/dev/token` sadece Development ortamında var. Başka bir ortamda bu endpoint hiç oluşmuyor, bunu bir test de kontrol ediyor. Arka plan işlerini **http://localhost:5144/hangfire** adresinden izleyebilirsin.

Testler için sadece Docker'ın açık olması yeterli: `dotnet test`. Integration testleri kendi PostgreSQL container'larını kendileri açıp kapatıyor.

### Mimari

İki eksende ayrılmış bir **modüler monolit**:

```
                 Catalog (ürün, stok)      Ordering (sipariş)
               ┌────────────────────────┬────────────────────────┐
  WebApi       │  controller'lar, auth, health, hata → HTTP      │
  Application  │  use case'ler (CQRS), event handler'lar         │
  Domain       │  Product, StockReservation │ Order, OrderItem   │
  Infrastructure  EF Core, PostgreSQL, Redis, Elasticsearch, outbox, Hangfire
               └────────────────────────┴────────────────────────┘
```

- **Yatay kural (Clean Architecture):** bağımlılıklar sadece içe doğru. Domain hiçbir framework'ü tanımıyor.
- **Dikey kural (modüller):** Catalog ve Ordering birbirinin içini bilmiyor. Sadece birbirinin `Contracts` klasöründeki mesajlarla konuşuyorlar.

İki kural da mimari testleriyle korunuyor. Kural bozulursa build kırmızı yanıyor.

### Bir siparişin hikayesi

```
Ayşe "Satın al"a basar
  → Sipariş kaydedilir (Placed). "Sipariş verildi" mesajı AYNI transaction içinde outbox tablosuna yazılır.
  → API hemen 201 döner. Ayşe beklemez.

Arka planda (Hangfire, outbox'ı birkaç saniyede bir boşaltır):
  → Catalog stoğu ayırır ve bir StockReservation kaydı tutar → "Stok ayrıldı"
  → Ordering siparişi onaylar (Confirmed)
  → Arama indeksi ve cache güncellenir, müşteriye bildirim gider
```

Bu zincirin bütün log satırları, Ayşe'nin isteğiyle aynı TraceId'yi taşıyor. Log'da tek bir id ile arama yapınca isteğin ve arkasından gelen bütün işlerin tamamı görünüyor.

### Neden böyle yaptım?

**Outbox pattern.** Sipariş veritabanına yazılıp sonra arama motoruna ayrıca gönderilseydi, ikisinin arasında bir çökme yaşandığında veriler birbirinden kopardı. Bunu bir demo sırasında gerçekten yaşadım: Bir ürün veritabanında vardı ama aramada hiç çıkmıyordu. Şimdi event'ler değişiklikle aynı transaction içinde kaydediliyor ve hata olursa artan aralıklarla (5 saniyeden 1 saate kadar) tekrar deneniyor.

**Stok neden hemen düşmüyor?** Popüler bir üründe her sipariş isteğinin aynı satırı kilitlemesini istemedim. İstek sadece siparişi kaydediyor. Stok arka planda, "ya hepsi ya hiçbiri" kuralıyla ayrılıyor. Bunun bedeli **eventual consistency**: Sipariş birkaç saniye `Placed` durumunda bekliyor.

**Son ürün yarışı.** İki istek aynı ürünü aynı anda güncellemeye çalışırsa PostgreSQL'in `xmin` satır versiyonu ikinciyi reddediyor. Bunun yanında stok için bir `CHECK (stock >= 0)` kısıtı da var. Domain kodu bir gün atlansa bile veritabanı negatif stoğa izin vermiyor.

**Saga ve telafi.** Catalog ile Ordering ayrı ayrı çalıştığı için aralarında "geri al" (rollback) diye bir şey yok. Mesajlar çaprazlanırsa, yani müşteri iptal ederken stok tam o anda ayrılmışsa, Ordering bunu fark ediyor ve stoğun geri verilmesini istiyor. Bu senaryoyu mesajları bilerek ters sırada teslim eden bir testle doğruladım.

**Idempotency key siparişle aynı transaction'da.** Key'i Redis'te tutsaydım "sipariş kaydedildi ama key kaydedilmedi" durumu mümkün olurdu, outbox'ın çözdüğü problemin aynısı. Aynı key ile aynı anda gelen 10 istek, veritabanının primary key'i sayesinde tek bir siparişe dönüşüyor.

**Başkasının siparişi neden 404?** 403 dönseydi, "bu id'de bir sipariş var ama senin değil" demiş olurdum. 404 ile var olan ve olmayan siparişler dışarıdan ayırt edilemiyor.

**Health check'te Elasticsearch neden "Degraded"?** Arama çökünce bütün instance'ları trafikten çekmek, bütün mağazayı kapatmak demek olurdu. Oysa sipariş vermek hâlâ mümkün. Sadece PostgreSQL kritik.

**Daha az paket.** Elasticsearch için resmi client yerine ince bir HttpClient adaptörü yazdım. Log için Serilog yerine .NET'in kendi JSON formatter'ını kullandım. MediatR'ı lisansı değiştiği için 12.5'te sabitledim.

### Testler

| Proje | Ne test ediyor | Sayı |
|---|---|---|
| Domain.UnitTests | iş kuralları (Money, Order, Product, StockReservation) | 59 |
| Application.UnitTests | use case'ler, saga, idempotency (bellekte) | 51 |
| ArchitectureTests | katman ve modül sınırları | 7 |
| Infrastructure.IntegrationTests | gerçek PostgreSQL: outbox, eşzamanlılık, iz takibi | 19 |
| WebApi.IntegrationTests | gerçek HTTP: auth, 401/403/404, health, idempotency | 31 |

Önemli testlerin bir kısmını bilerek koruduğu kodu bozarak doğruladım. Örneğin `/dev/token` satırını Development bloğunun dışına taşıdığımda üç test kırmızıya döndü. Eşzamanlılık kontrolünü kaldırdığımda 10 paralel istekte 500 hataları çıktı.

### Bilinçli olarak yapmadıklarım

- **Outbox temizliği:** İşlenmiş mesajlar şimdilik silinmiyor. Planım parti parti silen bir job yazmak, hacim çok büyürse de tabloyu tarihe göre bölmek (partitioning). Idempotency kayıtları için de aynısı geçerli.
- **Sipariş verirken Catalog okuması:** Fiyat ve isim snapshot'ı için Ordering, Catalog'u hâlâ doğrudan okuyor (yazmıyor). Mimari testinde bu istisna isim verilerek yazılı. Tam ayrışma için Catalog'un bir okuma contract'ı yayınlaması gerekiyor.
- **Sepet, ödeme, kargo:** kapsam dışı.
- **Hangfire paneli** sadece Development'ta açık. Production'da yetkilendirme gerekir.
- **Para birimi:** `Money` tutarları 2 ondalığa yuvarlıyor. JPY ya da KWD gibi para birimleri için bunun genişletilmesi gerekir.
- **Bildirimler** şimdilik sadece log'a yazılıyor. Gerçek e-posta gönderilecek olsaydı, at-least-once teslim yüzünden "bu bildirimi gönderdim mi?" kaydı tutmak gerekirdi.

### Sırada ne var?

- .NET 8'in desteği Kasım 2026'da bitiyor. **.NET 10'a geçiş** ilk iş.
- API'yi de container'a alıp her şeyi tek bir `docker compose up` ile çalıştırmak, demo verisi eklemek.
- Bozuk JSON gönderildiğinde 400 cevabında iç tip adlarının görünmesini engellemek.

---

## English

### What is this?

A .NET 8 project where I built the core of an e-commerce backend from scratch: products, orders and stock.

I didn't want to build "a CRUD app". I wanted to take the problems e-commerce systems actually run into, solve them one by one in a small codebase, and be able to explain why:

- What happens when two people try to buy the last item at the same time?
- What if a user hits "Buy" twice?
- What if the order is saved but the search engine happens to be down?
- What if the customer cancels while stock is being reserved in the background?

Below is what I used to answer those questions, and why each piece is there.

### Features

- **Products:** create, change price, fast reads through a Redis cache, Turkish full-text search with Elasticsearch (typing "telefn" still finds "telefon")
- **Orders:** place an order, "my orders" (paged), cancel (confirmed orders too)
- **Stock:** reserved in the background after an order is placed, returned when it is cancelled
- **Authentication:** JWT. The API only validates tokens; managing users is an identity provider's job.
- **Double-order protection:** `Idempotency-Key` header
- **Observability:** `/health/live` and `/health/ready`, an `X-Correlation-Id` on every response, JSON logs in production

### Running it

You need the .NET 8 SDK, Docker and the `dotnet-ef` tool (`dotnet tool install --global dotnet-ef`).

```bash
# 1. Infrastructure: PostgreSQL, Redis, Elasticsearch
cp .env.example .env                      # change the password inside
docker compose up -d

# 2. Settings (this file is git-ignored)
cp src/TodayEcommerce.Core.WebApi/appsettings.Development.example.json \
   src/TodayEcommerce.Core.WebApi/appsettings.Development.json
#    - replace <POSTGRES_PASSWORD> with the password from .env
#    - replace <JWT_SIGNING_KEY> with a key of at least 32 characters (e.g. `openssl rand -base64 48`)

# 3. Database
dotnet ef database update --project src/TodayEcommerce.Core.Infrastructure \
                          --startup-project src/TodayEcommerce.Core.WebApi

# 4. API
dotnet run --project src/TodayEcommerce.Core.WebApi --launch-profile http
```

Then open **http://localhost:5144/swagger**:

1. Call `POST /dev/token` with `{ "user": "admin" }` and paste the token into **Authorize** (top right).
2. Create a few products.
3. Get a token for `ayse` or `mehmet` the same way and place an order. Within a few seconds it becomes `Confirmed` and the stock goes down.

`/dev/token` only exists in Development. In any other environment the endpoint isn't mapped at all, and a test checks that. You can watch the background jobs at **http://localhost:5144/hangfire**.

For the tests you only need Docker running: `dotnet test`. The integration tests start and stop their own PostgreSQL containers.

### Architecture

A **modular monolith**, split along two axes:

```
                 Catalog (products, stock)  Ordering (orders)
               ┌────────────────────────┬────────────────────────┐
  WebApi       │  controllers, auth, health, errors → HTTP       │
  Application  │  use cases (CQRS), event handlers               │
  Domain       │  Product, StockReservation │ Order, OrderItem   │
  Infrastructure  EF Core, PostgreSQL, Redis, Elasticsearch, outbox, Hangfire
               └────────────────────────┴────────────────────────┘
```

- **Horizontal rule (Clean Architecture):** dependencies only point inward. The domain doesn't know any framework.
- **Vertical rule (modules):** Catalog and Ordering don't know each other's internals. They only talk through the messages in each other's `Contracts` folder.

Both rules are enforced by architecture tests. If someone breaks one, the build goes red.

### The life of an order

```
Ayşe clicks "Buy"
  → The order is saved (Placed). An "order placed" message is written to the outbox table in the SAME transaction.
  → The API answers 201 right away. Ayşe doesn't wait.

In the background (Hangfire drains the outbox every few seconds):
  → Catalog reserves the stock and keeps a StockReservation record → "stock reserved"
  → Ordering confirms the order (Confirmed)
  → The search index and the cache are updated, and the customer is notified
```

Every log line in that chain carries the same TraceId as Ayşe's request. Searching the logs for that one id shows the request together with all the work it caused.

### Why I built it this way

**Outbox pattern.** If an order were written to the database and then separately sent to the search engine, a crash in between would leave the two out of sync. I actually ran into this during a demo: a product existed in the database but never showed up in search. Now events are stored in the same transaction as the change, and failures are retried with growing delays (from 5 seconds up to an hour).

**Why isn't stock decreased right away?** I didn't want every order request to lock the same row of a popular product. The request only saves the order. Stock is reserved in the background, all or nothing. The price is **eventual consistency**: an order stays `Placed` for a few seconds.

**The last-item race.** If two requests try to update the same product at the same time, PostgreSQL's `xmin` row version rejects the second one. On top of that there's a `CHECK (stock >= 0)` constraint, so even if the domain code were bypassed one day, the database still wouldn't allow negative stock.

**Saga and compensation.** Catalog and Ordering work separately, so there is no rollback between them. If messages cross, meaning the customer cancels exactly while the stock is being reserved, Ordering notices and asks for the stock back. I verified this with a test that delivers the messages in the wrong order on purpose.

**The idempotency key lives in the same transaction as the order.** If I kept the key in Redis, "order saved but key not saved" would be possible: the same problem the outbox solves. Ten simultaneous requests with the same key end up as exactly one order, thanks to the database's primary key.

**Why is someone else's order a 404?** A 403 would say "there is an order with this id, it just isn't yours". With a 404, existing and non-existing orders look exactly the same from outside.

**Why is Elasticsearch only "Degraded" in the health check?** Pulling every instance out of traffic because search is down would take the whole shop down, while placing orders still works. Only PostgreSQL is critical.

**Fewer packages.** A thin HttpClient adapter instead of the official Elasticsearch client, and .NET's own JSON log formatter instead of Serilog. MediatR is pinned to 12.5 because its license changed after that.

### Tests

| Project | What it tests | Count |
|---|---|---|
| Domain.UnitTests | business rules (Money, Order, Product, StockReservation) | 59 |
| Application.UnitTests | use cases, saga, idempotency (in memory) | 51 |
| ArchitectureTests | layer and module boundaries | 7 |
| Infrastructure.IntegrationTests | real PostgreSQL: outbox, concurrency, tracing | 19 |
| WebApi.IntegrationTests | real HTTP: auth, 401/403/404, health, idempotency | 31 |

I checked several of the important tests by deliberately breaking the code they protect. For example, moving the `/dev/token` line out of the Development block turned three tests red. Removing the concurrency handling produced 500s for ten parallel requests.

### Things I deliberately left out

- **Outbox cleanup:** processed messages aren't deleted yet. The plan is a job that deletes them in small batches, and table partitioning by date if volume grows a lot. The same goes for idempotency records.
- **Reading the catalog when placing an order:** for the price and name snapshot, Ordering still reads (never writes) the Catalog directly. The architecture test names this exception explicitly. A full split needs a read contract published by the Catalog.
- **Cart, payment, shipping:** out of scope.
- **Hangfire dashboard** is only available in Development. Production would need authorization.
- **Currency:** `Money` rounds amounts to 2 decimals. Currencies like JPY or KWD would need this extended.
- **Notifications** only go to the log for now. If real e-mails were sent, at-least-once delivery would require keeping a "did I already send this?" record.

### What's next

- .NET 8 support ends in November 2026, so **moving to .NET 10** comes first.
- Containerizing the API too, so everything runs with a single `docker compose up`, plus some demo data.
- Stopping internal type names from showing up in 400 responses when malformed JSON is sent.
