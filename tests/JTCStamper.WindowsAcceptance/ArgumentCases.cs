using System.IO;
using System.Security.Cryptography;

internal static partial class Program
{
    static Dictionary<string, string> Snapshot(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(folder, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));

    static void ArgumentCases(string standard, string lite, string temp)
    {
        foreach (var (name, exe, runtime) in new[] { ("Standard", standard, false), ("Lite", lite, true) })
        {
            var folder = Path.Combine(temp, "arguments-" + name + "-" + Guid.NewGuid().ToString("N"));
            var valid = NewData(folder);
            var unmarked = Path.Combine(folder, "unmarked"); Directory.CreateDirectory(unmarked);
            File.WriteAllText(Path.Combine(unmarked, "preserve.txt"), "Synthetic existing file");
            var wrong = Path.Combine(folder, "wrong-marker"); Directory.CreateDirectory(wrong);
            File.WriteAllText(Path.Combine(wrong, ".jtc-smoke-root"), "wrong marker");
            var used = NewData(Path.Combine(folder, "used")); File.WriteAllText(Path.Combine(used, "key.dpapi"), "Synthetic sentinel, not a key");
            var outside = NewData(Path.Combine(root, "outside-temp-" + Guid.NewGuid().ToString("N")));
            var inputs = new (string Label, string[] Args)[]
            {
                ("missing-arguments", ["--smoke-test"]),
                ("unknown-phase", ["--smoke-test", "--test-root", valid, "--phase", "unknown"]),
                ("unknown-option", ["--smoke-test", "--test-root", valid, "--phase", "seed", "--unknown"]),
                ("duplicate-option", ["--smoke-test", "--test-root", valid, "--phase", "seed", "--clipboard", "--clipboard"]),
                ("wrong-order", ["--smoke-test", "--phase", "seed", "--test-root", valid]),
                ("unmarked-root", ["--smoke-test", "--test-root", unmarked, "--phase", "seed"]),
                ("wrong-marker", ["--smoke-test", "--test-root", wrong, "--phase", "seed"]),
                ("used-seed-root", ["--smoke-test", "--test-root", used, "--phase", "seed"]),
                ("outside-temp", ["--smoke-test", "--test-root", outside, "--phase", "seed"])
            };
            foreach (var input in inputs)
                Case(name + "-arguments-" + input.Label, () =>
                {
                    var before = Snapshot(folder); var outsideBefore = Snapshot(outside);
                    var applicationBefore = Snapshot(Path.GetDirectoryName(exe)!);
                    var result = Start(exe, input.Args, input.Label, runtime);
                    Require(result.ExitCode == 2, "Malformed test invocation did not return 2.");
                    bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(x => b.TryGetValue(x.Key, out var v) && x.Value == v);
                    Require(Same(before, Snapshot(folder)) && Same(outsideBefore, Snapshot(outside)) &&
                        Same(applicationBefore, Snapshot(Path.GetDirectoryName(exe)!)), "Rejected invocation modified data or application files.");
                    return new { result.ExitCode, ExistingFilesPreserved = true, JournalNotCreated = true, ClipboardRequested = false };
                });
        }
    }
}
