[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("ioc", "ioc-cli")]
    [string]$ProjectKey
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$config = @{
    "ioc" = @{
        Project = "src/Ioc/src/SourceGen.Ioc/SourceGen.Ioc.csproj"
        VersionFile = "src/Ioc/src/SourceGen.Ioc/version.json"
    }
    "ioc-cli" = @{
        Project = "src/Ioc/src/SourceGen.Ioc.Cli/SourceGen.Ioc.Cli.csproj"
        VersionFile = "src/Ioc/src/SourceGen.Ioc.Cli/version.json"
    }
}

$c = $config[$ProjectKey]
if (-not $c)
{
    throw "Unknown project '$ProjectKey'. Use 'ioc' or 'ioc-cli'."
}

Write-Host "Preparing next iteration for $ProjectKey"
nbgv prepare-release --project $c.Project

git add $c.VersionFile
git commit -m "chore($ProjectKey): prepare next iteration"
git push origin main
