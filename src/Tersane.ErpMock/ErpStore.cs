namespace Tersane.ErpMock;

public enum CreateOutcome { Created, Duplicate, Invalid }

public sealed record CreateResult(CreateOutcome Outcome, PurchaseRequisition? Requisition, string? ErrorCode = null, string? ErrorMessage = null);

/// <summary>
/// In-memory state of the mock ERP. Everything is guarded by one lock and callers only ever get copies,
/// so OData can enumerate safely while the simulation mutates the data.
/// </summary>
public sealed class ErpStore
{
    public const string Site = "TRS1";

    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, InventoryPart> _parts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InventoryPartInStock> _stock = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PurchaseRequisition> _requisitions = new(StringComparer.Ordinal);
    private DateTimeOffset _lastStamp = DateTimeOffset.MinValue;
    private int _requisitionSeq;

    public ErpStore(TimeProvider clock)
    {
        _clock = clock;
        Reset();
    }

    public List<InventoryPart> Parts() { lock (_gate) return _parts.Values.Select(p => p.Clone()).ToList(); }

    public List<InventoryPartInStock> Stock() { lock (_gate) return _stock.Values.Select(s => s.Clone()).ToList(); }

    public List<PurchaseRequisition> Requisitions() { lock (_gate) return _requisitions.Values.Select(r => r.Clone()).ToList(); }

