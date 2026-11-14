using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TruckLib;
using TruckLib.Sii;

try
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("Usage: --file FILE [--root DIRECTORY] [--iterations 10] [--output RESULTS_JSON]");
        Console.WriteLine("Usage: --baseline OLD_DLL --output RESULTS_JSON [--corpus DIRECTORY | --synthetic UNIT_COUNT] [--entries PATH_LIST] [--iterations 3]");
        Console.WriteLine("Compares exact values/types against an isolated baseline, then benchmarks preloaded memory. No game files are included.");
        return 0;
    }
    var options = new Dictionary<string, string>();
    string[] allowed = ["--file", "--root", "--corpus", "--baseline", "--output", "--entries", "--iterations", "--synthetic"];
    for (int i = 0; i < args.Length; i += 2)
        if (i + 1 >= args.Length || !allowed.Contains(args[i]) || !options.TryAdd(args[i], args[i + 1]))
            throw new ArgumentException("Unknown, repeated or incomplete benchmark option.");
    if (options.ContainsKey("--file")) return SingleFileBenchmark.Run(options);
    if (options.ContainsKey("--corpus") == options.ContainsKey("--synthetic")) throw new ArgumentException("Choose --corpus or --synthetic.");
    string baselinePath = Path.GetFullPath(options["--baseline"]);
    int iterations = int.Parse(options.GetValueOrDefault("--iterations", "3"), CultureInfo.InvariantCulture);
    if (iterations < 0 || iterations > 100) throw new ArgumentOutOfRangeException("--iterations");
    string output = Path.GetFullPath(options["--output"]);
    Dictionary<string, byte[]> buffers;
    if (options.TryGetValue("--synthetic", out string? size))
    {
        int count = int.Parse(size, CultureInfo.InvariantCulture);
        if (count < 1 || count > 1_000_000 || options.ContainsKey("--entries")) throw new ArgumentOutOfRangeException("--synthetic");
        var text = new StringBuilder("SiiNunit {\n");
        for (int i = 0; i < count; i++)
            text.Append(CultureInfo.InvariantCulture, $"model_def : model.item_{i} {{\n model_desc: \"/model/example/item_{i}.pmd\"\n enabled: true\n scale: (1.0, 2.0, 3.0)\n category[]: \"synthetic\"\n distance: {i}\n }}\n");
        text.Append('}');
        buffers = new(StringComparer.Ordinal) { ["/synthetic.sii"] = Encoding.UTF8.GetBytes(text.ToString()) };
    }
    else
    {
        string directory = Path.GetFullPath(options["--corpus"]);
        buffers = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(p => "/" + Path.GetRelativePath(directory, p).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
    }
    var fs = new MemoryFs(buffers);
    string[] entries = options.TryGetValue("--entries", out var manifest)
        ? File.ReadAllLines(manifest).Where(p => p.Length > 0).Distinct().ToArray()
        : buffers.Keys.Where(p => p.EndsWith(".sii", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
    var baselineContext = new BaselineContext();
    var baseline = baselineContext.LoadFromAssemblyPath(baselinePath);
    var oldOpen = baseline.GetType("TruckLib.Sii.SiiFile")!.GetMethod("Open", [typeof(string), typeof(IFileSystem)])!;
    var current = typeof(SiiFile).Assembly;
    var coverage = new List<object>();
    var mismatches = new List<string>();
    var oneUnit = new SiiFile { Units = [new Unit("a", "b") { Attributes = new() { ["c"] = "d" } }] };
    var twoUnits = new SiiFile { Units = [new Unit("a", "b"), new Unit("c", "d")] };
    if (Fingerprint(oneUnit) == Fingerprint(twoUnits)) throw new InvalidDataException("Fingerprint unit-boundary self-test failed.");
    Console.WriteLine($"Preloaded {buffers.Count} resources; validating {entries.Length} entries.");
    foreach (string path in entries)
    {
        object? oldFile = null, newFile = null;
        Exception? oldError = null, newError = null;
        try { oldFile = oldOpen.Invoke(null, [path, fs]); } catch (Exception e) { oldError = Unwrap(e); }
        try { newFile = SiiFile.Open(path, fs); } catch (Exception e) { newError = Unwrap(e); }
        if (oldError?.GetType().FullName != newError?.GetType().FullName
            || oldError is null && Fingerprint(oldFile!) != Fingerprint(newFile!)) mismatches.Add(path);
        coverage.Add(new { Path = path, Candidate = "Current", Error = oldError?.GetType().FullName,
            CandidateError = newError?.GetType().FullName,
            Units = (newFile as SiiFile)?.Units.Count });
    }
    if (mismatches.Count != 0) throw new InvalidDataException("Differential mismatches: " + string.Join(", ", mismatches));
    Console.WriteLine("Differential validation passed.");
    var timings = new List<object>();
    for (int i = 0; i < iterations; i++)
        foreach (bool legacy in i % 2 == 0 ? new[] { true, false } : new[] { false, true })
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var cpu = Process.GetCurrentProcess().TotalProcessorTime;
            var watch = Stopwatch.StartNew();
            foreach (string path in entries)
            {
                object file = legacy ? oldOpen.Invoke(null, [path, fs])! : SiiFile.Open(path, fs);
                GC.KeepAlive(file);
            }
            watch.Stop();
            var result = new { Iteration = i + 1, Legacy = legacy, Milliseconds = watch.Elapsed.TotalMilliseconds,
                CpuMilliseconds = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds,
                AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated };
            timings.Add(result); Console.WriteLine(JsonSerializer.Serialize(result));
        }
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, JsonSerializer.Serialize(new { Candidate = "Current", Baseline = baselinePath,
        BaselineHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baselinePath))),
        CurrentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(current.Location))),
        ResourceCount = buffers.Count, Entries = entries.Length, Coverage = coverage, Timings = timings },
        new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e); return 1; }

