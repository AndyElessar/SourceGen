[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("ioc", "ioc-cli")]
    [string]$ProjectKey,

    [string]$Tag,

    [switch]$PublishDraft
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command gh -ErrorAction SilentlyContinue))
{
    throw "GitHub CLI (gh) is required for monitoring/publishing release status."
}

$workflow = switch ($ProjectKey)
{
    "ioc" { "ioc.publish.yml" }
    "ioc-cli" { "ioc.cli.publish.yml" }
    default { throw "Unknown project '$ProjectKey'. Use 'ioc' or 'ioc-cli'." }
}

Write-Host "Recent runs for $workflow"
gh run list --workflow $workflow --limit 5

if ($PublishDraft)
{
    if ([string]::IsNullOrWhiteSpace($Tag))
    {
        throw "When using -PublishDraft, provide -Tag."
    }

    Write-Host "Publishing draft release for tag $Tag"
    gh release edit $Tag --draft=false --repo AndyElessar/SourceGen
}
