using Mullion.App.Services;
using Mullion.Core.Geometry;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// What the overlay promises while a window is being dragged.
/// <para>
/// The overlay is the only warning of what letting go will do, so a zone that
/// fills itself is a commitment: the window is going to end up filling this. It
/// held for the original gesture, where dropping always meant "snap to here",
/// and stopped holding the moment dropping back on a zone came to mean "return
/// to the size you were". A filled zone then advertised the one outcome that
/// was not about to happen.
/// </para>
/// </summary>
public class DragZoneOverlayTests
{
    private static readonly PxRect Zone = new(1280, 0, 2560, 1392);

    [Fact]
    public void AZoneThePointerIsNotOverIsBarelyThere()
    {
        var look = DragZoneOverlay.AppearanceOf(isHovered: false, Zone, landing: null);

        look.ZoneOpacity.ShouldBe(0.10);
        look.BorderThickness.ShouldBe(1);
        look.Preview.ShouldBeNull();
    }

    [Fact]
    public void AZoneAboutToBeFilledFillsItself()
    {
        var look = DragZoneOverlay.AppearanceOf(isHovered: true, Zone, landing: Zone);

        look.ZoneOpacity.ShouldBe(0.28);
        look.BorderThickness.ShouldBe(3);
        look.Preview.ShouldBeNull("the zone's own fill already says where the window goes");
    }

    [Fact]
    public void AZoneWithNothingKnownAboutTheLandingStillFillsItself()
    {
        // No remembered size, so dropping simply snaps - which is what a filled
        // zone has always meant.
        var look = DragZoneOverlay.AppearanceOf(isHovered: true, Zone, landing: null);

        look.ZoneOpacity.ShouldBe(0.28);
        look.Preview.ShouldBeNull();
    }

    [Fact]
    public void AZoneAboutToRestoreOutlinesItselfAndFillsTheWindowInstead()
    {
        // The behavior asked for: the zone says "this is where you are aiming"
        // and the smaller rectangle says "this is what you will get".
        var landing = new PxRect(2110, 396, 900, 600);

        var look = DragZoneOverlay.AppearanceOf(isHovered: true, Zone, landing);

        look.ZoneOpacity.ShouldBe(0.0, "a hollow zone, so only one shape is filled");
        look.BorderThickness.ShouldBe(3, "still the zone under the pointer");
        look.Preview.ShouldNotBeNull();
    }

    [Fact]
    public void TheFilledRectangleIsExactlyWhereTheWindowWillLand()
    {
        var landing = new PxRect(2110, 396, 900, 600);

        var preview = DragZoneOverlay.AppearanceOf(isHovered: true, Zone, landing).Preview;

        // Stated as fractions of the zone, so it lands correctly whatever the
        // display's scaling turns pixels into - and it has to survive the round
        // trip back, or the rectangle drawn is not the one described.
        preview!.Value.Project(Zone).ShouldBe(landing);
    }

    [Fact]
    public void AWindowAFewPixelsShortOfTheZoneIsStillJustFillingIt()
    {
        // Windows that resist being sized exactly land a pixel or two short.
        // Treating that as a restore would show a preview all but identical to
        // the zone behind it, which reads as a rendering fault.
        var landing = new PxRect(Zone.X + 2, Zone.Y + 1, Zone.Width - 3, Zone.Height - 2);

        DragZoneOverlay.AppearanceOf(isHovered: true, Zone, landing).Preview.ShouldBeNull();
    }
}