    public CreateResult CreateRequisition(PurchaseRequisition input)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(input.ExternalRef))
                return Invalid("MISSING_EXTERNAL_REF", "ExternalRef zorunludur.");

            var existing = _requisitions.Values.FirstOrDefault(r => r.ExternalRef == input.ExternalRef);
            if (existing is not null)
                return new CreateResult(CreateOutcome.Duplicate, existing.Clone(), "DUPLICATE_EXTERNAL_REF",
                    $"ExternalRef '{input.ExternalRef}' zaten {existing.RequisitionNo} numaralı talepte kullanılmış.");

            if (input.Lines.Count == 0)
                return Invalid("NO_LINES", "Satınalma talebinde en az bir satır olmalı.");

            foreach (var line in input.Lines)
            {
                if (!_parts.TryGetValue(line.PartNo, out var part))
                    return Invalid("PART_NOT_FOUND", $"Malzeme {line.PartNo} ERP'de tanımlı değil.");
                if (part.PartStatus != "A")
                    return Invalid("PART_BLOCKED", $"Malzeme {line.PartNo} satınalmaya kapalı (PartStatus={part.PartStatus}).");
                if (line.OriginalQty <= 0)
                    return Invalid("INVALID_QTY", $"Satır {line.LineNo}: miktar sıfırdan büyük olmalı.");
            }

            var now = NextStamp();
            var requisition = input.Clone();
            requisition.RequisitionNo = $"PR{now:yy}-{++_requisitionSeq:00000}";
            requisition.Contract = Site;
            requisition.Objstate = "Released";
            requisition.CreatedAt = now;
            requisition.ChangedAt = now;
            for (var i = 0; i < requisition.Lines.Count; i++)
            {
                var line = requisition.Lines[i];
                if (line.LineNo == 0) line.LineNo = i + 1;
                if (string.IsNullOrEmpty(line.UnitMeas)) line.UnitMeas = _parts[line.PartNo].UnitMeas;
            }

            _requisitions[requisition.RequisitionNo] = requisition;
            return new CreateResult(CreateOutcome.Created, requisition.Clone());
        }

        static CreateResult Invalid(string code, string message) => new(CreateOutcome.Invalid, null, code, message);
    }

    /// <summary>Moves a requisition one step: Released → Authorized → Closed. Closing books the goods into stock.</summary>
    public PurchaseRequisition? Advance(string requisitionNo)
    {
        lock (_gate)
        {
            if (!_requisitions.TryGetValue(requisitionNo, out var r)) return null;
            var now = NextStamp();
            switch (r.Objstate)
            {
                case "Released":
                    r.Objstate = "Authorized";
                    break;
                case "Authorized":
                    r.Objstate = "Closed";
                    foreach (var line in r.Lines) Receive(line.PartNo, line.OriginalQty, now);
                    break;
                default:
                    return r.Clone();
            }
            r.ChangedAt = now;
            return r.Clone();
        }
    }

    public PurchaseRequisition? Cancel(string requisitionNo)
    {
        lock (_gate)
        {
            if (!_requisitions.TryGetValue(requisitionNo, out var r) || r.Objstate is "Closed" or "Cancelled") return null;
            r.Objstate = "Cancelled";
            r.ChangedAt = NextStamp();
            return r.Clone();
        }
    }

    /// <summary>Simulation helper: requisitions in <paramref name="state"/> whose last change is older than <paramref name="age"/>.</summary>
    public List<string> DueForAdvance(string state, TimeSpan age)
    {
        lock (_gate)
        {
            var limit = _clock.GetUtcNow() - age;
            return _requisitions.Values.Where(r => r.Objstate == state && r.ChangedAt <= limit).Select(r => r.RequisitionNo).ToList();
        }
    }

    public bool SetPartBlocked(string partNo, bool blocked)
    {
        lock (_gate)
        {
            if (!_parts.TryGetValue(partNo, out var part)) return false;
            part.PartStatus = blocked ? "O" : "A";
            part.ChangedAt = NextStamp();
            return true;
        }
    }

    /// <summary>Production consumption: take a small quantity out of a random stock row.</summary>
    public InventoryPartInStock? ConsumeRandom(Random random)
    {
        lock (_gate)
        {
            var candidates = _stock.Values.Where(s => s.QtyOnhand - s.QtyReserved > 5).ToList();
            if (candidates.Count == 0) return null;
            var row = candidates[random.Next(candidates.Count)];
            var qty = Math.Max(1, Math.Round((row.QtyOnhand - row.QtyReserved) * (decimal)(0.03 + random.NextDouble() * 0.07), 0));
            row.QtyOnhand -= qty;
            row.ChangedAt = NextStamp();
            return row.Clone();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _parts.Clear();
            _stock.Clear();
            _requisitions.Clear();
            var now = NextStamp();
            // The mock keeps its data in memory. Starting the numbering from the clock (minutes) means a restarted
            // mock never hands out a requisition number that the internal system has already stored.
            _requisitionSeq = (int)(now.ToUnixTimeSeconds() / 60 % 89_000) + 10_000;

            foreach (var (partNo, description, unit, category, safety, status) in SeedParts)
                _parts[partNo] = new InventoryPart
                {
                    PartNo = partNo, Description = description, UnitMeas = unit, PartCategory = category,
                    SafetyStock = safety, PartStatus = status, ChangedAt = now,
                };

            foreach (var (partNo, location, onHand, reserved) in SeedStock)
            {
                var key = StockKey(partNo, location);
                _stock[key] = new InventoryPartInStock
                {
                    Objkey = key, Contract = Site, PartNo = partNo, LocationNo = location,
                    QtyOnhand = onHand, QtyReserved = reserved, UnitMeas = _parts[partNo].UnitMeas, ChangedAt = now,
                };
            }
        }
    }

    private void Receive(string partNo, decimal qty, DateTimeOffset now)
    {
        var row = _stock.Values.Where(s => s.PartNo == partNo).OrderByDescending(s => s.QtyOnhand).FirstOrDefault();
        if (row is null)
        {
            var key = StockKey(partNo, "A-01");
            row = new InventoryPartInStock { Objkey = key, Contract = Site, PartNo = partNo, LocationNo = "A-01", UnitMeas = _parts[partNo].UnitMeas };
            _stock[key] = row;
        }
        row.QtyOnhand += qty;
        row.ChangedAt = now;
    }

    /// <summary>Strictly increasing timestamps, so delta sync by ChangedAt never misses a change.</summary>
    private DateTimeOffset NextStamp()
    {
        var now = _clock.GetUtcNow();
        now = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        _lastStamp = now > _lastStamp ? now : _lastStamp.AddMilliseconds(1);
        return _lastStamp;
    }

    private static string StockKey(string partNo, string location) => $"{Site}^{partNo}^{location}";

    // Fictional catalogue. Part numbers are made up for the demo.
    private static readonly (string PartNo, string Description, string Unit, string Category, decimal SafetyStock, string Status)[] SeedParts =
    [
        ("ST-AH36-08", "Gemi sacı AH36 8 mm (2500x10000)", "AD", "Sac", 40, "A"),
        ("ST-AH36-12", "Gemi sacı AH36 12 mm (2500x10000)", "AD", "Sac", 30, "A"),
        ("PR-HP160-9", "HP profil 160x9", "M", "Profil", 300, "A"),
        ("PR-L100-10", "L köşebent 100x100x10", "M", "Profil", 200, "A"),
        ("WL-E71T1-12", "Özlü kaynak teli E71T-1 Ø1,2 mm", "KG", "Kaynak sarf", 500, "A"),
        ("WL-E6013-32", "Rutil elektrot E6013 Ø3,2 mm (kullanımdan kalktı)", "KG", "Kaynak sarf", 0, "O"),
        ("GS-ARCO2-50", "Kaynak gazı Ar/CO2 karışım tüpü 50 L", "AD", "Kaynak sarf", 20, "A"),
        ("PP-ST37-DN50", "Dikişsiz çelik boru ST37 DN50", "M", "Boru", 150, "A"),
        ("PP-CUNI-DN80", "CuNi 90/10 boru DN80", "M", "Boru", 60, "A"),
        ("FL-DN50-PN16", "Kaynak boyunlu flanş DN50 PN16", "AD", "Boru donanımı", 40, "A"),
        ("VL-BFY-DN100", "Kelebek vana DN100 PN16", "AD", "Vana", 8, "A"),
        ("PT-EPX-PRM20", "Epoksi astar boya 20 L", "AD", "Boya", 25, "A"),
        ("PT-AF-SPC20", "Antifouling boya 20 L", "AD", "Boya", 15, "A"),
        ("EL-CBL-3X25", "Halojensiz gemi kablosu 3x2,5 mm²", "M", "Elektrik", 1000, "A"),
        ("EL-JB-IP66", "Paslanmaz buat IP66", "AD", "Elektrik", 30, "A"),
        ("BL-M16X60-88", "Cıvata M16x60 8.8 galvaniz", "AD", "Bağlantı elemanı", 400, "A"),
    ];

    private static readonly (string PartNo, string Location, decimal OnHand, decimal Reserved)[] SeedStock =
    [
        ("ST-AH36-08", "SAC-SAHA", 64, 12),
        ("ST-AH36-12", "SAC-SAHA", 22, 6),
        ("PR-HP160-9", "PROFIL-01", 820, 140),
        ("PR-L100-10", "PROFIL-01", 180, 40),
        ("WL-E71T1-12", "A-01", 1450, 200),
        ("WL-E6013-32", "A-01", 35, 0),
        ("GS-ARCO2-50", "GAZ-01", 26, 4),
        ("PP-ST37-DN50", "BORU-01", 240, 30),
        ("PP-CUNI-DN80", "BORU-01", 44, 10),
        ("FL-DN50-PN16", "A-02", 96, 12),
        ("VL-BFY-DN100", "A-02", 5, 2),
        ("PT-EPX-PRM20", "BOYA-01", 60, 20),
        ("PT-AF-SPC20", "BOYA-01", 12, 0),
        ("EL-CBL-3X25", "A-03", 3200, 600),
        ("EL-JB-IP66", "A-03", 75, 10),
        ("BL-M16X60-88", "A-02", 2600, 300),
    ];
}