static Exception Unwrap(Exception e) => e is TargetInvocationException { InnerException: { } inner } ? inner : e;

static string Fingerprint(object file)
{
    var builder = new StringBuilder();
    var units = ((IEnumerable)file.GetType().GetProperty("Units")!.GetValue(file)!).Cast<object>().ToArray();
    builder.Append("FILE[").Append(units.Length).Append("]{");
    foreach (var unit in units)
    {
        builder.Append("UNIT{");
        Append(unit!.GetType().GetProperty("Class")!.GetValue(unit));
        Append(unit.GetType().GetProperty("Name")!.GetValue(unit));
        var attributes = (IDictionary)unit.GetType().GetProperty("Attributes")!.GetValue(unit)!;
        builder.Append("ATTRIBUTES[").Append(attributes.Count).Append("]{");
        foreach (DictionaryEntry attribute in attributes)
        { builder.Append("KEY{"); Append(attribute.Key); builder.Append("}VALUE{"); Append(attribute.Value); builder.Append('}'); }
        builder.Append("}ENDUNIT}");
    }
    var includes = ((IEnumerable<string>?)file.GetType().GetProperty("Includes")!.GetValue(file) ?? []).Order(StringComparer.Ordinal).ToArray();
    builder.Append("}INCLUDES[").Append(includes.Length).Append("]{");
    foreach (string included in includes)
        Append(included);
    builder.Append('}');
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    void Append(object? value)
    {
        if (value is null) { builder.Append("null;"); return; }
        string type = value.GetType().FullName!;
        builder.Append("TYPE[").Append(type.Length).Append(':').Append(type).Append("]{");
        switch (value)
        {
            case float number: builder.Append(BitConverter.SingleToInt32Bits(number)); break;
            case string text: builder.Append(text.Length).Append(':').Append(text); break;
            case ITuple tuple:
                builder.Append("TUPLE[").Append(tuple.Length).Append("]{");
                for (int i = 0; i < tuple.Length; i++) Append(tuple[i]);
                builder.Append('}'); break;
            case IEnumerable sequence:
                var elements = sequence.Cast<object?>().ToArray();
                builder.Append("SEQUENCE[").Append(elements.Length).Append("]{");
                foreach (var item in elements) Append(item);
                builder.Append('}'); break;
            case Vector2 vector: Append(vector.X); Append(vector.Y); break;
            case Vector3 vector: Append(vector.X); Append(vector.Y); Append(vector.Z); break;
            case Vector4 vector: Append(vector.X); Append(vector.Y); Append(vector.Z); Append(vector.W); break;
            case Quaternion quaternion: Append(quaternion.X); Append(quaternion.Y); Append(quaternion.Z); Append(quaternion.W); break;
            case Placement placement: Append(placement.Position); Append(placement.Rotation); break;
            default:
                if (value.GetType().FullName == "TruckLib.Sii.Placement")
                { Append(value.GetType().GetProperty("Position")!.GetValue(value)); Append(value.GetType().GetProperty("Rotation")!.GetValue(value)); }
                else if (value.GetType().FullName is "TruckLib.Sii.LinkPointer" or "TruckLib.Sii.OwnerPointer")
                    Append(value.GetType().GetProperty("Value")!.GetValue(value));
                else builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
        builder.Append("};");
    }
}

sealed class BaselineContext() : AssemblyLoadContext(isCollectible: false)
{
    protected override Assembly? Load(AssemblyName name)
        => AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
}

sealed class MemoryFs(Dictionary<string, byte[]> files) : IFileSystem
{
    public char DirectorySeparator => '/';
    internal static string Key(string p)
    {
        var parts = new List<string>();
        foreach (string part in p.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw new ArgumentException("Path is outside the resource root.");
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(part);
        }
        return "/" + string.Join('/', parts);
    }
    public bool FileExists(string p)
    {
        try { return files.ContainsKey(Key(p)); }
        catch (ArgumentException) { return false; }
    }
    public bool DirectoryExists(string p) => files.Keys.Any(k => k.StartsWith(Key(p).TrimEnd('/') + "/", StringComparison.Ordinal));
    public IList<string> GetFiles(string p) => files.Keys.Where(k => k.StartsWith(Key(p).TrimEnd('/') + "/", StringComparison.Ordinal)).ToArray();
    public string GetParent(string p) { string key = Key(p); return key[..key.LastIndexOf('/')]; }
    public byte[] ReadAllBytes(string p) => files[Key(p)];
    public string ReadAllText(string p) => Encoding.UTF8.GetString(ReadAllBytes(p));
    public string ReadAllText(string p, Encoding encoding) => encoding.GetString(ReadAllBytes(p));
    public Stream Open(string p) => new MemoryStream(ReadAllBytes(p), false);
}
