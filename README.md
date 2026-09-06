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
  A  1280×1392   S  2560×1392   D  1280×1392     <- 25 / 50 / 25, 16:9 centre
  Z  1280×696    X  2560×696    C  1280×696
```

## Building

Requires the .NET 10 SDK (pinned in `global.json`).

```
dotnet build
dotnet test
dotnet run --project src/Mullion.App
```

## The probe

`tools/Mullion.Probe` exercises the engine without any UI in the way, which is
how most of it was verified:

```
mullion-probe                        detected displays and the derived layout
mullion-probe --self-test            drive a real window through every zone
mullion-probe --hotkey-test          install the hook and verify Win+key end to end
mullion-probe --inject Q             inject Win+Q at an already-running Mullion
mullion-probe --snap S --delay 3     snap the foreground window
mullion-probe --undo                 restore the last move
```

## Layout

```
src/Mullion.Core/               no OS calls; all the layout maths and hotkey matching
src/Mullion.Platform.Windows/   every P/Invoke, and nothing else
src/Mullion.App/                Avalonia UI, tray, wizard
tools/Mullion.Probe/            diagnostic CLI
tests/Mullion.Core.Tests/       102 tests, run on any OS
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

## Not yet built

A settings UI (zones and keybindings are currently changed by re-running the
wizard or editing `%APPDATA%\Mullion\config.json`), repeat-press cycling wired
to the UI, and display-change layout rescue.
