using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PhinixClient.Storage
{
    internal static class SqliteRuntime
    {
        private static readonly object gate = new object();
        private static bool ready;
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string name);
        [DllImport("libdl.so.2")] private static extern IntPtr dlopen(string name, int flags);
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlopen")] private static extern IntPtr macopen(string name, int flags);
        internal static void Ensure()
        {
            lock (gate)
            {
                if (ready) return;
                string directory = Path.GetDirectoryName(typeof(SqliteRuntime).Assembly.Location);
                bool windows = Path.DirectorySeparatorChar == '\\';
                bool mac = !windows && File.Exists("/usr/lib/libSystem.B.dylib");
                string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
                mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
                string file = windows ? "e_sqlite3.dll" : mac ? "libe_sqlite3.dylib" : "libe_sqlite3.so";
                string relative = Path.Combine("runtimes", (windows ? "win-" : mac ? "osx-" : "linux-") + architecture, "native", file);
                // Verse recursively loads DLLs under Assemblies as managed code.
                // Native dependencies must be a sibling of that scan root.
                string native = Path.GetFullPath(Path.Combine(directory, "..", "Native", relative));
                if (!File.Exists(native) && !string.Equals(Path.GetFileName(directory), "Assemblies", StringComparison.OrdinalIgnoreCase))
                    native = Path.Combine(directory, relative); // ordinary SDK/test output, outside a mod scan root
                if (!File.Exists(native)) throw new DllNotFoundException("Packaged SQLite native library is missing: " + native);
#if PHINIX_RIMWORLD_17
                    NativeLibrary.SetDllImportResolver(typeof(SQLitePCL.SQLite3Provider_e_sqlite3).Assembly,
                        (name, assembly, search) => name == "e_sqlite3" ? NativeLibrary.Load(native) : IntPtr.Zero);
#else
                    // CLR/Mono resolve subsequent imports against the preloaded module's basename.
                    IntPtr handle = windows ? LoadLibraryW(native) : mac ? macopen(native, 2) : dlopen(native, 2);
                    if (handle == IntPtr.Zero) throw new DllNotFoundException("Packaged SQLite native library could not load.");
#endif
                SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
                ready = true;
            }
        }
    }
}
