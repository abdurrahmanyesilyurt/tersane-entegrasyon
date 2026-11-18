# Görüşme notları: Yazılım Uzman Yardımcısı (19.11.2026, 14:00, Teams)

Bu notlar projeyi kendi ağzından anlatabilmen için. Cümleler kısa tutuldu, okuyup ezberlemek yerine akışı hatırlamak için kullan.

## 0. Görüşmeden 15 dakika önce

```powershell
cd C:\Users\HP\source\repos\tersane-entegrasyon
docker compose up -d
```

- Tarayıcıda dört sekme aç: `localhost:5100` (pano), `localhost:5100/entegrasyon`, `localhost:5101` (sahte ERP), `localhost:5102` (eski ambar).
- Entegrasyon sayfasında ERP kesintisi **kapalı**, hata oranı **%0** olsun.
- VS Code'da şu dosyaları açık tut: `src/Tersane.Data/Sql/04_MaterialRequestProcedures.sql`, `05_OutboxProcedures.sql`,
  `src/Tersane.Worker/Outbox/OutboxProcessor.cs`, `src/Tersane.Worker/IntegrationSetup.cs`, `src/Tersane.Rpa/AmbarBot.cs`.
- RPA'yı göstereceksen bir kez önceden çalıştır (imaj hazır olsun): `docker compose --profile rpa run --rm rpa`.
- Görüşmeden sonra: `docker compose --profile rpa down` (veriyi de silmek için `-v`).

## 1. 30 saniyelik açılış

> "İlanı okuyunca üç başlık gördüm: şirket içi .NET uygulamaları, ERP entegrasyonu ve RPA. Üretimde ASP.NET Core 8,
> PostgreSQL ve Next.js ile çalışıyorum; IFS, PL/SQL ve RPA tarafında deneyimim yok, SQL Server'da da azdı. Bu açığı
> kapatmak için bu üç başlığı tek bir senaryoda birleştiren küçük bir proje yaptım: bir tersanede malzeme talebi açılıyor,
> ERP'de satınalma talebine dönüşüyor, onay ve stok geri geliyor; API'si olmayan eski bir ambar ekranına da irsaliyeleri bir
> bot giriyor. ERP gerçek değil, IFS Cloud'un OData API tarzını taklit eden bir mock. Amacım gerçek bir entegrasyonda
> karşılaşılan sorunları, yani kesintiyi, mükerrer kaydı ve hatalı mesajın yeniden işlenmesini çözebildiğimi göstermekti."

## 2. Beş dakikalık demo akışı

1. **Pano** (`/`): açık talepler, kritik stok, hatalı ERP mesajı sayısı, senkron durumu. *"Destek ekibinin ilk bakacağı yer."*
2. **Yeni malzeme talebi**: proje seç → bloklar ve iş emirleri AJAX ile gelir (jQuery). Kalem ekle: malzeme listesi ERP'den
   senkronlanmış, yanında canlı stok. Miktara `12,5` yaz: virgüllü ondalık hem istemcide hem sunucuda tr-TR ile doğrulanıyor.
   **"Kaydet ve ERP'ye gönder"**.
3. **Talep detayı**: adımlar, outbox mesajı, entegrasyon logu. Birkaç saniyede ERP numarası gelir. *"Gönder butonu bir saklı
   yordam çağırıyor; durum değişikliği ve outbox mesajı aynı transaction'da."*
4. **Sahte ERP** (`:5101`): talep "Released" olarak görünür, "İlerlet" ile onaylanır. İç sistemde durum 10 sn içinde değişir
   (delta senkron).
5. **Hata senaryosu** (`/entegrasyon`): "ERP kesintisi"ni aç, yeni bir talep gönder. Logda 503'ler ve deneme numaraları görünür.
   Kesintiyi kapat, mesaj kendiliğinden gider. Ya da `WL-E6013-32` ile kalıcı hata göster → **Hatalı** → "Yeniden işle".
6. **Kod** (zaman kalırsa): `usp_Outbox_Claim` (READPAST), `OutboxProcessor.SendMaterialRequestAsync` (idempotency),
   `IntegrationSetup` (Polly).
7. **RPA**: `docker compose --profile rpa run --rm rpa` → `rpa-out/…/sonuc.xlsx` ve `screenshots/`. Eski formu ekranda göster.

