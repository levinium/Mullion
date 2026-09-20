# Changelog

## How versioning works here

The version lives in exactly one place: `<VersionPrefix>` in
`Directory.Build.props`. Everything else reads it off the compiled assembly — the
About panel in Settings, the update check's idea of what "current" means, and the
file properties of the published `Mullion.exe` — so none of them can disagree with
each other. The build also stamps the commit onto `InformationalVersion`, because
"1.0.2" names a release and not the exact build, and a bug report wants the build.

**To release a new version:**

1. Bump `<VersionPrefix>` in `Directory.Build.props`.
2. Add a section below describing what changed.
3. Commit, then tag and push: `git tag v1.2.3` and `git push origin master v1.2.3`.
4. The Release workflow refuses a tag that does not match the version, runs every
   test, builds the one self-contained `Mullion.exe`, writes its SHA-256 beside
   it, and creates a **draft** release.
5. Check the files, edit the draft's notes, and publish it.

That last step is the one that matters to people who already have the app: the
update check reads the published release, so nothing is offered to anybody until
the draft is published, and a release left as a draft is a release nobody gets.

`.\tools\Publish.ps1` builds the same executable locally, for trying a build
before tagging it.

Numbering follows the usual three parts, `MAJOR.MINOR.PATCH`:

- **PATCH** for fixes only.
- **MINOR** for new capability, backwards compatible.
- **MAJOR** for a change that breaks an existing setup — config that will not
  load, or default hotkeys moving under someone who has learned them.

Settings live in `%APPDATA%\Mullion\config.json`, with a backup kept beside it.
The file carries a schema version and is migrated forward when an older one is
read, so upgrading keeps your zones and hotkeys. A file that cannot be read falls
back to the backup, and then to defaults, rather than stopping the app — which is
why a format change is a MAJOR one only when it would genuinely cost you
something you had set up.

---

## 1.0.2

**Checking for updates no longer closes the app.** Pressing **Check for updates**
in Settings ended Mullion instead of answering it. The reply came back from a
background thread and went straight into the window, which the UI toolkit refuses
to allow, and the resulting error was not caught anywhere.

The once-a-day check was never affected, and neither was anything about how
updates are found or installed — only the button that goes looking on purpose,
and the re-check that runs at the start of a download. If updating has seemed
broken, it was this, and nothing was wrong with the release you were being
offered.

---

## 1.0.1

**Windows snapped to a zone now meet exactly.** A window sized to a zone still
showed a one-pixel line of whatever was behind it along all four edges, and two
windows in adjacent zones showed two — one from each side of the seam. Zones tile
the screen with nothing between them, so those lines read as the tiling being
slightly wrong.

The cause was that Windows reports a window's bounds as including a translucent
border it paints around the outside. Aiming those bounds at a zone therefore put
the border on the boundary rather than the window, which is exactly one pixel of
daylight per edge. Mullion now asks Windows how thick that border is and counts
it as part of the frame, the same way it already accounted for the invisible
resize border that surrounds every window.

---

## 1.0.0

First release.

Press `Win` and a key and the window you are looking at snaps to a zone. Mullion
reads the monitor arrangement it finds, works out a split that suits the shape of
each screen, and lays the zones onto the left-hand keys so the keyboard becomes a
small map of the desk. Tap the same key again while still holding `Win` and the
window grows through its zone, then the half it sits in, then the whole screen.
Hold `Shift` while dragging instead and the zones appear to drop a window into.

Where the guess is not what you wanted, the zone editor changes it. Zones are
stored as proportions rather than pixels, so unplugging a monitor, changing a
resolution or docking a laptop moves them rather than losing them. ``Win+` ``
minimizes, `Win+Backspace` takes back the last move, and every key is rebindable.

One file, nothing to install, and no configuration needed before it does
anything.
