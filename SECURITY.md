# Security

## Reporting a vulnerability

Please report security issues privately through
**[GitHub's private vulnerability reporting](https://github.com/levinium/Mullion/security/advisories/new)**
rather than opening a public issue.

You should get an acknowledgement within a week. This is a single-maintainer
project, so please allow reasonable time for a fix before disclosing publicly.

## Why this app installs a keyboard hook

Mullion installs a `WH_KEYBOARD_LL` low-level keyboard hook. That is the same
Windows mechanism a keylogger uses, so it deserves a plain explanation rather
than a reassurance.

**Why it is necessary.** Mullion's entire purpose is binding `Win`+letter to move
windows. Windows will not allow that through the supported API:
`RegisterHotKey` refuses shell-reserved combinations, because the shell has
already claimed `Win+A`, `Win+S`, `Win+D` and the rest. A low-level hook is the
only mechanism Windows offers that sits ahead of the shell in the input chain.
The alternative the ecosystem settled on — remapping the Windows key with
PowerToys first — is a workaround that affects every other application on the
machine.

**What the hook does with a keystroke.** The callback matches the event against a
table of bindings and decides one thing: swallow it or pass it on. Matched
events are posted to a worker queue by scan code. Nothing else happens in the
callback, because Windows silently uninstalls a hook whose callback is slow.

**What is never recorded.** Keystrokes are not logged, buffered, accumulated,
timed, counted or transmitted. The log records what Mullion *did* — hook
reinstalls, window move outcomes, configuration problems — and where a hotkey is
mentioned, only the **name of the chord that matched a binding** is written, for
example `Win+A`. A key that matches no binding leaves no trace of any kind.

This is a rule the code is built around rather than a promise made about it, and
it is stated in the source at `src/Mullion.App/Services/Log.cs`.

## What leaves the machine

Under normal use, nothing.

The app makes exactly one kind of network request: a once-a-day HTTP GET asking
GitHub whether a newer release exists. It carries no identifier, no machine
details, no configuration, no usage data and no version in a query string — it is
a request for a public file, and the comparison happens locally. It can be turned
off in **Settings → About**, and with it off the app makes no network requests at
all.

The only other request is the release download itself, and only when a person
explicitly asks for an update.

Two source files in the entire application reference `HttpClient`, and both exist
solely for that feature:

```
src/Mullion.App/Services/UpdateService.cs      the daily check
src/Mullion.App/Services/UpdateInstaller.cs    the download, on request
```

There is no telemetry, no analytics, no crash reporting and no account.

## What stays on the machine

| What | Where |
|---|---|
| Settings and zone layouts | `%APPDATA%\Mullion\config.json` |
| Diagnostic log | `%LOCALAPPDATA%\Mullion\logs\mullion.log` |

Both paths are shown in **Settings → About**, with buttons to open them, so you
can read exactly what the app has written.

## Updating itself

Mullion can replace its own executable. Because that means downloading a program
and arranging for the machine to run it, it is deliberately constrained:

- **Nothing happens without two explicit clicks.** There is no silent or
  automatic update.
- **The download must match the checksum published beside the release**, or it is
  discarded and nothing on disk changes. A release that publishes no checksum is
  refused rather than installed unverified.
- **If the running copy carries a valid Authenticode signature, the replacement
  must carry a valid signature from the same publisher.** This is inert while the
  binary is unsigned, and becomes effective the moment it is signed — with no way
  for a signed install to be downgraded to an unsigned file.
- **The swap cannot leave the machine without a working Mullion.** The running
  executable is renamed aside, the verified download takes its place, and if that
  second step fails the first is undone.

Be clear about what the checksum does and does not establish: it is published
from the same place as the download, so it proves the file arrived intact, not
that the release is trustworthy. **Code signing is what would establish that**,
and the binary is not signed yet — an application to
[SignPath Foundation](https://signpath.org/) is the intended route.

## Verifying a download

Every release publishes `Mullion.exe.sha256` beside the executable.

```powershell
(Get-FileHash .\Mullion.exe -Algorithm SHA256).Hash
```

Compare that with the contents of the `.sha256` file. They should match, ignoring
case.

## Reproducing a release

Releases are built by GitHub Actions from a tagged commit, never from a
maintainer's machine. The workflow is `.github/workflows/release.yml`, its logs
are public, and it refuses to run if the tag disagrees with the version in
`Directory.Build.props`.

## Antivirus false positives

A keyboard hook inside a compressed single-file bundle has the same shape as a
packed keylogger, and some engines flag it on that basis. If you hit one, the
source is here to read, the build is reproducible from a public workflow, and the
checksum lets you confirm you have the file that workflow produced.
