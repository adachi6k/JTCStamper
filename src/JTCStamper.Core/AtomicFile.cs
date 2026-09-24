namespace JTCStamper.Core;

// Publish only a fully written, flushed file. The temporary file is on the same volume.
public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write, bool overwrite = true)
    {
        var target = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".jtc-write-" + Guid.NewGuid() + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }
            File.Move(temporary, target, overwrite);
        }
        finally
        {
            // A cleanup failure must not hide the original write/replace error.
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
