# Code signing policy

How a Mullion release gets made, and what guarantees that the binary people
download is the one this repository built.

## What is signed

One artifact: `Mullion.exe`, a self-contained single-file Windows executable
(win-x64). Every release also publishes `Mullion.exe.sha256` beside it.

There is no installer, no MSI and no MSIX. Installing Mullion is copying one
file.

## Where builds come from

**Releases are built by GitHub Actions, never on a maintainer's machine.**

The workflow is [`.github/workflows/release.yml`](../.github/workflows/release.yml),
it is public, and its run logs are public. It triggers only on a pushed tag
matching `v*`, and it:

1. Checks out the tagged commit.
2. **Refuses to continue if the tag disagrees with `VersionPrefix` in
   `Directory.Build.props`.** A mismatch would publish one version as a binary
   reporting another, so it fails the build rather than shipping it.
3. Runs the full test suite. `tools/Publish.ps1` will not produce an executable
   if any test fails.
4. Publishes the single-file executable.
5. Computes and attaches the SHA256.
6. Creates the GitHub release **as a draft**.

No step of that is skippable, and nothing in it can be run differently by a
person. The version number is stamped from the repository, and the commit the
build came from is embedded in the executable's `InformationalVersion`, so any
binary can be traced to the exact source it was built from.

## Who approves a release

Mullion has one maintainer ([@levinium](https://github.com/levinium)), who holds
the author, reviewer and approver roles.

Every release therefore involves two deliberate, separate human actions:

1. **Pushing the version tag**, which is what starts the build. Tags are pushed
   by hand; no automation creates one.
2. **Publishing the draft release**, which is what makes the artifacts
   downloadable. The workflow deliberately stops at a draft so the built
   artifacts are reviewed before anyone can get them.

**No release is signed or published automatically.** Signing requests are
approved individually and manually, per release. There is no configuration in
which a commit, a merge or a scheduled job results in a signed binary.

## Account security

- Repository and signing accounts are protected with multi-factor
  authentication.
- Release artifacts are produced only by the workflow above, from a public
  runner, using the repository's own `GITHUB_TOKEN` scoped to `contents: write`.
- No long-lived signing material exists on any developer machine. Signing is
  performed by the signing service against an artifact built in CI.

## Metadata

Every built binary carries consistent, enforced metadata, set centrally in
`Directory.Build.props` rather than per-project:

| Attribute | Value |
|---|---|
| Product | Mullion |
| Company | Mullion |
| Version | `VersionPrefix`, which the release workflow checks against the tag |
| Informational version | version plus the short commit it was built from |
| Copyright | Copyright (c) 2026 levinium |
| Repository | https://github.com/levinium/Mullion |

## How a signed release would be verified downstream

Mullion can update itself, and the updater already enforces the rule that makes
signing meaningful:

> If the **running** executable carries a valid Authenticode signature, a
> replacement is accepted only if it carries a valid signature from the **same
> publisher**.

The check is implemented in
[`src/Mullion.Platform.Windows/Windows/Authenticode.cs`](../src/Mullion.Platform.Windows/Windows/Authenticode.cs)
and applied in `UpdateInstaller.StageAsync`. It verifies the signature with
`WinVerifyTrust` *before* reading the certificate out of the file, because
reading the certificate alone would happily return a valid one for a binary
someone had since edited.

While the binary is unsigned this rule is inert, by design: demanding a signature
the project does not yet produce would break its own updates. From the first
signed release onward it applies automatically, and a signed installation can
never be downgraded to an unsigned file.

## Reporting a problem

See [SECURITY.md](../SECURITY.md).
