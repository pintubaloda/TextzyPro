[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$FrontendArtifactPath,

    [Parameter(Mandatory = $true)]
    [string]$BackendArtifactPath,

    [Parameter(Mandatory = $true)]
    [string]$FrontendTargetPath,

    [Parameter(Mandatory = $true)]
    [string]$BackendTargetPath,

    [string]$FrontendSiteName,
    [string]$BackendSiteName,
    [string]$FrontendAppPool,
    [string]$BackendAppPool,
    [string]$FrontendApiBase = "https://api.textzy.in",
    [string]$FrontendFacebookAppId = "",
    [string]$FrontendWabaEmbeddedConfigId = "",
    [string]$ReleaseTag = ""
)

$ErrorActionPreference = "Stop"

function Assert-PathExists {
    param([string]$PathValue, [string]$Label)
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "$Label is not configured."
    }
    if (-not (Test-Path $PathValue)) {
        New-Item -ItemType Directory -Path $PathValue -Force | Out-Null
    }
}

function Copy-Artifact {
    param(
        [string]$Source,
        [string]$Target
    )

    if (-not (Test-Path $Source)) {
        throw "Artifact path not found: $Source"
    }

    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    $robocopyArgs = @(
        $Source,
        $Target,
        "/MIR",
        "/R:2",
        "/W:2",
        "/NFL",
        "/NDL",
        "/NP"
    )

    & robocopy @robocopyArgs | Out-Null
    $exitCode = $LASTEXITCODE
    if ($exitCode -ge 8) {
        throw "Robocopy failed for $Source -> $Target with exit code $exitCode"
    }
}

function Write-AppOffline {
    param([string]$Target)
    $offlinePath = Join-Path $Target "app_offline.htm"
    Set-Content -Path $offlinePath -Value "<html><body><h1>Updating Textzy backend</h1></body></html>" -Encoding UTF8
    return $offlinePath
}

function Stop-IisTarget {
    param(
        [string]$SiteName,
        [string]$AppPool
    )

    Import-Module WebAdministration -ErrorAction SilentlyContinue

    if (-not [string]::IsNullOrWhiteSpace($SiteName)) {
        if (Test-Path "IIS:\Sites\$SiteName") {
            Stop-Website -Name $SiteName
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($AppPool)) {
        if (Test-Path "IIS:\AppPools\$AppPool") {
            Stop-WebAppPool -Name $AppPool
        }
    }
}

function Start-IisTarget {
    param(
        [string]$SiteName,
        [string]$AppPool
    )

    Import-Module WebAdministration -ErrorAction SilentlyContinue

    if (-not [string]::IsNullOrWhiteSpace($SiteName)) {
        if (Test-Path "IIS:\Sites\$SiteName") {
            Start-Website -Name $SiteName
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($AppPool)) {
        if (Test-Path "IIS:\AppPools\$AppPool") {
            Start-WebAppPool -Name $AppPool
        }
    }
}

function Restart-IisTarget {
    param(
        [string]$SiteName,
        [string]$AppPool
    )

    Stop-IisTarget -SiteName $SiteName -AppPool $AppPool
    Start-Sleep -Seconds 3
    Start-IisTarget -SiteName $SiteName -AppPool $AppPool
}

function Write-FrontendRuntimeConfig {
    param(
        [string]$TargetPath,
        [string]$ApiBase,
        [string]$FacebookAppId,
        [string]$WabaEmbeddedConfigId
    )

    $envFilePath = Join-Path $TargetPath "env.js"
    $content = @"
window.__APP_CONFIG__ = window.__APP_CONFIG__ || {
  API_BASE: "$ApiBase",
  FACEBOOK_APP_ID: "$FacebookAppId",
  WABA_EMBEDDED_CONFIG_ID: "$WabaEmbeddedConfigId"
};
"@

    Set-Content -Path $envFilePath -Value $content -Encoding UTF8
}

Assert-PathExists -PathValue $FrontendTargetPath -Label "Frontend target path"
Assert-PathExists -PathValue $BackendTargetPath -Label "Backend target path"

$releaseRoot = Join-Path ([System.IO.Path]::GetDirectoryName($FrontendTargetPath)) "releases"
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

$releaseName = if ([string]::IsNullOrWhiteSpace($ReleaseTag)) { Get-Date -Format "yyyyMMddHHmmss" } else { $ReleaseTag.Replace(":", "-") }
$frontendReleasePath = Join-Path $releaseRoot "frontend-$releaseName"
$backendReleasePath = Join-Path $releaseRoot "backend-$releaseName"

New-Item -ItemType Directory -Path $frontendReleasePath -Force | Out-Null
New-Item -ItemType Directory -Path $backendReleasePath -Force | Out-Null

Copy-Artifact -Source $FrontendArtifactPath -Target $frontendReleasePath
Copy-Artifact -Source $BackendArtifactPath -Target $backendReleasePath

$offlineFile = Write-AppOffline -Target $BackendTargetPath
try {
    Copy-Artifact -Source $frontendReleasePath -Target $FrontendTargetPath
    Write-FrontendRuntimeConfig `
        -TargetPath $FrontendTargetPath `
        -ApiBase $FrontendApiBase `
        -FacebookAppId $FrontendFacebookAppId `
        -WabaEmbeddedConfigId $FrontendWabaEmbeddedConfigId
    Stop-IisTarget -SiteName $BackendSiteName -AppPool $BackendAppPool
    Start-Sleep -Seconds 5
    Copy-Artifact -Source $backendReleasePath -Target $BackendTargetPath
}
finally {
    if (Test-Path $offlineFile) {
        Remove-Item $offlineFile -Force -ErrorAction SilentlyContinue
    }
    Start-IisTarget -SiteName $BackendSiteName -AppPool $BackendAppPool
}

Restart-IisTarget -SiteName $FrontendSiteName -AppPool $FrontendAppPool

Write-Host "Frontend runtime config written to $(Join-Path $FrontendTargetPath 'env.js')."
Write-Host "Textzy deployment completed."