## 3. İlan maddesi → projedeki karşılığı

| İlanda | Projede | Ne söyleyebilirim |
|---|---|---|
| **SQL** | Şema, index'ler (filtreli, unique), view'lar, CTE'ler | Üretimde PostgreSQL ile şema tasarımı ve sorgu optimizasyonu yaptım; burada aynı işleri SQL Server'da yaptım. |
| **T-SQL** | 7 saklı yordam, 2 view, inline TVF, TVP tipi, sequence, `MERGE`, `OPENJSON`, `FOR JSON`, `TRY/CATCH`, `THROW`, `READPAST` | En çok burada zaman harcadım. `usp_MaterialRequest_Submit` ve `usp_Outbox_Claim` üzerinden anlatırım. |
| **PL/SQL** | Projede yok (bkz. §7 kopya kağıdı) | "Deneyimim yok. T-SQL'de yaptığım her şeyin Oracle karşılığını çıkardım: `FOR UPDATE SKIP LOCKED`, `RAISE_APPLICATION_ERROR`, paketler, `BULK COLLECT/FORALL`." |
| **ASP.NET / ASP.NET Core** | 3 web uygulaması (MVC, Razor Pages, OData + Minimal API) + Worker | Üretimde ASP.NET Core 8 ile katmanlı Web API'lar yazıyorum. .NET Framework ile stajda iç uygulamalar geliştirdim. |
| **MVC** | `Tersane.Web`: controller'lar, Razor view'lar, tag helper'lar, view component, partial, model binding, anti-forgery | Ekranların hepsi sunucu tarafında render; jQuery yalnızca etkileşim için. |
| **Entity Framework** | EF Core 10: migration'lar, keyless entity (view), `HasDbFunction` (TVF), rowversion, `ExecuteUpdate`, `SqlQuery`, computed column | "Ne zaman EF, ne zaman saklı yordam" sorusuna hazırım (§6). |
| **React** | Projede yok; üretimde Next.js + React + TanStack Query | "React tarafını üretimde her gün kullanıyorum; bu projede ilanın vurguladığı MVC + jQuery'yi öne aldım." |
| **jQuery** | Bağımlı açılır listeler, dinamik form satırları + unobtrusive validation yeniden parse, AJAX + anti-forgery header, polling, eski formda `alert/confirm` | `wwwroot/js/material-request.js` ve `site.js`. |
| **CSS** | CSS değişkenleriyle tema, Grid/Flexbox düzen, durum rozetleri, responsive, print | Bootstrap'in üstüne kendi temam (`site.css`). Eski form bilerek eski görünümde. |
| **OOP** | Typed `ErpClient`, `DelegatingHandler`'lar (token, audit), `BackgroundService`, `IFisRepository` (SQL / in-memory), hata sınıflandırma (`ErpFailureKind`) | Arayüz arkasına alınan repository sayesinde eski sistem testte bellekte çalışıyor. |
| **Yazılım desteği** | Entegrasyon izleme ekranı, health view, log tablosu, istek/yanıt gövdesi detayı, "Yeniden işle" | "Destek verdiğim bir sistemde ilk ihtiyaç: neyin neden takıldığını loga bakarak 1 dakikada görmek." |
| **IFS ERP** | Sahte ERP: OData projection tarzı uç noktalar, OAuth2, Objstate, `ChangedAt` ile delta | "IFS deneyimim yok. Okuduğum kadarıyla IFS Cloud API'leri OData tabanlı projection'lar; mock'u o tarza göre kurdum ki entegrasyon kodu gerçeğine yakın olsun." |
| **RPA** | Playwright for .NET botu: Excel → eski form, ekran görüntüsü, log, sonuç Excel'i, idempotent tekrar çalışma | "Ticari RPA araçlarını (UiPath, Power Automate) kullanmadım. Seçici, bekleme, hata yönetimi ve kuyruk kavramları aynı; kod tabanlı yaptım." |
| **.NET Core projeleri** | .NET 10 LTS, 7 proje, tek çözüm | .NET 8'in desteği 10 Kasım 2026'da bitti, bu yüzden yeni projeyi .NET 10 LTS'te açtım. Kod kalıpları ASP.NET Core 8 ile aynı. |
| **Entegrasyon: analiz, tasarım, geliştirme, test** | Outbox, idempotency, iki seviyeli retry, delta senkron, 36 test (Testcontainers ile gerçek SQL Server) | Sıra diyagramı README'de. Testler kesinti, çökme ve mükerrer senaryolarını da kapsıyor. |
| **İngilizce dokümantasyon** | Koddaki yorumlar ve commit mesajları İngilizce; OData, Polly, Playwright dokümanlarından çalıştım | |

