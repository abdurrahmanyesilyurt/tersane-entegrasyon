# PL/SQL örneği (Oracle Database Free)

Ana proje SQL Server kullanıyor. Bu klasör, entegrasyonun kalbindeki saklı yordamların **Oracle PL/SQL karşılığını**
içeriyor. Amaç, iki veritabanı arasındaki farkları çalışan kodla göstermek. İş mantığı aynı: transactional outbox,
idempotent gönderim, kilitlenen satırı atlayarak kuyruktan mesaj alma, toplu upsert.

```powershell
docker compose --profile oracle up -d oracle
```

```powershell
docker compose logs -f oracle
```

İlk açılışta `init/01-tersane.sh` şemayı ve paketi `tersane` kullanıcısıyla kurar, ardından testleri çalıştırır.
Logda `ALL ORACLE CHECKS PASSED` görünmeli. Testleri sonradan tekrar çalıştırmak için:

```powershell
docker compose --profile oracle exec oracle bash /opt/tersane-sql/run-tests.sh
```

Bağlantı: `localhost:15210/FREEPDB1`, kullanıcı `tersane` / `Tersane_Demo_2026` (demo değeri).

## Dosyalar

| Dosya | İçerik |
|---|---|
| [`sql/01_schema.sql`](sql/01_schema.sql) | Tablolar, identity kolonlar, sequence varsayılanı, **virtual column** (talep no), `IS JSON` kontrolü, SQL seviyesinde nesne/koleksiyon tipleri |
| [`sql/02_tersane_integration.pks`](sql/02_tersane_integration.pks) | Paket spesifikasyonu: public API ve `PRAGMA EXCEPTION_INIT` ile isimli iş hataları |
| [`sql/03_tersane_integration.pkb`](sql/03_tersane_integration.pkb) | Paket gövdesi: `submit_request`, `claim_outbox`, `requeue`, `upsert_stock` |
| [`sql/90_tests.sql`](sql/90_tests.sql) | 15 kontrol; tek transaction, sonunda `ROLLBACK` |
| [`sql/run-tests.sh`](sql/run-tests.sh) | Testler + iki oturumlu `SKIP LOCKED` testi (A satırı kilitli tutarken B beklemeden atlamalı) |

## T-SQL ↔ PL/SQL: bu projede

| İş | SQL Server (`src/Tersane.Data/Sql`) | Oracle (bu klasör) |
|---|---|---|
| Talep numarası | Sequence + `PERSISTED` computed column | Sequence `DEFAULT` + deterministik **virtual column** |
| Satırı kilitle, sonra kontrol et | `WITH (UPDLOCK, HOLDLOCK)` | `SELECT … FOR UPDATE` |
| İş kuralı hatası | `THROW 50422, …` | `RAISE_APPLICATION_ERROR(-20422, …)` + `PRAGMA EXCEPTION_INIT` |
| Hata yakalama | `BEGIN TRY … BEGIN CATCH … THROW` | `EXCEPTION WHEN DUP_VAL_ON_INDEX / NO_DATA_FOUND …` |
| Transaction | Yordam kendi açar, `XACT_ABORT ON` | Yordam commit **etmez**; transaction çağıranındır |
| JSON üretme | `FOR JSON PATH` | `JSON_OBJECT`, `JSON_ARRAYAGG … FORMAT JSON` |
| Kuyruktan alma | `UPDATE TOP(n) … WITH (READPAST)` + `OUTPUT` | Cursor `FOR UPDATE SKIP LOCKED` + `BULK COLLECT … LIMIT` + `FORALL` |
| Toplu upsert girdisi | TVP (`dbo.StockBalanceRow READONLY`) | SQL nesne tipi koleksiyonu (`t_stock_rows`) + `TABLE()` |
| Upsert | `MERGE … OUTPUT $action` | `MERGE … WHEN MATCHED THEN UPDATE … WHERE` + `SQL%ROWCOUNT` |
| Etkilenen satırları geri alma | `OUTPUT inserted.*` | `RETURNING … BULK COLLECT INTO` |
| Çağıran tarafı (.NET) | `SqlParameter`, `SqlDbType.Structured` | ODP.NET (`Oracle.ManagedDataAccess`); 23ai öncesi BOOLEAN bağlanamadığı için 0/1 `NUMBER` |

İki önemli tuzak testlerde de var:

- Oracle'da `''` **NULL'dır**. SQL Server'dan taşınan `WHERE kolon = ''` kontrolleri sessizce hiçbir satır döndürmez.
- `WHERE ROWNUM <= n … FOR UPDATE SKIP LOCKED` sınırlamayı kilitlemeden **önce** uygular. Diğer oturumların kilitlediği
  satırlar atlanınca n'den az satır gelebilir. Doğrusu cursor'dan `FETCH … LIMIT n` ile almak.
