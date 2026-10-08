using System;
using System.IO;
using System.Linq;
using System.Threading;
using Phinix.PluginStore;

internal static partial class Program
{
    private static void CacheTemporaryPathShape()
    {
        // Reproduce the reported path lengths without relying on Linux allowing long paths.
        string directory = new string('x', 204);
        string target = Path.Combine(directory, "managed-catalog.cache");
        string oldTemporary = target + "." + new string('a', 32) + ".tmp";
        string temporary = RepositoryCacheWrite.TemporaryPath(directory);
        Assert(target.Length == 226 && oldTemporary.Length == 263, "Reported cache path shape exceeds the traditional Windows file limit only when staging.");
        Assert(temporary.Length == 241 && temporary.Length < 260, "Shared compact cache staging fits the reported Windows path shape.");
        Assert(Path.GetDirectoryName(temporary) == directory, "Cache staging stays beside the target for replacement on the same volume.");
        Assert(temporary != RepositoryCacheWrite.TemporaryPath(directory), "Concurrent cache writes receive independent temporary names.");
    }

    private static void CheckCacheStaging(string directory, string target, Func<RepositoryCacheWrite> stage)
    {
        byte[] original = File.ReadAllBytes(target);
        // An old or unrelated temporary file is never this operation's cleanup responsibility.
        string foreign = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(foreign, "unrelated temporary file");
        try
        {
            using (var first = stage())
            using (var second = stage())
            {
                string[] files = Directory.GetFiles(directory, "*.tmp").Where(p => p != foreign).ToArray();
                Assert(files.Length == 2, "Both cache paths stage concurrent writes without collisions.");
                foreach (string path in files)
                {
                    Guid id;
                    string name = Path.GetFileName(path);
                    Assert(name.Length == 36 && name.EndsWith(".tmp", StringComparison.Ordinal) &&
                        Guid.TryParseExact(name.Substring(0, 32), "N", out id), "Actual staged filename uses only a full random ID and tmp suffix.");
                }
                Assert(File.ReadAllBytes(target).SequenceEqual(original), "Staging leaves the valid prior cache intact.");
                first.Dispose();
                Assert(Directory.GetFiles(directory, "*.tmp").Length == 2, "Disposing one stage preserves the other stage and unrelated file.");
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    bool cancelled = false;
                    try { second.Commit(cancellation.Token); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Assert(cancelled && File.ReadAllBytes(target).SequenceEqual(original), "Cancelled cache commit preserves the prior valid bundle.");
                }
            }
            Assert(Directory.GetFiles(directory, "*.tmp").SequenceEqual(new[] { foreign }), "Cancellation cleanup removes only the owned temporary files.");
            using (var next = stage()) next.Commit(CancellationToken.None);
            Assert(File.ReadAllBytes(target).SequenceEqual(original), "Compact cache staging still replaces the entire validated bundle.");
            Assert(File.Exists(foreign) && Directory.GetFiles(directory, "*.tmp").Length == 1, "Successful replacement preserves unrelated temporary files.");
        }
        finally { File.Delete(foreign); }
    }
}
