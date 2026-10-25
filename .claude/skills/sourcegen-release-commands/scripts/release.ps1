[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("ioc", "ioc-cli")]
    [string]$ProjectKey,

    [Parameter(Mandatory = $true)]
    [ValidateSet("prerelease", "release")]
    [string]$ReleaseType,

    [Parameter(Mandatory = $true)]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Native
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    & $Command

    if ($LASTEXITCODE -ne 0)
    {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

$config = @{
    "ioc" = @{
        Project = "src/Ioc/src/SourceGen.Ioc/SourceGen.Ioc.csproj"
        VersionFile = "src/Ioc/src/SourceGen.Ioc/version.json"
        TestProject = "src/Ioc/test/SourceGen.Ioc.Test/SourceGen.Ioc.Test.csproj"
        TagPrefix = "ioc-v"
        Workflow = "ioc.publish.yml"
    }
    "ioc-cli" = @{
        Project = "src/Ioc/src/SourceGen.Ioc.Cli/SourceGen.Ioc.Cli.csproj"
        VersionFile = "src/Ioc/src/SourceGen.Ioc.Cli/version.json"
        TestProject = "src/Ioc/test/SourceGen.Ioc.Cli.Test/SourceGen.Ioc.Cli.Test.csproj"
        TagPrefix = "ioc-cli-v"
        Workflow = "ioc.cli.publish.yml"
    }
}

$c = $config[$ProjectKey]
if (-not $c)
{
    throw "Unknown project '$ProjectKey'. Use 'ioc' or 'ioc-cli'."
}

if ($ReleaseType -eq "release" -and $Version -match "-")
{
    throw "Stable release version must not contain a prerelease label."
}

if ($ReleaseType -eq "prerelease" -and $Version -notmatch "-")
{
    Write-Warning "Prerelease usually includes a suffix like -alpha, -beta, or -rc."
}

$tag = "$($c.TagPrefix)$Version"

Write-Host "Setting version to $Version for $ProjectKey"
Invoke-Native -Description "nbgv set-version" -Command { nbgv set-version $Version --project $c.Project }

$versionFile = Get-Content -Raw $c.VersionFile | ConvertFrom-Json
if ($versionFile.version -ne $Version)
{
    throw "Version file '$($c.VersionFile)' expected '$Version' but found '$($versionFile.version)'."
}

Write-Host "Version file verified: $($versionFile.version)"

Write-Host "Running tests"
Invoke-Native -Description "dotnet run tests" -Command {
    dotnet run --project $c.TestProject -c Release -- --treenode-filter "/*/*/*/*"
}

Write-Host "Committing version file"
Invoke-Native -Description "git add version file" -Command { git add $c.VersionFile }
Invoke-Native -Description "git commit" -Command { git commit -m "chore($ProjectKey): set version to $Version" }

Write-Host "Creating tag $tag"
Invoke-Native -Description "git tag" -Command { git tag -a $tag -m "Release $ProjectKey v$Version" }

Write-Host "Pushing commit and tag"
Invoke-Native -Description "git push main" -Command { git push origin main }
Invoke-Native -Description "git push tag" -Command { git push origin $tag }

Write-Host "Publish workflow triggered by tag $tag ($($c.Workflow))."
