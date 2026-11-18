namespace Tersane.EskiAmbar;

/// <summary>The legacy system keeps its own (fictional) master data; it has no API to anything else.</summary>
public static class LegacyCatalog
{
    public static readonly IReadOnlyDictionary<string, (string Name, string[] Racks)> Depots = new Dictionary<string, (string, string[])>
    {
        ["ANA"] = ("Ana Ambar", ["A-01", "A-02", "A-03", "A-04"]),
        ["SAC"] = ("Sac Stok Sahası", ["SAC-SAHA", "SAC-KAPALI"]),
        ["BORU"] = ("Boru Ambarı", ["BORU-01", "BORU-02"]),
        ["BOYA"] = ("Boya Ambarı", ["BOYA-01"]),
        ["GAZ"] = ("Gaz Deposu", ["GAZ-01"]),
    };

    public static readonly IReadOnlyDictionary<string, (string Name, string Unit)> Materials = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
    {
        ["ST-AH36-08"] = ("GEMI SACI AH36 8MM", "AD"),
        ["ST-AH36-12"] = ("GEMI SACI AH36 12MM", "AD"),
        ["PR-HP160-9"] = ("HP PROFIL 160X9", "M"),
        ["PR-L100-10"] = ("L KOSEBENT 100X100X10", "M"),
        ["WL-E71T1-12"] = ("OZLU KAYNAK TELI E71T-1 1,2", "KG"),
        ["GS-ARCO2-50"] = ("AR/CO2 TUP 50LT", "AD"),
        ["PP-ST37-DN50"] = ("CELIK BORU ST37 DN50", "M"),
        ["PP-CUNI-DN80"] = ("CUNI BORU DN80", "M"),
        ["FL-DN50-PN16"] = ("FLANS DN50 PN16", "AD"),
        ["VL-BFY-DN100"] = ("KELEBEK VANA DN100", "AD"),
        ["PT-EPX-PRM20"] = ("EPOKSI ASTAR 20LT", "AD"),
        ["PT-AF-SPC20"] = ("ANTIFOULING 20LT", "AD"),
        ["EL-CBL-3X25"] = ("GEMI KABLOSU 3X2,5", "M"),
        ["EL-JB-IP66"] = ("BUAT IP66", "AD"),
        ["BL-M16X60-88"] = ("CIVATA M16X60 8.8", "AD"),
    };
}
