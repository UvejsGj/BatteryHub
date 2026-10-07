using System.Globalization;

namespace BatteryHub.Tests.Fixtures;

/// <summary>
/// A raw report stored as a text file: <c># key: value</c> header lines, then hex bytes
/// (whitespace and line breaks ignored). See Fixtures/README.md.
/// </summary>
public sealed record Fixture(string Name, IReadOnlyDictionary<string, string> Meta, byte[] Bytes)
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public string this[string key] =>
        Meta.TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"Fixture {Name} has no '{key}' header.");

    public static Fixture Load(string relativePath) => Parse(relativePath, File.ReadAllText(Path.Combine(Root, relativePath)));

    /// <summary>Names of every fixture under <paramref name="folder"/>, for <c>[MemberData]</c>.</summary>
    public static TheoryData<string> All(string folder)
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Root, folder), "*.hex").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(Root, path));
        }

        return data;
    }

    public static Fixture Parse(string name, string text)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bytes = new List<byte>();
        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith('#'))
            {
                int colon = line.IndexOf(':');
                if (colon > 0)
                {
                    meta[line[1..colon].Trim()] = line[(colon + 1)..].Trim();
                }

                continue;
            }

            foreach (var token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                bytes.Add(byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
            }
        }

        return new Fixture(name, meta, bytes.ToArray());
    }
}
