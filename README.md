# Mullion

Hotkey-driven window tiling for Windows. Press `Win`+a key, and the focused
window snaps to a zone.

A *mullion* is the vertical bar that divides a window into panes — which is
what this does to a wide monitor.

## Why it exists

Binding `Win+A` normally requires remapping the Windows key with PowerToys
first, because `RegisterHotKey` cannot claim shell-reserved `Win` combinations.
Mullion uses a low-level keyboard hook, which sits ahead of the shell in the
input chain and can take those keys directly. No remap in the chain.

## The idea

The left-hand key block is already a 2×3 grid, so the display arrangement is
mapped onto it position for position:

```
   Q  W  E        upper region of each column
   A  S  D        the whole of each column      <- home row
   Z  X  C        lower region of each column
```

Two properties make this workable:

- **The home row always means "the whole of this column."** Whatever else
  changes, `A`/`S`/`D` addresses the displays themselves, so the rows above and
  below are strictly additive and nothing already learned moves.
- **Nothing is hardcoded per display type.** Split counts derive from the
  long-axis ratio and a DPI-scaled pixel floor, so a 3:2 laptop or a rotated
  32:9 gets a sensible layout without having been anticipated.

On a 5120×1440 that produces:

```
  Q  1280×696    W  2560×696    E  1280×696
  A  1280×1392   S  2560×1392   D  1280×1392     <- 25 / 50 / 25, 16:9 center
  Z  1280×696    X  2560×696    C  1280×696
```

## Installing

```
.\tools\Publish.ps1
```

Produces one self-contained `publish\Mullion.exe` — about 63MB, with the .NET
runtime inside it. Copy it anywhere and run it; nothing else has to be present
on the machine and nothing has to be kept in step with it.

Then turn on **Start Mullion when I sign in** in Settings. Auto-start records
wherever the exe is at that moment, so move it first and set it afterwards.

The publish runs the tests and refuses to produce an exe if any fail. A hotkey
tool that is broken is worse than one that is absent: it swallows keystrokes on
their way to whatever you actually wanted.

Not trimmed, deliberately. Avalonia resolves controls, converters and styles by
name at runtime, so a trimmer that cannot see those uses removes them — and the
failure is a blank window at launch rather than a build error.

## Building


Requires the .NET 10 SDK (pinned in `global.json`).

```
dotnet build
dotnet test
dotnet run --project src/Mullion.App
```

## Cycling

Press a key again **while still holding Win** and the window widens through a
cycle — its zone, then the half of the display it sits in, then the display.
Releasing Win resets to the first step, so an accidental extra press cannot
leave a window somewhere unexpected minutes later.

```
Win held, A tapped 3x    1280 -> 2560 -> 5120 wide
Win released between     1280 each time
```

## Simulated arrangements

Most of the arrangements the layout engine is built for cannot be produced on
one desk. `--simulate` swaps in a synthetic display provider and shows exactly
what a first launch would make of one:

```
dotnet run --project src/Mullion.App -- --simulate two-across
dotnet run --project src/Mullion.App -- --simulate verticals-flanking-stacked
```

The presets live in `Mullion.Core/Simulation/SimulatedTopologies.cs` — a lone
32:9, a rotated one, three portraits, an L-shape, a mixed-DPI laptop and dock,
a 5:4 beside a 16:9, and others.

Simulating is read-only and says so on screen: no hook is installed, nothing is
saved, the watchers stay down, and the layout is always generated fresh rather
than matched to a stored profile. The zones describe monitors that are not
there, so a live hotkey would throw a real window off-screen.

`tools/Capture-Simulations.ps1` screenshots every preset into `screenshots/`,
which is how the defaults get reviewed side by side.

## Drag to snap

Hold **Shift** while dragging a window and the zones appear; the one under the
cursor highlights; release and the window lands in it, through the same mover
the hotkeys use. The modifier is configurable, and can be set to none to have
the zones appear on every drag.

The zones offered are the largest tiling the layout allows, not every zone.
Zones overlap on purpose — a column holds the whole of itself *and* its halves,
on different keys — which a key can disambiguate and a pointer cannot, since it
sits inside all three at once.

