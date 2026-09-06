using Mullion.Core.Model;

namespace Mullion.Core.Abstractions;

public interface IDisplayProvider
{
    IReadOnlyList<DisplayInfo> GetDisplays();

    /// <summary>Notes about how identity was resolved, for the diagnostics view.</summary>
    IReadOnlyList<string> Diagnostics { get; }
}
