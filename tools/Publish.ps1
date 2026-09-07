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
    [switch] $SkipTests
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

dotnet publish (Join-Path $root 'src\Mullion.App\Mullion.App.csproj') `
    -c Release -r win-x64 --self-contained true `
    -o $OutputDirectory --nologo -v q

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