Windows that draw their own title bars still work. Chromium and Electron
implement a custom caption by returning `HTCAPTION` from `WM_NCHITTEST`, so the
OS runs its normal move loop and `EVENT_SYSTEM_MOVESIZESTART` fires as it does
for any other window — verified with `--drag-test` on Brave and VS Code.

**PowerToys FancyZones claims the same gesture.** Two zone managers on one drag
both move the window and whichever finishes last wins, so the result changes
from drag to drag — which looks like Mullion being unreliable rather than like a
conflict. Mullion detects this and says so, with a button to open PowerToys.

## The probe

`tools/Mullion.Probe` exercises the engine without any UI in the way, which is
how most of it was verified:

```
mullion-probe                        detected displays and the derived layout
mullion-probe --self-test            drive a real window through every zone
mullion-probe --hotkey-test          install the hook and verify Win+key end to end
mullion-probe --cycle-test           verify repeat-press cycling and reset-on-release
mullion-probe --watch-displays       log display-change messages as they arrive
mullion-probe --inject Q             inject Win+Q at an already-running Mullion
mullion-probe --snap S --delay 3     snap the foreground window
mullion-probe --undo                 restore the last move
mullion-probe --drag-test            log which apps report a drag, and the zone under the cursor
mullion-probe --rebind-survives      verify a rebind outlives the next layout rebuild
```

## Supporting it

Release builds can carry a "Support Mullion" button; a build from a clean
checkout cannot. The destination is a build property, empty in this repository,
and with nothing set the button does not render at all — so a fork ships no ask,
and there is nothing to remember to strip out.

```
.\tools\Publish.ps1 -SponsorUrl "https://github.com/sponsors/<user>"
```

Include an `{amount}` placeholder and the ask becomes a picker — $3 / $5 / $10 /
$25 and Other, defaulting to $5 — with the chosen sum substituted into the link.
Only worth doing where the destination actually reads an amount out of the URL:
offering a choice that the payment page never hears about is worse than not
asking.

## The icon

`tools/icon/Build-Icon.ps1` regenerates `mullion.ico` and, with `-Mockup`, a
contact sheet of every candidate at every size on light and dark backgrounds.
16px is where icons fail, so they are judged there rather than by scaling a
large one down.

## Layout

```
src/Mullion.Core/               no OS calls; all the layout maths and hotkey matching
src/Mullion.Platform.Windows/   every P/Invoke, and nothing else
src/Mullion.App/                Avalonia UI, tray, wizard
tools/Mullion.Probe/            diagnostic CLI
tests/Mullion.Core.Tests/       132 tests, run on any OS
```

`Mullion.Core` references no Windows or Avalonia assemblies. That boundary is
what keeps the layout maths testable without hardware and makes a macOS backend
a self-contained job rather than a rewrite.

## Known limitations

- **Elevated windows.** Windows blocks a medium-integrity process from
  interfering with an elevated one. Mullion cannot move such a window, and its
  hook receives no key events at all while one has focus — the hotkey silently
  does nothing. Opt into elevated auto-start (a scheduled task, so no UAC prompt
  each logon) if you need to manage them.
- **Competing hooks.** Hooks are called in install order, most recent first.
  Mullion beats a tool that started at boot only because it started later; if
  that tool restarts it claims the key first. Mullion detects PowerToys
  Keyboard Manager remaps that collide and says so.
- **Wayland.** Linux support would mean X11 only. Wayland deliberately forbids
  programmatic window positioning, so this app's core function cannot work
  there regardless of effort.
- **macOS and Linux are not implemented.** The abstractions are in place; the
  backends are not.

- **Tray icon visibility.** Windows 11 hides new tray icons in the overflow
  flyout by default. Drag it onto the taskbar, or enable it under Settings →
  Personalization → Taskbar → Other system tray icons.

## Not yet built

Named layout snapshots and per-app rules.

Drag-to-snap across several monitors has never been exercised: the per-zone
overlay exists for mixed DPI, which a single display cannot produce.
