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
    Check("history-snapshot-is-fresh-and-detects-later-tampering", dir =>
    {
        using var journal = new Journal(dir, key);
        var id = new CopyService(journal, new FakeClipboard()).GenerateCodedAndCopy(stamp, _ => [1, 2, 3]);
        journal.Annotate(id, "first");
        var service = new VerificationService(journal);
        var snapshot = service.ReadHistory();
        Assert(snapshot.Entries.Count == 4 && snapshot.Generations.Single().EventId == id);
        journal.Annotate(id, "second");
        Assert(service.ReadHistory().Entries.Count == 5 && snapshot.Entries.Count == 4);
        var path = Path.Combine(dir, "000000000004.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("first", "altered"));
        Throws(() => service.ReadHistory());
    });
    Check("event-read-verifies-unrelated-records-and-observes-cancellation", dir =>
    {
        using var journal = new Journal(dir, key);
        var copy = new CopyService(journal, new FakeClipboard());
        var selected = copy.GenerateCodedAndCopy(stamp, _ => [1, 2, 3]);
        var other = copy.GenerateCodedAndCopy(stamp, _ => [4, 5, 6]);
        journal.Annotate(selected, "selected note");
        Assert(new VerificationService(journal).ReadEventHistory(selected).Entries.Count == 4);
        Assert(new VerificationService(journal).ReadEventHistory(selected).Entries.All(x => x.Entry.EventId == selected));
        Assert(new VerificationService(journal).ReadEventHistory(Guid.NewGuid()).Entries.Count == 0);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { new VerificationService(journal).ReadEventHistory(selected, cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
        var path = Path.Combine(dir, "000000000006.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("PNG", "BAD"));
        Throws(() => new VerificationService(journal).ReadEventHistory(selected));
        Throws(() => new VerificationService(journal).ReadEventHistory(Guid.NewGuid()));
    });
    Check("event-history-rejects-signed-but-invalid-other-generations", dir =>
    {
        foreach (var fault in new[] { "event", "code", "image" })
        {
            using var journal = new Journal(Path.Combine(dir, fault), key);
            var selected = new CopyService(journal, new FakeClipboard()).GenerateCodedAndCopy(stamp, _ => [1, 2, 3]);
            var service = new VerificationService(journal);
            Assert(service.ReadEventHistory(selected).Generations.Single().EventId == selected);
            var id = Guid.NewGuid();
            var bad = new Generation(fault == "event" ? Guid.NewGuid() : id, DateTimeOffset.UtcNow,
                stamp with { GeometryCode = fault == "code" ? RingCode.ForEvent(id) ^ 1 : RingCode.ForEvent(id) },
                Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })), fault == "image" ? "BAUG" : "AQID");
            journal.Append("Generated", id, bad); // Valid MAC; invalid semantics in an unrelated event.
            Throws(() => service.ReadHistory());
            Throws(() => service.ReadEventHistory(selected));
        }
    });
    Check("restore-retains-authenticated-images-notes-and-original", dir =>
    {
        using var source = new Journal(Path.Combine(dir, "source"), key);
        var id = new CopyService(source, new FakeClipboard()).GenerateCodedAndCopy(stamp, _ => [1, 2, 3]);
        source.Annotate(id, "restore note");
        var entries = source.Read().Select(x => x.Signed).ToArray();
        var target = Path.Combine(dir, "restored");
        HistoryRestore.ToNewDirectory(target, key, entries, path => File.WriteAllBytes(path, [5]));
        using var restored = new Journal(Path.Combine(target, "journal"), key);
        Assert(restored.Read().Select(x => x.Signed).SequenceEqual(entries));
        var service = new VerificationService(restored);
        Assert(service.Original(JsonSerializer.Serialize(entries[0])).Status == VerificationStatus.Match);
        Assert(service.Image([1, 2, 3]).Status == VerificationStatus.Match);
        Assert(service.ReadGenerations().Single().EventId == id);
        Assert(!Directory.EnumerateDirectories(dir, ".jtc-restore-*").Any());
    });
    Check("restore-rejects-corruption-wrong-key-and-preserves-destination", dir =>
    {
        using var source = new Journal(Path.Combine(dir, "source"), key);
        new CopyService(source, new FakeClipboard()).GenerateCodedAndCopy(stamp, _ => [1]);
        var entries = source.Read().Select(x => x.Signed).ToArray();
        var target = Path.Combine(dir, "restored");
        Throws(() => HistoryRestore.ToNewDirectory(target, RandomNumberGenerator.GetBytes(32), entries, _ => throw new Exception()));
        Assert(!Directory.Exists(target));
        var broken = entries.ToArray(); broken[0] = broken[0] with { Mac = new string('0', 64) };
        Throws(() => HistoryRestore.ToNewDirectory(target, key, broken, _ => throw new Exception()));
        Throws(() => HistoryRestore.ToNewDirectory(target, key, entries.Skip(1).ToArray(), _ => throw new Exception()));
        Assert(!Directory.Exists(target) && !Directory.EnumerateDirectories(dir, ".jtc-restore-*").Any());
        Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "keep"), "original");
        Throws(() => HistoryRestore.ToNewDirectory(target, key, entries, _ => throw new Exception()));
        Assert(File.ReadAllText(Path.Combine(target, "keep")) == "original");
    });
    Check("restore-key-write-failure-and-destination-race", dir =>
    {
        var target = Path.Combine(dir, "restored");
        Throws(() => HistoryRestore.ToNewDirectory(target, key, [], _ => throw new IOException("disk full")));
        Assert(!Directory.Exists(target));
        Throws(() => HistoryRestore.ToNewDirectory(target, key, [], path =>
        {
            File.WriteAllBytes(path, [1]); Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "keep"), "concurrent");
        }));
        Assert(File.ReadAllText(Path.Combine(target, "keep")) == "concurrent");
        Assert(!Directory.EnumerateDirectories(dir, ".jtc-restore-*").Any());
    });
    Check("atomic-save-preserves-existing-on-partial-write", dir =>
    {
        var path = Path.Combine(dir, "original.jtc");
        File.WriteAllText(path, "original");
        Throws(() => AtomicFile.Write(path, output => { output.WriteByte(1); throw new IOException("disk full"); }));
        Assert(File.ReadAllText(path) == "original" && Directory.GetFiles(dir).Length == 1);
        AtomicFile.Write(path, output => output.Write([2, 3]));
        Assert(File.ReadAllBytes(path).SequenceEqual(new byte[] { 2, 3 }));
    });
    Check("atomic-create-failure-and-collision", dir =>
    {
        var path = Path.Combine(dir, "key.dpapi");
        Throws(() => AtomicFile.Write(path, output => { output.WriteByte(1); throw new IOException("interrupted"); }, false));
        Assert(!File.Exists(path) && Directory.GetFiles(dir).Length == 0);
        AtomicFile.Write(path, output => output.WriteByte(2), false);
        Throws(() => AtomicFile.Write(path, output => output.WriteByte(3), false));
        Assert(File.ReadAllBytes(path).SequenceEqual(new byte[] { 2 }) && Directory.GetFiles(dir).Length == 1);
        Directory.CreateDirectory(Path.Combine(dir, "blocked"));
        Throws(() => AtomicFile.Write(Path.Combine(dir, "blocked"), output => output.WriteByte(4)));
        Assert(Directory.GetFiles(dir).Length == 1);
    });
    Check("same-stamp-distinct-images-retain-event-associations", dir =>
    {
        using var journal = new Journal(dir, key);
        var plain = stamp with { Renderer = RingCode.PlainRenderer, GeometryCode = null };
        var service = new CopyService(journal, new FakeClipboard());
        service.GenerateCodedAndCopy(plain, _ => [1]);
        service.GenerateCodedAndCopy(plain, _ => [2]);
        service.GenerateCodedAndCopy(plain, _ => [1]);
        var history = new VerificationService(journal).ReadGenerations();
        var references = HistoryReferences.LatestImages(history);
        Assert(references.Length == 2 && references[0].EventId == history[2].EventId);
        var a = new bool[96 * 96]; PaintStamp(a, 96, 96, 48, 48, 88, false);
        var b = new bool[96 * 96]; PaintStamp(b, 96, 96, 48, 48, 88, true);
        var ranked = ImageSearch.Rank(a, [new(plain, b, history[1].PngSha256), new(plain, a, history[0].PngSha256)]);
        Assert(ranked[0].PngSha256 == history[0].PngSha256);
        var matches = HistoryReferences.Matching(history, ranked[0]).ToArray();
        Assert(matches.Length == 2 && matches.All(g => g.PngSha256 == history[0].PngSha256));
        Assert(HistoryReferences.LatestImages(history, 1).Length == 1);
    });
    Check("generated-copy-annotation-and-reopen", dir =>
    {
        Guid first;
        using (var journal = new Journal(dir, key))
        {
            var clipboard = new FakeClipboard();
            first = new CopyService(journal, clipboard).GenerateCodedAndCopy(stamp, _ => [1, 2, 3]);
            var second = new CopyService(journal, clipboard).GenerateCodedAndCopy(stamp with { DisplayDate = new DateOnly(2100, 1, 1) }, _ => [1, 2, 3]);
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
        Throws(() => new CopyService(journal, clipboard).GenerateCodedAndCopy(stamp, _ => [1]));
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
        Throws(() => new CopyService(journal, clipboard).GenerateCodedAndCopy(stamp, _ => [1]));
        Assert(clipboard.Calls == 0 && journal.Read().Count == 0);
    });
    Check("copy-intent-save-failure-blocks-copy", dir =>
    {
        using var journal = new Journal(dir, key);
        Directory.CreateDirectory(Path.Combine(dir, "000000000002.json"));
        var clipboard = new FakeClipboard();
        Throws(() => new CopyService(journal, clipboard).GenerateCodedAndCopy(stamp, _ => [1]));
        Assert(clipboard.Calls == 0 && journal.Read().Count == 1);
    });
    Check("clipboard-failure-recorded", dir =>
    {
        using var journal = new Journal(dir, key);
        var clipboard = new FakeClipboard { Action = () => throw new IOException("Clipboard busy") };
        Throws(() => new CopyService(journal, clipboard).GenerateCodedAndCopy(stamp, _ => [1]));
        Assert(journal.Read().Last().Entry.Kind == "CopyFailed");
        Assert(!journal.Read().Any(x => x.Entry.Kind == "CopyCompleted"));
    });
    Check("completion-save-failure-is-not-success", dir =>
    {
        using var journal = new Journal(dir, key);
        var clipboard = new FakeClipboard { Action = () => Directory.CreateDirectory(Path.Combine(dir, "000000000003.json")) };
        Throws(() => new CopyService(journal, clipboard).GenerateCodedAndCopy(stamp, _ => [1]));
        Assert(clipboard.Calls == 1 && journal.Read().Last().Entry.Kind == "CopyRequested");
    });
    Check("middle-deletion-detected", dir =>
    {
        using var journal = new Journal(dir, key);
        new CopyService(journal, new FakeClipboard()).GenerateCodedAndCopy(stamp, _ => [1]);
        File.Delete(Path.Combine(dir, "000000000002.json")); Throws(() => journal.Read());
    });
    Check("interrupted-temp-ignored", dir =>
    {
        using var journal = new Journal(dir, key);
        File.WriteAllText(Path.Combine(dir, "crash.tmp"), "partial data");
        Assert(journal.Read().Count == 0);
        new CopyService(journal, new FakeClipboard()).GenerateCodedAndCopy(stamp, _ => [1]);
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
        var first = service.GenerateCodedAndCopy(stamp, _ => [1, 2, 3]);
        var second = service.GenerateCodedAndCopy(stamp, _ => [1, 2, 3]);
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
    Check("verification-message-candidates-are-not-exact-matches", dir =>
    {
        var message = VerificationMessages.Image(new(0x22F, 1, 1, 1, 9, true));
        Assert(message.Kind == VerificationMessageKind.Candidate);
        Assert(message.Title == "対応する生成履歴候補あり" && !message.Title.Contains("判定不能"));
        Assert(message.Findings.Contains("1件") && message.Meaning.Contains("確定していません"));
        Assert(VerificationMessages.ShapeEvidence(0x22F, 0x22F, new(1, 1, 1)).Contains("／ コード一致"));
        Assert(VerificationMessages.ShapeEvidence(0x22F, 0, new(1, 1, 1)).Contains("／ コード不一致"));
        Assert(VerificationMessages.ShapeEvidence(null, 0, new(1, 1, 1)).Contains("／ コード未読取"));
        Assert(VerificationMessages.ScoreHelp.Contains("確率ではありません") && VerificationMessages.ScoreHelp.Contains("完全一致"));
        Throws(() => VerificationMessages.ShapeEvidence(0, 0, new(double.NaN, 1, 1)));
    });
    Check("verification-message-filter-off-and-unread-code", dir =>
    {
        var mixed = VerificationMessages.Image(new(1, 3, 1, 1, 9, false));
        Assert(mixed.Kind == VerificationMessageKind.Candidate && mixed.Findings.Contains("異なる") && mixed.Findings.Contains("2件"));
        var different = VerificationMessages.Image(new(1, 2, 0, 0, 9, false));
        Assert(different.Title == "形が似た履歴候補あり" && different.Findings.Contains("一致していません"));
        var unread = VerificationMessages.Image(new(null, 2, 0, 0, 9, true));
        Assert(unread.Kind == VerificationMessageKind.Candidate && unread.Findings.Contains("読み取れませんでした"));
        var noEvidence = VerificationMessages.Image(new(null, 0, 0, 0, 9, true));
        Assert(noEvidence.Kind == VerificationMessageKind.Unavailable && noEvidence.Title == "照合の手がかりが不足しています");
        Throws(() => VerificationMessages.Image(new(1, 3, 1, 1, 9, true)));
        Throws(() => VerificationMessages.Image(new(null, 1, 1, 1, 9, false)));
    });
    Check("verification-message-no-history-versus-no-candidate", dir =>
    {
        Assert(VerificationMessages.Image(new(1, 0, 0, 1, 0, true)).Kind == VerificationMessageKind.Unavailable);
        var none = VerificationMessages.Image(new(1, 0, 0, 0, 9, true));
        Assert(none.Kind == VerificationMessageKind.NoCandidate && none.Findings.Contains("同じコードはありません"));
        var codeOnly = VerificationMessages.Image(new(1, 0, 0, 3, 9, true));
        Assert(codeOnly.Kind == VerificationMessageKind.NoCandidate && codeOnly.Findings.Contains("3件") && codeOnly.Findings.Contains("比較対象外"));
        Assert(codeOnly.Meaning.Contains("偽造とは判断できません"));
    });
    Check("verification-message-exact-and-integrity-failure", dir =>
    {
        using var journal = new Journal(dir, key);
        var copy = new CopyService(journal, new FakeClipboard());
        copy.GenerateCodedAndCopy(stamp, _ => [1, 2, 3]);
        copy.GenerateCodedAndCopy(stamp, _ => [1, 2, 3]);
        var service = new VerificationService(journal);
        var exact = VerificationMessages.Exact(service.Image([1, 2, 3]), false);
        Assert(exact.Kind == VerificationMessageKind.Exact && exact.Findings.Contains("2件") && exact.Meaning.Contains("複数"));
        var original = VerificationMessages.Exact(service.Original(JsonSerializer.Serialize(journal.Read()[0].Signed)), true);
        Assert(original.Kind == VerificationMessageKind.Exact && original.Findings.Contains("認証情報"));
        var failed = VerificationMessages.Exact(service.Original("{}"), true);
        Assert(failed.Kind == VerificationMessageKind.Unavailable && failed.Meaning.Contains("判断していません"));
        Assert(VerificationMessages.Exact(new(VerificationStatus.Match, "invalid", []), false).Kind == VerificationMessageKind.Unavailable);
    });
    Check("plain-and-coded-snapshots-are-authenticated", dir =>
    {
        using var journal = new Journal(dir, key);
        var clipboard = new FakeClipboard();
        var copy = new CopyService(journal, clipboard);
        var plain = stamp with { Renderer = RingCode.PlainRenderer };
        Stamp? rendered = null;
        var id = copy.GenerateCodedAndCopy(plain, x => { rendered = x; return [1, 2, 3, 4]; });
        Assert(rendered!.GeometryCode is null && rendered.Renderer == RingCode.PlainRenderer);
        RingCode.Validate(rendered);
        Throws(() => RingCode.Validate(plain with { GeometryCode = 0 }));
        var service = new VerificationService(journal);
        var record = service.ReadGenerations().Single();
        Assert(VerificationService.StoredPng(record)!.SequenceEqual(new byte[] { 1, 2, 3, 4 }));
        Assert(service.Image([1, 2, 3, 4]).Matches.Single().EventId == id);
        Assert(service.Original(JsonSerializer.Serialize(journal.Read()[0].Signed)).Status == VerificationStatus.Match);
        Assert(VerificationService.StoredPng(record with { PngBase64 = null }) is null);
        Throws(() => VerificationService.StoredPng(record with { PngBase64 = Convert.ToBase64String([9]) }));
        var sameImageId = copy.GenerateCodedAndCopy(plain, _ => [1, 2, 3, 4]);
        Assert(sameImageId != id && service.Image([1, 2, 3, 4]).Matches.Count == 2);
        Assert(CorrespondenceScore.Calculate(new(1, 1, 1), null, null).Total == .3);
        Assert(VerificationMessages.ShapeEvidence(null, null, new(1, 1, 1)).Contains("プレーン"));
        var path = Path.Combine(dir, "plain.jtcstamp");
        new StampSettings(1, "上段", DateOnly.FromDateTime(DateTime.Today), "下段", true).Save(path);
        Assert(StampSettings.Load(path).Plain);
        File.Delete(path);
        var badId = Guid.NewGuid();
        journal.Append("Generated", badId, record with { EventId = badId, PngBase64 = Convert.ToBase64String([0]) });
        Assert(service.Image([1, 2, 3, 4]).Status == VerificationStatus.Indeterminate);
    });
    Check("result-indicator-does-not-promote-rounded-scores", dir =>
    {
        Assert(ResultIndicator.Candidate(1).Tone == IndicatorTone.Success);
        Assert(ResultIndicator.Candidate(.3).Tone == IndicatorTone.Caution);
        Assert(ResultIndicator.Candidate(.9999).Tone == IndicatorTone.Caution);
        Assert(ResultIndicator.Candidate(.9999).Value == "< 1.000");
        Assert(ResultIndicator.Empty(true).Tone == IndicatorTone.NoCandidate);
        Assert(ResultIndicator.Empty(false).Tone == IndicatorTone.Caution);
        Assert(ResultIndicator.Exact().Value == "一致");
        Throws(() => ResultIndicator.Candidate(double.NaN));
    });
    Check("delete-all-history-preserves-key-owner-and-next-generation", dir =>
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using (var journal = new Journal(dir, key))
        {
            var id = Guid.NewGuid();
            journal.Append("Generated", id, new { Name = "test" });
            journal.Annotate(id, "note");
            File.WriteAllText(Path.Combine(dir, "unrelated.txt"), "keep");
            journal.DeleteAllHistory();
            Assert(journal.Read().Count == 0 && File.Exists(Path.Combine(dir, "unrelated.txt")));
            Throws(() => { using var other = new Journal(dir, key); });
            journal.DeleteAllHistory();
            journal.Append("Generated", Guid.NewGuid(), new { Name = "new" });
            Assert(journal.Read().Single().Entry.Sequence == 1);
        }
        using var reopened = new Journal(dir, key);
        Assert(reopened.Read().Count == 1);
    });
    Check("correspondence-score-components-and-code-collision", dir =>
    {
        var text = new TextSimilarity(1, 1, 1);
        Assert(Math.Abs(CorrespondenceScore.Calculate(text, null, 1).Total - .3) < 1e-10);
        Assert(Math.Abs(CorrespondenceScore.Calculate(text, 2, 1).Total - .3) < 1e-10);
        Assert(CorrespondenceScore.Calculate(text, 1, 1).Total == 1);
        Assert(CorrespondenceScore.Calculate(new(1, 0, 1), 1, 1).Total < .91);
        Assert(VerificationMessages.ShapeEvidence(null, 1, text).Contains("未確認"));
        // Identical text with different events cannot become an Exact result, even with a code collision.
        Assert(VerificationMessages.Image(new(1, 2, 2, 2, 2, true)).Kind == VerificationMessageKind.Candidate);
        var a = new bool[96 * 96];
        foreach (int top in new[] { 15, 42, 74 })
            for (int y = top; y < top + 6; y++) for (int x = 32; x < 48; x++) a[y * 96 + x] = true;
        var b = (bool[])a.Clone();
        for (int y = 35; y < 61; y++) for (int x = 0; x < 96; x++) b[y * 96 + x] = false;
        for (int y = 42; y < 48; y++) for (int x = 55; x < 65; x++) b[y * 96 + x] = true;
        var differentDate = ImageSearch.CompareText(a, b);
        Assert(differentDate.Name == 1 && differentDate.Bottom == 1 && differentDate.Date == 0);
        Assert(ImageSearch.CompareText(new bool[96 * 96], new bool[96 * 96]).Contribution == 0);
        var first = stamp with { GeometryCode = 1 }; var second = stamp with { GeometryCode = 2 };
        Assert(ImageSearch.Rank(a, [new(first, a), new(second, a)], 2)[0].Stamp == second);
    });
    Check("image-search-document-two-stamps-and-red-rectangle", dir =>
    {
        const int w = 640, h = 480;
        var page = new bool[w * h];
        PaintStamp(page, w, h, 80, 100, 88, false);
        PaintStamp(page, w, h, 390, 280, 160, true);
        for (int y = 30; y < 80; y++) for (int x = 480; x < 560; x++) page[y * w + x] = true;
        var found = ImageSearch.Detect(page, w, h);
        Assert(found.Count == 2);
        Assert(found.Any(x => x.X < 80 && x.X + x.Width > 80));
        Assert(found.Any(x => x.X < 390 && x.X + x.Width > 390));
        var reference = new bool[384 * 384]; PaintStamp(reference, 384, 384, 192, 192, 352, false);
        var bounds = ImageSearch.Bounds(reference, 384, 384)!;
        var template = new StampTemplate(stamp, ImageSearch.Normalize(reference, 384, 384, bounds));
        var other = new bool[384 * 384]; PaintStamp(other, 384, 384, 192, 192, 352, true);
        var otherStamp = stamp with { Name = "別印" };
        var templates = new[] { template, new StampTemplate(otherStamp, ImageSearch.Normalize(other, 384, 384, ImageSearch.Bounds(other, 384, 384)!)) };
        foreach (var region in found)
        {
            var ranked = ImageSearch.Rank(ImageSearch.Normalize(page, w, h, region), templates);
            Assert(ranked.Count > 0 && ranked[0].Stamp == (region.X < 200 ? stamp : otherStamp));
            Console.WriteLine($"  synthetic top score {ranked[0].Score:0.000}");
        }
        Assert(ImageSearch.Rank(new bool[96 * 96], templates).Count == 0);
    });
    Check("image-search-small-rotations-and-size-changes", dir =>
    {
        var reference = new bool[384 * 384]; PaintStamp(reference, 384, 384, 192, 192, 352, false);
        var template = new StampTemplate(stamp, ImageSearch.Normalize(reference, 384, 384, ImageSearch.Bounds(reference, 384, 384)!));
        foreach (int size in new[] { 64, 88, 160 }) foreach (int angle in new[] { -7, 0, 5 })
        {
            var page = new bool[320 * 240]; PaintStamp(page, 320, 240, 183, 118, size, false, angle);
            var found = ImageSearch.Detect(page, 320, 240);
            Assert(found.Count == 1);
            var ranked = ImageSearch.Rank(ImageSearch.Normalize(page, 320, 240, found[0]), [template]);
            Assert(ranked.Count == 1);
        }
    });
    Check("image-search-red-alpha-limits-and-empty", dir =>
    {
        var mask = ImageSearch.RedMask([40,32,195,255, 0,0,0,255, 40,32,195,0, 255,255,255,255], 4, 1);
        Assert(mask.SequenceEqual(new[] { true, false, false, false }));
        Assert(ImageSearch.Detect(new bool[10000], 100, 100).Count == 0);
        Assert(ImageSearch.Bounds(new bool[10000], 100, 100) is null);
        Throws(() => ImageSearch.Normalize(mask, 4, 1, new(-1, 0, 3, 1)));
        Throws(() => ImageSearch.RedMask([], int.MaxValue, int.MaxValue));
    });
    Check("geometry-generation-event-binding-original-and-failure", dir =>
    {
        using var journal = new Journal(dir, key); var clipboard = new FakeClipboard();
        Stamp? rendered = null;
        var id = new CopyService(journal, clipboard).GenerateCodedAndCopy(stamp, coded => { rendered = coded; return [1, 2, 3]; });
        var first = journal.Read()[0];
        var generation = JsonSerializer.Deserialize<Generation>(first.Entry.Payload)!;
        Assert(generation.Stamp == rendered && generation.Stamp.GeometryCode == RingCode.ForEvent(id));
        Assert(generation.Stamp.Renderer == RingCode.Renderer && clipboard.Calls == 1);
        Assert(new VerificationService(journal).Original(JsonSerializer.Serialize(first.Signed)).Matches.Single().EventId == id);
        var before = journal.Read().ToArray();
        Throws(() => new CopyService(journal, clipboard).GenerateCodedAndCopy(stamp, _ => throw new InvalidOperationException("Render failed")));
        Assert(before.SequenceEqual(journal.Read()) && clipboard.Calls == 1);
        Throws(() => RingCode.Validate(stamp with { GeometryCode = 4096 }));
        Throws(() => RingCode.Validate(stamp));
        Throws(() => RingCode.Validate(stamp with { Renderer = "unsupported", GeometryCode = 0 }));
        Directory.CreateDirectory(Path.Combine(dir, "000000000004.json"));
        Throws(() => new CopyService(journal, clipboard).GenerateCodedAndCopy(stamp, _ => [4, 5, 6]));
        Assert(clipboard.Calls == 1 && before.SequenceEqual(journal.Read()));
    });
    Check("ring12-all-values-crc-and-validation", dir =>
    {
        foreach (int code in Enumerable.Range(0, 4096))
        {
            var cells = RingCode.Encode(code);
            Assert(RingCode.ReadCells(cells) == code);
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = !cells[i]; Assert(RingCode.ReadCells(cells) is null); cells[i] = !cells[i];
            }
        }
        RingCode.Validate(stamp with { GeometryCode = 0xABC });
        Throws(() => RingCode.Encode(4096));
        Throws(() => RingCode.Validate(stamp with { GeometryCode = 4096 }));
    });
    Check("unsupported-record-does-not-block-current-history", dir =>
    {
        using var journal = new Journal(dir, key);
        var oldId = Guid.NewGuid();
        var oldStamp = stamp with { Renderer = "unsupported", GeometryCode = 0 };
        var original = journal.Append("Generated", oldId, new Generation(oldId, DateTimeOffset.UtcNow, oldStamp, new string('A', 64)));
        var currentId = new CopyService(journal, new FakeClipboard()).GenerateCodedAndCopy(stamp, _ => [8, 9]);
        var verify = new VerificationService(journal);
        Assert(verify.Original(JsonSerializer.Serialize(original)).Status == VerificationStatus.Indeterminate);
        Assert(verify.Image([8, 9]).Matches.Single().EventId == currentId);
        var all = verify.ReadGenerations();
        Assert(all.Count == 1 && all.Single(x => x.EventId == currentId).Stamp.Renderer == RingCode.Renderer);
        Assert(journal.Read()[0].Signed == original);
    });
    Check("ring12-raster-size-rotation-and-controls", dir =>
    {
        int correct = 0;
        foreach (int size in new[] { 88, 176 }) foreach (double rotation in new[] { -4.0, 0, 4.0 })
        foreach (int code in new[] { 0, 1, 0x555, 0xAAA, 0xABC, 4095 })
        {
            var red = RingFixture(size, code, rotation);
            var box = new ImageRegion(0, 0, size, size);
            var reading = RingCode.Decode(red, size, size, box);
            if (reading.Code != code) throw new Exception($"ring12 size={size} rotation={rotation} expected={code} actual={reading.Code} {reading.Reason}");
            correct++;
        }
        Console.WriteLine($"  synthetic ring decoding {correct}/36 (not real-image accuracy)");
        foreach (int code in Enumerable.Range(0, 4))
        {
            var old = SeparatorFixture(88, RingCode.SeparatorDifference(code), 0);
            Assert(RingCode.Decode(old, 88, 88, new(0, 0, 88, 88)).Code is null);
        }
        Assert(RingCode.Decode(new float[88 * 88], 88, 88, new(0, 0, 88, 88)).Code is null);
        Assert(RingCode.Decode(RingFixture(66, 0, 0), 66, 66, new(0, 0, 66, 66)).Code is null);
    });
    Check("gap12-disconnected-circle-discovery", dir =>
    {
        foreach (int size in new[] { 88, 176, 352 })
        {
            int side = size + 40;
            var mask = new bool[side * side];
            var red = RingFixture(size, 1, -4);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                mask[(y + 20) * side + x + 20] = red[y * size + x] > 0.15;
            // A closed interior glyph must not become a second detected stamp.
            for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
                if (Math.Abs(Math.Sqrt(Math.Pow(x - side / 2.0, 2) + Math.Pow(y - side / 2.0, 2)) - 16) < 1)
                    mask[y * side + x] = true;
            var found = ImageSearch.Detect(mask, side, side);
            Assert(found.Count == 1);
            Assert(Math.Abs(found[0].Width - size) <= 2 && Math.Abs(found[0].Height - size) <= 2);
        }
    });
    Console.WriteLine($"{passed} checks passed.");
}
finally { Directory.Delete(root, true); CryptographicOperations.ZeroMemory(key); }
static float[] RingFixture(int size, int code, double rotation)
{
    var red = SeparatorFixture(size, RingCode.SeparatorDifference(code), rotation);
    var cells = RingCode.Encode(code);
    var axes = Enumerable.Range(0, RingCode.CellCount).Where(i => cells[i]).Select(i =>
    {
        double a = (RingCode.CellAngle(i) + rotation) * Math.PI / 180;
        return (Cos: Math.Cos(a), Sin: Math.Sin(a));
    }).ToArray();
    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
    {
        int hit = 0;
        for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
        {
            double xx = ((x + (sx + 0.5) / 4) / size - 0.5) * 87.1;
            double yy = ((y + (sy + 0.5) / 4) / size - 0.5) * 87.1;
            double radius = Math.Sqrt(xx * xx + yy * yy);
            bool ink = Math.Abs(radius - 43) < 0.55;
            if (ink)
                foreach (var a in axes)
                {
                    double along = xx * a.Cos + yy * a.Sin, across = -xx * a.Sin + yy * a.Cos;
                    if (along > 0 && Math.Abs(Math.Atan2(across, along) * 180 / Math.PI) < RingCode.GapDegrees / 2) { ink = false; break; }
                }
            if (ink) hit++;
        }
        red[y * size + x] = Math.Max(red[y * size + x], hit / 16f * 0.64f);
    }
    return red;
}
// Area sampled analytic separators; tests line extraction independently of WPF/fonts.
static float[] SeparatorFixture(int size, double difference, double rotation)
{
    var red = new float[size * size];
    double a = rotation * Math.PI / 180, cosine = Math.Cos(a), sine = Math.Sin(a);
    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
    {
        int hit = 0;
        for (int sy = 0; sy < 8; sy++) for (int sx = 0; sx < 8; sx++)
        {
            double xx = ((x + (sx + 0.5) / 8) / size - 0.5) * 87.1;
            double yy = ((y + (sy + 0.5) / 8) / size - 0.5) * 87.1;
            double u = xx * cosine + yy * sine, v = -xx * sine + yy * cosine;
            double slope = Math.Tan(difference / 2 * Math.PI / 180);
            bool upper = Math.Abs(v + 15 - slope * u) < 0.55;
            bool lower = Math.Abs(v - 15 + slope * u) < 0.55;
            if (Math.Abs(u) < 38 && (upper || lower)) hit++;
        }
        red[y * size + x] = hit / 64f * 0.64f;
    }
    return red;
}
// Deterministic synthetic circle/separator/glyph fixtures; not a real-world accuracy benchmark.
static void PaintStamp(bool[] page, int width, int height, double cx, double cy, double size, bool alternate, double angle = 0)
{
    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
    {
        double dx = (x - cx) * 96 / size, dy = (y - cy) * 96 / size;
        double radians = angle * Math.PI / 180;
        (dx, dy) = (dx * Math.Cos(radians) - dy * Math.Sin(radians), dx * Math.Sin(radians) + dy * Math.Cos(radians));
        double radius = Math.Sqrt(dx * dx + dy * dy);
        bool circle = Math.Abs(radius - 43) <= 0.8;
        bool lines = Math.Abs(Math.Abs(dy) - 15) <= 0.8 && radius <= 43;
        bool text = alternate
            ? Math.Abs(dx) < 20 && (Math.Abs(dy + 26) < 2 || Math.Abs(dy) < 2 || Math.Abs(dy - 26) < 2)
            : Math.Abs(dx) < 25 && Math.Abs(dx % 10) < 2 && Math.Abs(dy) < 34 && Math.Abs(Math.Abs(dy) - 15) > 5;
        if (circle || lines || text) page[y * width + x] = true;
    }
}
sealed class FakeClipboard : IClipboard
{
    public int Calls { get; private set; }
    public Action? Action { get; init; }
    public void Copy(byte[] png) { Calls++; Action?.Invoke(); }
}
