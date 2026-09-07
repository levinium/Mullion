using System.Reflection;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

/// <summary>
/// The boundary the whole design rests on: Mullion.Core knows nothing about
/// Windows and nothing about Avalonia.
/// <para>
/// It is what lets the layout engine be tested on any machine with no hardware -
/// every arrangement this app has to handle is one nobody here can plug in - and
/// what makes a macOS or Linux backend a self-contained job rather than an
/// excavation. Both are easy to lose by accident: one convenient using
/// directive for a Rect or a Size and the reference is there for good, with
/// nothing failing to say so.
/// </para>
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Core = typeof(LayoutBuilder).Assembly;

    private static IReadOnlyList<string> ReferencedBy(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty)];

    [Fact]
    public void CoreDoesNotReferenceAvalonia()
    {
        // A UI framework in here would mean the layout maths could only run
        // where a UI can be created, which is most of the reason the split
        // exists at all.
        ReferencedBy(Core)
            .ShouldNotContain(
                name => name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase),
                "Mullion.Core must stay drawable-by-anything");
    }

    [Fact]
    public void CoreDoesNotReferenceAPlatformBackend()
    {
        // The dependency runs the other way: a backend implements Core's
        // abstractions. Pointing back at one would make Core need the very thing
        // it exists to be independent of.
        ReferencedBy(Core)
            .ShouldNotContain(
                name => name.StartsWith("Mullion.Platform", StringComparison.OrdinalIgnoreCase),
                "Mullion.Core must not know which platform is carrying it");
    }

    [Fact]
    public void CoreDoesNotReferenceTheApp()
    {
        ReferencedBy(Core)
            .ShouldNotContain(
                name => name.Equals("Mullion", StringComparison.OrdinalIgnoreCase),
                "the app builds on Core, not the other way round");
    }

    [Fact]
    public void CoreIsNotBuiltForAParticularOperatingSystem()
    {
        // A windows-only target framework would compile here today and refuse to
        // build on the machine someone eventually ports this on.
        Core.GetCustomAttribute<System.Runtime.Versioning.TargetPlatformAttribute>()
            .ShouldBeNull("Mullion.Core should target plain net10.0");
    }

    [Fact]
    public void NothingInCoreIsMarkedWindowsOnly()
    {
        // The attribute is how a Windows-only API announces itself; carrying one
        // means something in here is not portable after all, whatever the
        // assembly references happen to say.
        var windowsOnly = Core.GetTypes()
            .Where(t => t.GetCustomAttribute<System.Runtime.Versioning.SupportedOSPlatformAttribute>()
                is { PlatformName: var p } && p.StartsWith("windows", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.FullName)
            .ToList();

        windowsOnly.ShouldBeEmpty();
    }
}
