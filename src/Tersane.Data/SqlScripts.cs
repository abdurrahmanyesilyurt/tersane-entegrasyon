using System.Reflection;

namespace Tersane.Data;

/// <summary>Reads the embedded T-SQL programmability scripts (Sql/*.sql) in file-name order.</summary>
public static class SqlScripts
{
    private const string Prefix = "Tersane.Data.Sql.";

    public static IReadOnlyList<(string Name, string Sql)> All()
    {
        var assembly = typeof(SqlScripts).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n => (n[Prefix.Length..], Read(assembly, n)))
            .ToList();
    }

    private static string Read(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded script {resourceName} not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