## 4. Eksiklerim: dürüst ve kısa

- **IFS:** "Kullanmadım. Projede taklit ettiğim şey yalnızca API tarzı. İlk haftalarda IFS'in veri modelini ve entegrasyon
  yöntemlerini öğrenmem gerekecek. Entegrasyonun temel sorunlarını ise biliyorum ve çözebiliyorum."
- **PL/SQL:** "Yazmadım. SQL ve T-SQL bilgim aktarılabilir. Farkları listeledim (§7). En önemli iki fark: Oracle'da boş string
  NULL'dır ve okuyucular yazanları bloklamaz (MVCC)."
- **T-SQL:** "Az kullanmıştım, bu projede yoğun çalıştım. Saklı yordamları baştan sona kendim anlatabilirim."
- **RPA araçları:** "UiPath kullanmadım. Web için kod tabanlı otomasyon yazdım; masaüstü uygulamalar için UiPath veya
  Power Automate Desktop gerekir, onu öğrenirim."
- **Projeyi nasıl yaptın / ne kadar sürdü?** Dürüst ol: "İlana hazırlık için kısa sürede, yapay zekâ destekli geliştirme
  araçlarıyla yaptım. Mimari kararları ben verdim, kodu tek tek inceledim ve çalıştırarak doğruladım. Buradaki her parçayı
  anlatabilirim." (Üretimde zaten yapay zekâ ajanı geliştirdiğin için bu bir artı.)

## 5. Mimariyi bir cümleyle anlat

- **İç uygulama** sadece kendi veritabanına yazar. ERP'yi hiç beklemez.
- **Outbox**: ERP'ye gidecek her şey aynı transaction'da bir tabloya yazılır. Worker oradan alıp gönderir.
- **Worker** iki yönlü çalışır: outbox'ı ERP'ye iter, ERP'deki değişiklikleri (parça, stok, talep durumu) watermark ile çeker.
- **RPA** API'si olmayan sistem için son çare. Excel'den okur, formu insan gibi doldurur, her adımı kanıtlar.

## 6. Olası sorular ve kısa cevaplar

**Outbox neden? Doğrudan ERP'yi çağırsan olmaz mı?**
Doğrudan çağırırsan iki sorun çıkar. DB commit olur ama ERP çağrısı düşerse talep "gönderildi" görünür ama ERP'de yoktur.
Ya da ERP yazar ama DB rollback olursa ERP'de sahipsiz kayıt kalır. Outbox ile iş değişikliği ve mesaj tek transaction'da
yazılır, gönderim ayrı ve tekrar denenebilir. Kullanıcı ERP yavaş ya da kapalıyken de çalışmaya devam eder.

**Idempotency'yi nasıl sağladın?**
Üç katmanda. (1) `IdempotencyKey` UNIQUE: aynı talep iki kez kuyruğa giremez, saklı yordam ikinci çağrıda mevcut mesajı döner.
(2) Worker ERP'ye yazmadan önce `ExternalRef = talep no` ile arar. ERP yazdıktan sonra worker çökse bile tekrar denemede yeni
kayıt açılmaz. (3) ERP 409 dönerse bu başarı sayılır, mevcut kayıt bulunur. Teslim garantisi "en az bir kez" (at-least-once),
idempotency ile sonuç "tam bir kez" gibi davranır.

**İki worker aynı mesajı alırsa?**
Alamaz. `usp_Outbox_Claim` içinde `UPDATE TOP(n)` + `UPDLOCK, READPAST, ROWLOCK` var. Bir worker'ın kilitlediği satırı diğeri
atlar. Her mesajın bir kira süresi (lease) var. Worker çökerse süre dolunca mesaj tekrar alınır. Tamamlarken
`WHERE LockedBy = @ben` kontrolü (fencing) var, kirası başkasına geçmiş worker sonucu yazamaz. Bunun testi de var: 6 paralel
worker 60 mesajı çakışmadan alıyor.

