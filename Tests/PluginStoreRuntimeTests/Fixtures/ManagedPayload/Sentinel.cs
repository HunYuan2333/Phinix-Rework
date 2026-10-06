using System;
using System.IO;
using System.Runtime.CompilerServices;

// Trusted test fixture only. Validation must never load this assembly or run this code.
public static class Sentinel
{
#pragma warning disable CA2255 // An initializer is intentional: validation must never execute it.
    [ModuleInitializer]
    public static void OnModuleLoad()
    {
        string path = Environment.GetEnvironmentVariable("PHINIX_PAYLOAD_TEST_SENTINEL");
        if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, "Unexpected execution");
    }
}
