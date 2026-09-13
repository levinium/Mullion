<#
.SYNOPSIS
    Builds Mullion as one self-contained executable.

.DESCRIPTION
    Produces a single .exe with the runtime inside it, so installing Mullion is
    copying one file. Nothing else has to be present on the machine and nothing
    has to be kept in step with it.

    The tests are run first and the publish is abandoned if any fail. A hotkey
    tool that is broken is worse than one that is absent: it swallows keystrokes
    on their way to whatever the user actually wanted.

.EXAMPLE
    .\tools\Publish.ps1
    .\tools\Publish.ps1 -OutputDirectory D:\Apps\Mullion -SkipTests
#>
[CmdletBinding()]
param(
    # Where the executable lands. Defaults to publish/ off the repo root.
    [string] $OutputDirectory,

    # Publish without running the tests first.
    [switch] $SkipTests,

    # Where "Support Mullion" sends people. Left unset - as it is for every
    # build from a clean checkout - the app has no donate button at all, so a
    # fork cannot ship one asking on someone else's behalf.
    #
    # An {amount} placeholder turns the ask into a picker, and is only worth
    # including where the destination actually reads the amount out of the URL.
    [string] $SponsorUrl,

    # Where the app looks for a newer release. Unlike SponsorUrl this HAS a
    # default - the project's own releases - because the two fail in opposite
    # directions: a donate link left in by accident asks strangers for money,
    # while an update feed left out is simply never mentioned again. Pass this
    # to point a fork at its own releases, or "" to build with no check at all.
    [string] $UpdateFeedUrl,

    # The page a person is sent to when an update is offered. Only meaningful
    # alongside -UpdateFeedUrl; it defaults to match.
    [string] $UpdatePageUrl
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'publish' }

if (-not $SkipTests) {
    Write-Host 'Running tests...'
    dotnet test (Join-Path $root 'Mullion.slnx') --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; not publishing.' }
}

Write-Host 'Publishing...'

# [string[]] and the leading comma are both load-bearing. An `if` used as an
# expression yields its branch's OUTPUT, and PowerShell enumerates a one-element
# array down to a bare string on the way out - after which `@sponsor` splats a
# string, which it does one character at a time. The publish then failed with
# "Unknown switch" and a command line reading "- p : S p o n s o r U r l = ...".
[string[]] $sponsor = if ($SponsorUrl) { , "-p:SponsorUrl=$SponsorUrl" } else { @() }

if ($SponsorUrl) {
    Write-Host "  Support button enabled: $SponsorUrl"
} else {
    Write-Host '  No support button (pass -SponsorUrl to include one).'
}

# PSBoundParameters rather than truthiness, because "" is a MEANING here - it
# is how a fork asks for a build that never checks for updates - and a plain
# `if ($UpdateFeedUrl)` cannot tell that apart from not passing it at all.
[string[]] $updates = @()

if ($PSBoundParameters.ContainsKey('UpdateFeedUrl')) {
    $updates += "-p:UpdateFeedUrl=$UpdateFeedUrl"

    # Kept in step unless told otherwise: a build that checks one repository's
    # releases and then sends people to another's is worse than not checking.
    $page = if ($PSBoundParameters.ContainsKey('UpdatePageUrl')) { $UpdatePageUrl } else { $UpdateFeedUrl }
    $updates += "-p:UpdatePageUrl=$page"

    if ($UpdateFeedUrl) {
        Write-Host "  Update feed: $UpdateFeedUrl"
    } else {
        Write-Host '  Update check disabled for this build.'
    }
} else {
    Write-Host '  Update feed: the project default (pass -UpdateFeedUrl to change it).'
}

dotnet publish (Join-Path $root 'src\Mullion.App\Mullion.App.csproj') `
    -c Release -r win-x64 --self-contained true `
    -o $OutputDirectory --nologo -v q @sponsor @updates

if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Join-Path $OutputDirectory 'Mullion.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe but it is not there." }

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)

# Anything beside the exe defeats the point, so say what is there rather than
# letting a quiet folder of leftovers look like success.
$strays = Get-ChildItem $OutputDirectory -File |
    Where-Object { $_.Name -ne 'Mullion.exe' } |
    Select-Object -ExpandProperty Name

Write-Host ''
Write-Host "  $exe  ($size MB)"

if ($strays) {
    Write-Host ''
    Write-Host '  Also written (the exe does not need these to run):'
    $strays | ForEach-Object { Write-Host "    $_" }
}

Write-Host ''
Write-Host '  Run it once, then turn on "Start Mullion when I sign in" in Settings.'
