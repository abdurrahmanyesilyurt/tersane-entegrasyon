using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace Tersane.EskiAmbar;

/// <summary>Ambar giriş fişi (warehouse goods receipt) of the legacy system.</summary>
public sealed record AmbarGirisFisi(
    string FisNo,
    DateOnly FisTarihi,
    string Depo,
    string Raf,
    string MalzemeKodu,
    decimal Miktar,
    string Birim,
    string Tedarikci,
    string IrsaliyeNo,
    bool KaliteKontrol,
    string? Aciklama,
    string Kullanici,
    DateTime KayitZamani);

public interface IFisRepository
{
    Task InitializeAsync(CancellationToken ct);

    /// <summary>Returns the receipt that already holds this waybill line, if any.</summary>
    Task<string?> FindDuplicateAsync(string irsaliyeNo, string malzemeKodu, CancellationToken ct);

    Task<string> InsertAsync(AmbarGirisFisi fis, CancellationToken ct);

    Task<IReadOnlyList<AmbarGirisFisi>> ListAsync(int top, CancellationToken ct);
}

/// <summary>
/// Plain ADO.NET on purpose: the legacy app predates EF. Every value goes through SqlParameter (no string
/// concatenation → no SQL injection), and the fiş number comes from an IDENTITY in the same INSERT.
/// </summary>
public sealed class SqlFisRepository(string connectionString) : IFisRepository
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var master = new SqlConnection(builder.ConnectionString);
                await master.OpenAsync(ct);
                await using (var create = new SqlCommand("DECLARE @sql nvarchar(300) = N'CREATE DATABASE ' + QUOTENAME(@db); IF DB_ID(@db) IS NULL EXEC (@sql);", master))
                {
                    create.Parameters.AddWithValue("@db", database);
                    await create.ExecuteNonQueryAsync(ct);
                }
                break;
            }
            catch (SqlException) when (attempt < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.AmbarGirisFisi') IS NULL
            BEGIN
                CREATE TABLE dbo.AmbarGirisFisi
                (
                    Id            int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_AmbarGirisFisi PRIMARY KEY,
                    FisNo         AS ('AG-' + RIGHT('000000' + CONVERT(varchar(10), Id), 6)) PERSISTED,
                    FisTarihi     date           NOT NULL,
                    Depo          varchar(10)    NOT NULL,
                    Raf           varchar(20)    NOT NULL,
                    MalzemeKodu   varchar(40)    NOT NULL,
                    Miktar        decimal(18, 3) NOT NULL CONSTRAINT CK_AmbarGirisFisi_Miktar CHECK (Miktar > 0),
                    Birim         varchar(10)    NOT NULL,
                    Tedarikci     nvarchar(150)  NOT NULL,
                    IrsaliyeNo    varchar(30)    NOT NULL,
                    KaliteKontrol bit            NOT NULL,
                    Aciklama      nvarchar(500)  NULL,
                    Kullanici     varchar(50)    NOT NULL,
                    KayitZamani   datetime2(0)   NOT NULL CONSTRAINT DF_AmbarGirisFisi_KayitZamani DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT UQ_AmbarGirisFisi_Irsaliye UNIQUE (IrsaliyeNo, MalzemeKodu)
                );
            END
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> FindDuplicateAsync(string irsaliyeNo, string malzemeKodu, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT FisNo FROM dbo.AmbarGirisFisi WHERE IrsaliyeNo = @irsaliye AND MalzemeKodu = @kod", conn);
        cmd.Parameters.Add("@irsaliye", System.Data.SqlDbType.VarChar, 30).Value = irsaliyeNo;
        cmd.Parameters.Add("@kod", System.Data.SqlDbType.VarChar, 40).Value = malzemeKodu;
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task<string> InsertAsync(AmbarGirisFisi f, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            INSERT dbo.AmbarGirisFisi (FisTarihi, Depo, Raf, MalzemeKodu, Miktar, Birim, Tedarikci, IrsaliyeNo, KaliteKontrol, Aciklama, Kullanici)
            OUTPUT inserted.FisNo
            VALUES (@tarih, @depo, @raf, @kod, @miktar, @birim, @tedarikci, @irsaliye, @kk, @aciklama, @kullanici)
            """, conn);
        cmd.Parameters.Add("@tarih", System.Data.SqlDbType.Date).Value = f.FisTarihi.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@depo", System.Data.SqlDbType.VarChar, 10).Value = f.Depo;
        cmd.Parameters.Add("@raf", System.Data.SqlDbType.VarChar, 20).Value = f.Raf;
        cmd.Parameters.Add("@kod", System.Data.SqlDbType.VarChar, 40).Value = f.MalzemeKodu;
        var miktar = cmd.Parameters.Add("@miktar", System.Data.SqlDbType.Decimal);
        miktar.Precision = 18;
        miktar.Scale = 3;
        miktar.Value = f.Miktar;
        cmd.Parameters.Add("@birim", System.Data.SqlDbType.VarChar, 10).Value = f.Birim;
        cmd.Parameters.Add("@tedarikci", System.Data.SqlDbType.NVarChar, 150).Value = f.Tedarikci;
        cmd.Parameters.Add("@irsaliye", System.Data.SqlDbType.VarChar, 30).Value = f.IrsaliyeNo;
        cmd.Parameters.Add("@kk", System.Data.SqlDbType.Bit).Value = f.KaliteKontrol;
        cmd.Parameters.Add("@aciklama", System.Data.SqlDbType.NVarChar, 500).Value = (object?)f.Aciklama ?? DBNull.Value;
        cmd.Parameters.Add("@kullanici", System.Data.SqlDbType.VarChar, 50).Value = f.Kullanici;
        try
        {
            return (string)(await cmd.ExecuteScalarAsync(ct))!;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601) // unique constraint: a parallel insert won the race
        {
            return await FindDuplicateAsync(f.IrsaliyeNo, f.MalzemeKodu, ct)
                ?? throw new InvalidOperationException("Mükerrer kayıt bulunamadı.", ex);
        }
    }

    public async Task<IReadOnlyList<AmbarGirisFisi>> ListAsync(int top, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP (@top) FisNo, FisTarihi, Depo, Raf, MalzemeKodu, Miktar, Birim, Tedarikci, IrsaliyeNo, KaliteKontrol, Aciklama, Kullanici, KayitZamani
            FROM dbo.AmbarGirisFisi ORDER BY Id DESC
            """, conn);
        cmd.Parameters.Add("@top", System.Data.SqlDbType.Int).Value = top;
        var list = new List<AmbarGirisFisi>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AmbarGirisFisi(
                reader.GetString(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetDecimal(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
                reader.GetBoolean(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11), reader.GetDateTime(12)));
        }
        return list;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

/// <summary>Used when no connection string is configured (local runs, RPA tests).</summary>
public sealed class InMemoryFisRepository : IFisRepository
{
    private readonly ConcurrentDictionary<(string, string), AmbarGirisFisi> _rows = new();
    private int _seq;

    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<string?> FindDuplicateAsync(string irsaliyeNo, string malzemeKodu, CancellationToken ct)
        => Task.FromResult(_rows.TryGetValue((irsaliyeNo, malzemeKodu), out var f) ? f.FisNo : null);

    public Task<string> InsertAsync(AmbarGirisFisi fis, CancellationToken ct)
    {
        var saved = _rows.GetOrAdd((fis.IrsaliyeNo, fis.MalzemeKodu), _ => fis with { FisNo = $"AG-{Interlocked.Increment(ref _seq):000000}" });
        return Task.FromResult(saved.FisNo);
    }

    public Task<IReadOnlyList<AmbarGirisFisi>> ListAsync(int top, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AmbarGirisFisi>>(_rows.Values.OrderByDescending(r => r.FisNo).Take(top).ToList());
}
