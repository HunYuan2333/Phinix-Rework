using System;

// Disk-only inspection cannot establish the current game's loaded identities.
internal static class LocalIdentityCheck
{
    internal static int Check(string[] args)
    {
        Console.Error.WriteLine("Retired: --local-identities cannot validate a running RimWorld environment. Use the in-game planner and startup preflight; no third-party mod files were inspected.");
        return 2;
    }
}