**Polly ne yapıyor, outbox'taki yeniden deneme ne yapıyor? İkisi neden var?**
Polly kısa süreli aksaklıklar için: birkaç saniye içinde 3 deneme (üstel bekleme + jitter), circuit breaker, timeout. ERP
5 dakika kapalıysa Polly'nin denemeleri biter. O zaman outbox devreye girer: mesaj 15 sn, 30 sn, 60 sn… sonra tekrar denenir.
5 denemede olmazsa "Hatalı" olur ve operatör ekrandan yeniden işler. Hangi hatanın tekrar deneneceği de ayrılmış: 5xx, 408,
429 ve ağ hataları geçici. 400 gibi veri hatası kalıcı, tekrar denenmez.

**Circuit breaker'ı açıkla.**
Son 30 saniyede çağrıların yarısından fazlası hata verdiyse (en az 8 çağrıda) devre 15 saniye açılır. Bu sürede ERP'ye hiç
gidilmez, hemen "geçici hata" dönülür. Zaten zorlanan ERP'yi daha fazla yüklememek için.

**Delta senkronizasyon nasıl çalışıyor?**
Her akışın (parça, stok, talep durumu) `SyncStates` tablosunda bir watermark'ı var. ERP'ye `$filter=ChangedAt gt {watermark}`
ile soruyorum, `@odata.nextLink` ile tüm sayfaları okuyorum, sonra watermark'ı en büyük `ChangedAt`'e çekiyorum. Saat farkı
ya da aynı anda yazılan kayıtlar kaçmasın diye watermark'tan birkaç saniye geriden başlıyorum. Upsert'ler idempotent
olduğu için aynı satırı iki kez okumak zarar vermiyor.

**Neden bazı işler EF Core'da, bazıları saklı yordamda?**
Ekran CRUD'u, ilişkili okuma ve tipli sorgular için EF Core daha hızlı geliştirilir ve daha okunur. Saklı yordamı şu
durumlarda kullandım: kilitlenme davranışının önemli olduğu kuyruk (READPAST), tek transaction'da birden fazla tabloyu
değiştiren iş kuralı (submit), set tabanlı toplu işler (MERGE ile yüzlerce stok satırı tek seferde). Bir de DBA'nın SQL olarak
inceleyebilmesi için. EF Core'dan view'ı keyless entity olarak, TVF'yi `HasDbFunction` ile kullandım.

**TVP mi, SqlBulkCopy mi, tek tek INSERT mi?**
Yüzlerce satırlık bir sayfa için TVP: tek round trip, tek MERGE, tipli. On binlerce satır yükleme için `SqlBulkCopy` ile bir
staging tablosu ve ardından MERGE daha hızlı olur. Tek tek INSERT her satır için ayrı bir round trip demek.

**MERGE'ün bilinen sorunları?**
Eşzamanlı çalışınca aynı anahtarı iki oturum birden ekleyebilir. Bu yüzden `WITH (HOLDLOCK)` kullandım. Ayrıca MERGE'ü,
yalnızca değer değiştiyse güncelleyecek şekilde yazdım. Böylece tekrar gelen veri hiçbir şeyi değiştirmiyor, log da şişmiyor.

**`sp_` öneki neden yok?**
`sp_` sistem yordamları için ayrılmış. SQL Server bu önekli bir yordamı önce `master`'da arar, hem yavaştır hem karışıklık
yaratır. `usp_` kullandım.

**`SET XACT_ABORT ON` ne işe yarıyor?**
Transaction içinde bir çalışma zamanı hatası olursa transaction'ın tamamı geri alınır. Bu olmadan bazı hatalarda
transaction açık kalabilir ve yarım iş commit edilebilir. `TRY/CATCH` + `THROW` ile hata orijinal numarasıyla uygulamaya döner.
Uygulama 50000 üstü numaraları iş kuralı mesajı olarak gösteriyor.

