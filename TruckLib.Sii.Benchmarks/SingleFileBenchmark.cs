using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TruckLib;
using TruckLib.Sii;

internal static class SingleFileBenchmark
{
    public static int Run(Dictionary<string, string> options)
    {
        if (options.Keys.Any(k => k is not ("--file" or "--root" or "--iterations" or "--output")))
            throw new ArgumentException("Single-file measurement accepts only --file, --root, --iterations and --output.");
        string file = Path.GetFullPath(options["--file"]);
        string root = Path.GetFullPath(options.GetValueOrDefault("--root", Path.GetDirectoryName(file)!));
        string relative = Path.GetRelativePath(root, file);
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Contains(".."))
            throw new ArgumentException("The file must be inside --root.");
        int iterations = int.Parse(options.GetValueOrDefault("--iterations", "10"), CultureInfo.InvariantCulture);
        if (iterations < 1 || iterations > 10000) throw new ArgumentOutOfRangeException("--iterations");

        string entry = "/" + relative.Replace('\\', '/');
        var capture = new CaptureFiles(root);
        // Discover and read only this entry and the includes actually requested.
        var first = SiiFile.Open(entry, capture);
        var memory = new MemoryFs(capture.Files);
        var warming = Stopwatch.StartNew();
        int warmupParses = 0;
        do
        {
            GC.KeepAlive(SiiFile.Open(entry, memory));
            warmupParses++;
        } while (warming.ElapsedMilliseconds < 500 && warmupParses < 1000);
        using var process = Process.GetCurrentProcess();
        var watch = new Stopwatch();
        var runs = new List<Sample>();
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var cpu = process.TotalProcessorTime;
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            var parsed = SiiFile.Open(entry, memory);
            watch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            runs.Add(new(watch.Elapsed.TotalMilliseconds,
                (process.TotalProcessorTime - cpu).TotalMilliseconds, allocated));
            GC.KeepAlive(parsed);
        }
        var result = new
        {
            File = file, FileBytes = capture.Files[entry].Length, PreloadedFiles = capture.Files.Count,
            Units = first.Units.Count, WarmupParses = warmupParses, Iterations = iterations,
            MedianMilliseconds = Median(runs.Select(r => r.Milliseconds)),
            MedianCpuMilliseconds = Median(runs.Select(r => r.CpuMilliseconds)),
            MedianAllocatedBytes = Median(runs.Select(r => (double)r.AllocatedBytes)), Runs = runs
        };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        if (options.TryGetValue("--output", out var output))
        {
            string target = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, json);
        }
        return 0;
    }

    private sealed record Sample(double Milliseconds, double CpuMilliseconds, long AllocatedBytes);
    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
    }

    private sealed class CaptureFiles(string root) : IFileSystem
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public char DirectorySeparator => '/';
        private static string Key(string path) => MemoryFs.Key(path);
        private string DiskPath(string path) => Path.Combine(root, Key(path).TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        public bool FileExists(string path)
        {
            try { return File.Exists(DiskPath(path)); }
            catch (ArgumentException) { return false; }
        }
        public bool DirectoryExists(string path) => Directory.Exists(DiskPath(path));
        public IList<string> GetFiles(string path) => Directory.GetFiles(DiskPath(path))
            .Select(p => "/" + Path.GetRelativePath(root, p).Replace('\\', '/')).ToArray();
        public string GetParent(string path) { string key = Key(path); return key[..key.LastIndexOf('/')]; }
        public byte[] ReadAllBytes(string path)
        {
            string key = Key(path);
            if (!Files.TryGetValue(key, out var bytes)) Files[key] = bytes = File.ReadAllBytes(DiskPath(key));
            return bytes;
        }
        public string ReadAllText(string path) => ReadAllText(path, Encoding.UTF8);
        public string ReadAllText(string path, Encoding encoding) => encoding.GetString(ReadAllBytes(path));
        public Stream Open(string path) => new MemoryStream(ReadAllBytes(path), false);
    }
}
