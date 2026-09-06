using System.Security.Cryptography;
using System.Text;
using Mullion.Core.Model;

namespace Mullion.Core.Config;

/// <summary>
/// Two hashes describing a display arrangement.
/// <para>
/// Split deliberately: <paramref name="Hardware"/> answers "are these the same
/// monitors?" and <paramref name="Arrangement"/> answers "are they still
/// positioned the same way?". Rearranging monitors or changing a resolution
/// should reuse the existing profile with zones reprojected, not create a new
/// one - which needs the two questions answered separately.
/// </para>
/// </summary>
public readonly record struct TopologyFingerprint(string Hardware, string Arrangement)
{
    public override string ToString() => $"{Hardware}/{Arrangement}";

    public static TopologyFingerprint Compute(IReadOnlyList<DisplayInfo> displays)
    {
        // Sorted by stable key, never by enumeration order, so the fingerprint
        // does not change just because Windows enumerated the monitors
        // differently after a reboot.
        var ordered = displays.OrderBy(d => d.StableKey, StringComparer.Ordinal).ToList();

        var hardware = string.Join('|', ordered.Select(d => d.StableKey));

        var arrangement = string.Join('|', ordered.Select(d =>
            $"{d.StableKey}:{d.Bounds.X},{d.Bounds.Y},{d.Bounds.Width},{d.Bounds.Height}:{d.Dpi}:{(d.IsPrimary ? 1 : 0)}"));

        return new TopologyFingerprint($"hw_{Short(hardware)}", $"ar_{Short(arrangement)}");
    }

    private static string Short(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash)[..8];
    }
}
