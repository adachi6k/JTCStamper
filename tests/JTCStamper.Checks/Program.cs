using System.Security.Cryptography;
using System.Text.Json;
using JTCStamper.Core;

var root = Path.Combine(Path.GetTempPath(), "jtc-checks-" + Guid.NewGuid());
Directory.CreateDirectory(root);
var key = RandomNumberGenerator.GetBytes(32);
int passed = 0;
void Check(string name, Action<string> action)
{
    var dir = Path.Combine(root, name); Directory.CreateDirectory(dir);
    action(dir); Console.WriteLine("PASS " + name); passed++;
}
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected failure"); }
var stamp = new Stamp("山田", new DateOnly(1900, 1, 1), "確認");
try
{
    Check("generated-copy-annotation-and-reopen", dir =>
    {
        Guid first;
        using (var journal = new Journal(dir, key))
        {
            var clipboard = new FakeClipboard();
            first = new CopyService(journal, clipboard).GenerateAndCopy(stamp, [1, 2, 3]);
            var second = new CopyService(journal, clipboard).GenerateAndCopy(stamp with { DisplayDate = new DateOnly(2100, 1, 1) }, [1, 2, 3]);
            Assert(first != second && clipboard.Calls == 2);
            var before = journal.Read()[0].Signed;
            journal.Annotate(first, "用途: 稟議資料（貼付は未確認）");
            Assert(journal.Read()[0].Signed == before);
            Assert(journal.Verify(before).EventId == first);
            var generation = JsonSerializer.Deserialize<Generation>(journal.Read()[0].Entry.Payload)!;
            Assert(generation.Stamp.DisplayDate.Year == 1900 && generation.CreatedUtc.Year >= 2026);
            Throws(() => journal.Annotate(Guid.NewGuid(), "orphan"));
            Throws(() => { using var competing = new Journal(dir, key); });
        }
        using var reopened = new Journal(dir, key);
        Assert(reopened.Read().Count == 7);
    });
    Check("tampering-blocks-copy", dir =>
    {
        using var journal = new Journal(dir, key);
        journal.Append("Generated", Guid.NewGuid(), new { Name = "before" });
        var file = Directory.GetFiles(dir, "*.json").Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("before", "tamper"));
        var clipboard = new FakeClipboard();
        Throws(() => new CopyService(journal, clipboard).GenerateAndCopy(stamp, [1]));
        Assert(clipboard.Calls == 0);
    });
    Check("wrong-key-rejected", dir =>
    {
        using (var journal = new Journal(dir, key)) journal.Append("Generated", Guid.NewGuid(), new { });
        Throws(() => { using var journal = new Journal(dir, RandomNumberGenerator.GetBytes(32)); });
    });
    Check("generation-save-failure-blocks-copy", dir =>
    {
        using var journal = new Journal(dir, key);
        Directory.CreateDirectory(Path.Combine(dir, "000000000001.json"));
        var clipboard = new FakeClipboard();
        Throws(() => new CopyService(journal, clipboard).GenerateAndCopy(stamp, [1]));
        Assert(clipboard.Calls == 0 && journal.Read().Count == 0);
    });
    Check("copy-intent-save-failure-blocks-copy", dir =>
    {
        using var journal = new Journal(dir, key);
        Directory.CreateDirectory(Path.Combine(dir, "000000000002.json"));
        var clipboard = new FakeClipboard();
        Throws(() => new CopyService(journal, clipboard).GenerateAndCopy(stamp, [1]));
        Assert(clipboard.Calls == 0 && journal.Read().Count == 1);
    });
    Check("clipboard-failure-recorded", dir =>
    {
        using var journal = new Journal(dir, key);
        var clipboard = new FakeClipboard { Action = () => throw new IOException("Clipboard busy") };
        Throws(() => new CopyService(journal, clipboard).GenerateAndCopy(stamp, [1]));
        Assert(journal.Read().Last().Entry.Kind == "CopyFailed");
        Assert(!journal.Read().Any(x => x.Entry.Kind == "CopyCompleted"));
    });
    Check("completion-save-failure-is-not-success", dir =>
    {
        using var journal = new Journal(dir, key);
        var clipboard = new FakeClipboard { Action = () => Directory.CreateDirectory(Path.Combine(dir, "000000000003.json")) };
        Throws(() => new CopyService(journal, clipboard).GenerateAndCopy(stamp, [1]));
        Assert(clipboard.Calls == 1 && journal.Read().Last().Entry.Kind == "CopyRequested");
    });
    Check("middle-deletion-detected", dir =>
    {
        using var journal = new Journal(dir, key);
        new CopyService(journal, new FakeClipboard()).GenerateAndCopy(stamp, [1]);
        File.Delete(Path.Combine(dir, "000000000002.json")); Throws(() => journal.Read());
    });
    Check("interrupted-temp-ignored", dir =>
    {
        using var journal = new Journal(dir, key);
        File.WriteAllText(Path.Combine(dir, "crash.tmp"), "partial data");
        Assert(journal.Read().Count == 0);
        new CopyService(journal, new FakeClipboard()).GenerateAndCopy(stamp, [1]);
        Assert(journal.Read().Count == 3);
    });
    Check("stamp-settings-roundtrip-and-validation", dir =>
    {
        var path = Path.Combine(dir, "sample.jtcstamp");
        var settings = new StampSettings(1, "山田", new DateOnly(2100, 9, 18), "承認");
        settings.Save(path); Assert(StampSettings.Load(path) == settings);
        Throws(() => (settings with { Name = "" }).Save(path));
        Assert(StampSettings.Load(path) == settings);
        (settings with { Bottom = "確認" }).Save(path);
        Assert(StampSettings.Load(path).Bottom == "確認");
        File.WriteAllText(path, "{}"); Throws(() => StampSettings.Load(path));
        File.WriteAllText(path, "{ invalid JSON"); Throws(() => StampSettings.Load(path));
        Throws(() => (settings with { Version = 2 }).Save(path));
    });
    Check("date-default-rollover-and-explicit-override", dir =>
    {
        var today = new DateOnly(2026, 9, 18);
        var selection = new StampDateSelection(() => today);
        Assert(!selection.IsSpecified && selection.Resolve() == today);
        today = today.AddDays(1);
        Assert(selection.Resolve() == today);
        selection.Specify(new DateOnly(1900, 1, 1));
        today = today.AddDays(1);
        Assert(selection.IsSpecified && selection.Resolve() == new DateOnly(1900, 1, 1));
        selection.Specify(new DateOnly(2100, 12, 31));
        Assert(selection.Resolve() == new DateOnly(2100, 12, 31));
        selection.UseToday();
        Assert(!selection.IsSpecified && selection.Resolve() == today);
    });
    Check("verification-exact-duplicates-original-and-readonly", dir =>
    {
        using var journal = new Journal(dir, key);
        var service = new CopyService(journal, new FakeClipboard());
        var first = service.GenerateAndCopy(stamp, [1, 2, 3]);
        var second = service.GenerateAndCopy(stamp, [1, 2, 3]);
        var before = journal.Read().ToArray();
        var verify = new VerificationService(journal);
        var result = verify.Image([1, 2, 3]);
        Assert(result.Status == VerificationStatus.Match && result.Matches.Count == 2);
        Assert(result.Matches.Select(x => x.EventId).ToHashSet().SetEquals([first, second]));
        Assert(verify.Image([1, 2, 4]).Status == VerificationStatus.NoRecord);
        var original = JsonSerializer.Serialize(before[0].Signed);
        Assert(verify.Original(original).Matches.Single().EventId == first);
        Assert(verify.Original("{}").Status == VerificationStatus.Indeterminate);
        Assert(verify.Original("not JSON").Status == VerificationStatus.Indeterminate);
        Assert(verify.Original(JsonSerializer.Serialize(before[1].Signed)).Status == VerificationStatus.Indeterminate);
        Assert(before.SequenceEqual(journal.Read()));
        using var other = new Journal(Path.Combine(dir, "other"), key);
        Assert(new VerificationService(other).Original(original).Status == VerificationStatus.NoRecord);
        using var wrongKey = new Journal(Path.Combine(dir, "wrong"), RandomNumberGenerator.GetBytes(32));
        Assert(new VerificationService(wrongKey).Original(original).Status == VerificationStatus.Indeterminate);
        var damaged = before[0].Signed with { Mac = new string('0', 64) };
        Assert(verify.Original(JsonSerializer.Serialize(damaged)).Status == VerificationStatus.Indeterminate);
        File.WriteAllText(Path.Combine(dir, "000000000001.json"), JsonSerializer.Serialize(damaged));
        Assert(verify.Image([1, 2, 3]).Status == VerificationStatus.Indeterminate);
        Assert(verify.Original(original).Status == VerificationStatus.Indeterminate);
    });
    Console.WriteLine($"{passed} checks passed.");
}
finally { Directory.Delete(root, true); CryptographicOperations.ZeroMemory(key); }
sealed class FakeClipboard : IClipboard
{
    public int Calls { get; private set; }
    public Action? Action { get; init; }
    public void Copy(byte[] png) { Calls++; Action?.Invoke(); }
}
