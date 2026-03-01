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
nbgv set-version $Version --project $c.Project
nbgv get-version --project $c.Project

Write-Host "Running tests"
dotnet run --project $c.TestProject -c Release -- --treenode-filter "/*/*/*/*"

Write-Host "Committing version file"
git add $c.VersionFile
git commit -m "chore($ProjectKey): set version to $Version"

Write-Host "Creating tag $tag"
git tag -a $tag -m "Release $ProjectKey v$Version"

Write-Host "Pushing commit and tag"
git push origin main
git push origin $tag

Write-Host "Publish workflow triggered by tag $tag ($($c.Workflow))."