**Bulduğun ilginç bir hata?**
Üç tane var. (1) `LINENO` T-SQL'de ayrılmış kelime, `[LineNo]` diye yazmak gerekti. (2) `sqlcmd` varsayılan olarak
`QUOTED_IDENTIFIER OFF` çalışıyor. Filtreli index'i olan tabloya o oturumda UPDATE atılamadı; `-I` gerekti. Eski betiklerin
neden patladığını açıklar. (3) Kaos ekranında `0.3` değeri tr-TR kültüründe 3 olarak okunuyordu, çünkü nokta binlik
ayırıcı. Değeri yüzde olarak tam sayı taşıdım. Türkçe ortamda "i".ToUpper() = "İ" sorunu için de kodları `ToUpperInvariant`
ile normalize ediyorum.

**RPA botu neden kırılmıyor?**
Sabit `Sleep` yok. Bot belirli bir durumu bekliyor: raf seçeneği yüklendi mi, malzeme sorgusu bitti mi, sonuç etiketi çıktı mı.
Seçiciler ID yerine görünen etiketle yazıldı (`GetByLabel`), WebForms'un ürettiği ID'ler değişse de çalışır. Oturum düşerse
yeniden giriş yapar. Timeout'ta satırı yeni sayfada bir kez daha dener. İş kuralı reddini denemez, kaydeder. Her satır için
ekran görüntüsü ve isteğe bağlı Playwright trace'i var. Aynı dosyayı tekrar çalıştırmak mükerrer kayıt üretmez.

**RPA ne zaman doğru, ne zaman yanlış çözüm?**
API veya veritabanı erişimi varsa RPA son tercih: ekran değişince kırılır, yavaştır. Doğru olduğu yer: API'si olmayan eski
sistem, dış bir portal, ya da entegrasyon projesinin bütçesi çıkmayan, sık değişmeyen ve hacmi düşük işler. RPA'yı da
izlenebilir yapmak gerekir: log, ekran görüntüsü, sonuç raporu, tekrar çalıştırılabilirlik.

**Güvenlik?**
SQL'de her değer parametreyle gidiyor (EF, `SqlParameter`, `FormattableString`), string birleştirme yok. MVC'de tüm
POST'larda anti-forgery doğrulaması var, AJAX çağrıları da token'ı header'da taşıyor. ERP'ye OAuth2 client credentials ile
bağlanılıyor, token önbellekte tutuluyor. Parolalar demo değerleri. Gerçek ortamda secret store'dan (Key Vault, Docker
secrets) okunur. İç uygulama gerçekte Windows Authentication / AD ile korunurdu.

