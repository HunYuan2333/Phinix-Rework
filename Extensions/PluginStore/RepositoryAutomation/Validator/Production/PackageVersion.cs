using System;
using System.Globalization;

namespace Phinix.PluginStore
{
    // Package versions deliberately exclude CLR's fourth component and tag prefixes.
    internal sealed class PackageVersion : IComparable<PackageVersion>
    {
        private PackageVersion(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public static bool TryParse(string text, out PackageVersion version)
        {
            version = null;
            if (string.IsNullOrEmpty(text) || text.Length > 32) return false;
            string[] parts = text.Split('.');
            if (parts.Length != 3) return false;
            int[] values = new int[3];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0 || (parts[i].Length > 1 && parts[i][0] == '0')) return false;
                foreach (char c in parts[i]) if (c < '0' || c > '9') return false;
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i])) return false;
            }
            version = new PackageVersion(values[0], values[1], values[2]);
            return true;
        }

        public int CompareTo(PackageVersion other)
        {
            if (other == null) return 1;
            int result = Major.CompareTo(other.Major);
            if (result == 0) result = Minor.CompareTo(other.Minor);
            return result == 0 ? Patch.CompareTo(other.Patch) : result;
        }

        public override string ToString()
        {
            return Major.ToString(CultureInfo.InvariantCulture) + "." +
                Minor.ToString(CultureInfo.InvariantCulture) + "." + Patch.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal sealed class PackageVersionRange
    {
        private PackageVersionRange(string text, PackageVersion exact, PackageVersion lower, PackageVersion upper)
        {
            Text = text;
            Exact = exact;
            Lower = lower;
            Upper = upper;
        }

        public string Text { get; }
        public PackageVersion Exact { get; }
        public PackageVersion Lower { get; }
        public PackageVersion Upper { get; }

        public static bool TryParse(string text, out PackageVersionRange range)
        {
            range = null;
            PackageVersion exact;
            if (PackageVersion.TryParse(text, out exact))
            {
                range = new PackageVersionRange(text, exact, null, null);
                return true;
            }
            if (string.IsNullOrEmpty(text) || text.Length > 80) return false;
            string[] parts = text.Split(' ');
            PackageVersion lower, upper;
            if (parts.Length != 2 || !parts[0].StartsWith(">=", StringComparison.Ordinal) ||
                !parts[1].StartsWith("<", StringComparison.Ordinal) ||
                !PackageVersion.TryParse(parts[0].Substring(2), out lower) ||
                !PackageVersion.TryParse(parts[1].Substring(1), out upper) || lower.CompareTo(upper) >= 0) return false;
            range = new PackageVersionRange(text, null, lower, upper);
            return true;
        }

        public bool Contains(PackageVersion version)
        {
            if (version == null) return false;
            return Exact != null ? Exact.CompareTo(version) == 0 : Lower.CompareTo(version) <= 0 && Upper.CompareTo(version) > 0;
        }
    }
}