**Bunu canlıya nasıl alırsın?**
İmajlar CI'da derlenip registry'ye itilir. Migration ayrı bir job olarak çalışır (compose'daki `db-migrate` gibi). Worker
birden fazla kopya çalışabilir, kuyruk buna göre tasarlandı. İzleme için health endpoint, `vw_IntegrationHealth` ve log
tablosu var. Eşik aşılınca (ör. 10 dakikadan eski bekleyen mesaj) alarm kurulur. Log tablosu `usp_IntegrationLog_Purge` ile
parça parça temizlenir.

**Gerçek IFS'e bağlansaydın neyi değiştirirdin?**
`ErpClient` içindeki URL'ler ve alan adları gerçek projection'lara göre değişir. Kimlik doğrulama IFS'in kimlik sağlayıcısından
alınan client'la yapılır. ETag/optimistic concurrency kurallarına uyulur. Veri eşlemesi (malzeme kodu, birim, proje/aktivite)
iş birimiyle birlikte analiz edilir. Outbox, idempotency, retry, log ve senkron tasarımı aynen kalır. Bu yüzden ERP'yi bir
istemci sınıfının arkasına aldım.

**Test stratejin?**
Birim testten çok entegrasyon testi var, çünkü riskli kısım SQL ve HTTP davranışı. Testcontainers her çalıştırmada gerçek
SQL Server 2022 açıyor, her test sınıfı kendi veritabanında çalışıyor. Sahte ERP bellekte (`WebApplicationFactory`) çalışıyor
ve kaos ayarlarıyla 503, kesinti ve token iptali deneniyor. RPA testi gerçek Kestrel portunda headless Chromium ile çalışıyor.
CI aynı testleri çalıştırıyor, ardından compose ile tüm sistemi kaldırıp uçtan uca duman testi yapıyor.

## 7. T-SQL ↔ PL/SQL kopya kağıdı

| Konu | SQL Server (T-SQL) | Oracle (PL/SQL) |
|---|---|---|
| Hata fırlatma | `THROW 50422, N'mesaj', 1;` | `RAISE_APPLICATION_ERROR(-20422, 'mesaj');` |
| Hata yakalama | `BEGIN TRY … END TRY BEGIN CATCH … END CATCH` | `BEGIN … EXCEPTION WHEN NO_DATA_FOUND THEN … WHEN OTHERS THEN … END;` |
| Kuyruktan kilitsiz alma | `WITH (UPDLOCK, READPAST)` | `SELECT … FOR UPDATE SKIP LOCKED` |
| Sıradaki numara | `NEXT VALUE FOR dbo.seq` | `seq.NEXTVAL` (12c+ `GENERATED AS IDENTITY` de var) |
| Upsert | `MERGE … OUTPUT $action` | `MERGE` (OUTPUT yok; gerekirse ayrı sayım) |
| Toplu veri | TVP (`READONLY` parametre) | Koleksiyonlar + `BULK COLLECT` / `FORALL` |
| Kod gruplama | Şema + yordamlar | **Paket** (spec + body), paket değişkenleri |
| Transaction | Varsayılan autocommit | Açık transaction `COMMIT`'e kadar sürer; DDL otomatik commit eder |
| Okuma/yazma kilidi | Varsayılan READ COMMITTED kilitlidir (RCSI açılmadıysa okuyucu yazanı bekler) | MVCC: okuyucu yazanı bloklamaz |
| Boş string | `''` ile `NULL` farklıdır | `''` **NULL'dır** |
| İlk n satır | `TOP (n)` | `FETCH FIRST n ROWS ONLY` (eski: `ROWNUM`) |
| Null yedeği / birleştirme | `ISNULL`, `+` | `NVL`, `\|\|` |
| Zaman | `SYSUTCDATETIME()` | `SYSTIMESTAMP`, `SYS_EXTRACT_UTC(SYSTIMESTAMP)` |
| JSON | `FOR JSON`, `OPENJSON` | `JSON_OBJECT/JSON_ARRAYAGG`, `JSON_TABLE` |
| Geçici tablo | `#temp`, tablo değişkeni | Global temporary table, koleksiyon |

## 8. IFS hakkında (okuduğum kadarıyla, deneyim değil)

Bunları "okuduğum kadarıyla" diye söyle, kesin bilgi gibi sunma.

- IFS Cloud'da dış sistemler için API katmanı OData v4 tabanlı **projection**'lar. Mock'taki
  `/…/projection/v1/{Projection}.svc/{EntitySet}` yolu bu tarzı taklit ediyor.
- Klasik IFS Applications sürümlerinde iş mantığı Oracle veritabanında PL/SQL paketlerinde (`…_API` adlı paketler). Bu yüzden
  IFS kullanan şirketlerde PL/SQL bilgisi isteniyor.
- Entegrasyon için IFS'in kendi araçları da var (ör. IFS Connect, olay tabanlı tetikleyiciler). Hangisinin kullanıldığını
  görüşmede sormak iyi olur.

## 9. Onlara sorulabilecek sorular

- IFS hangi sürümde? Entegrasyonlar projection/REST üzerinden mi, IFS Connect mi, doğrudan veritabanı mı?
- RPA için hangi aracı kullanıyorsunuz, botlar hangi süreçlerde çalışıyor?
- .NET tarafında projeler .NET Framework mı, .NET Core/8 mi? Yükseltme planı var mı?
- Yazılım destek talepleri nasıl geliyor (ticket, öncelik, nöbet)?
- İlk üç ayda benden beklenen somut iş ne olur?

## 10. Komut hatırlatmaları

```powershell
docker compose up -d --build                 # her şeyi kaldır
docker compose ps                            # durum
docker compose logs worker -f                # worker logu
docker compose --profile rpa run --rm rpa    # RPA botu
dotnet test                                  # 36 test (Docker açık olmalı)
docker compose --profile rpa down -v         # her şeyi sil
```
