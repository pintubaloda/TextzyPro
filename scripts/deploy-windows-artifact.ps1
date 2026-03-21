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

function Get-AppCmdPath {
    $path = Join-Path $env:windir "System32\\inetsrv\\appcmd.exe"
    if (Test-Path $path) { return $path }
    return $null
}

function Assert-PathExists {
    param([string]$PathValue, [string]$Label)
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "$Label is not configured."
    }
    if (-not (Test-Path $PathValue)) {
        New-Item -ItemType Directory -Path $PathValue -Force | Out-Null
    }
}

function Invoke-Robocopy {
    param(
        [string]$Source,
        [string]$Target,
        [string[]]$AdditionalArgs = @()
    )

    if (-not (Test-Path $Source)) {
        throw "Artifact path not found: $Source"
    }

    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    $robocopyArgs = @(
        $Source,
        $Target,
        "/R:2",
        "/W:2",
        "/NFL",
        "/NDL",
        "/NP"
    )
    $robocopyArgs += $AdditionalArgs

    & robocopy @robocopyArgs | Out-Null
    $exitCode = $LASTEXITCODE
    if ($exitCode -ge 8) {
        throw "Robocopy failed for $Source -> $Target with exit code $exitCode"
    }

    $global:LASTEXITCODE = 0
}

function Copy-ArtifactToRelease {
    param(
        [string]$Source,
        [string]$Target
    )

    Invoke-Robocopy -Source $Source -Target $Target -AdditionalArgs @("/MIR")
}

function Deploy-Artifact {
    param(
        [string]$Source,
        [string]$Target,
        [string[]]$ExcludedFiles = @()
    )

    # Copy new and changed files, but do not delete server-only files.
    $args = @("/E")
    if ($ExcludedFiles -and $ExcludedFiles.Count -gt 0) {
        $args += "/XF"
        $args += $ExcludedFiles
    }
    Invoke-Robocopy -Source $Source -Target $Target -AdditionalArgs $args
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

    $appcmd = Get-AppCmdPath
    if ($appcmd) {
        if (-not [string]::IsNullOrWhiteSpace($SiteName)) {
            & $appcmd stop site /site.name:"$SiteName" 2>$null | Out-Null
        }
        if (-not [string]::IsNullOrWhiteSpace($AppPool)) {
            & $appcmd stop apppool /apppool.name:"$AppPool" 2>$null | Out-Null
        }
        return
    }

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

    $appcmd = Get-AppCmdPath
    if ($appcmd) {
        if (-not [string]::IsNullOrWhiteSpace($SiteName)) {
            & $appcmd start site /site.name:"$SiteName" 2>$null | Out-Null
        }
        if (-not [string]::IsNullOrWhiteSpace($AppPool)) {
            & $appcmd start apppool /apppool.name:"$AppPool" 2>$null | Out-Null
        }
        return
    }

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
$protectedDeployFiles = @(
    "web.config",
    "*cors*.json",
    "*CORS*.json"
)

New-Item -ItemType Directory -Path $frontendReleasePath -Force | Out-Null
New-Item -ItemType Directory -Path $backendReleasePath -Force | Out-Null

Copy-ArtifactToRelease -Source $FrontendArtifactPath -Target $frontendReleasePath
Copy-ArtifactToRelease -Source $BackendArtifactPath -Target $backendReleasePath

$backendDllName = "Textzy.Api.dll"
$backendReleaseDll = Join-Path $backendReleasePath $backendDllName
if (-not (Test-Path $backendReleaseDll)) {
    throw "Backend artifact missing $backendDllName at $backendReleaseDll"
}
$backendReleaseStamp = (Get-Item $backendReleaseDll).LastWriteTimeUtc

$offlineFile = Write-AppOffline -Target $BackendTargetPath
try {
    Deploy-Artifact -Source $frontendReleasePath -Target $FrontendTargetPath -ExcludedFiles $protectedDeployFiles
    Write-FrontendRuntimeConfig `
        -TargetPath $FrontendTargetPath `
        -ApiBase $FrontendApiBase `
        -FacebookAppId $FrontendFacebookAppId `
        -WabaEmbeddedConfigId $FrontendWabaEmbeddedConfigId
    Stop-IisTarget -SiteName $BackendSiteName -AppPool $BackendAppPool
    Start-Sleep -Seconds 5
    Deploy-Artifact -Source $backendReleasePath -Target $BackendTargetPath -ExcludedFiles $protectedDeployFiles

    $backendTargetDll = Join-Path $BackendTargetPath $backendDllName
    if (-not (Test-Path $backendTargetDll)) {
        throw "Backend target missing $backendDllName after deploy at $backendTargetDll"
    }
    $backendTargetStamp = (Get-Item $backendTargetDll).LastWriteTimeUtc
    if ($backendTargetStamp -lt $backendReleaseStamp) {
        throw "Backend deploy did not update $backendDllName. target=$backendTargetStamp artifact=$backendReleaseStamp"
    }
}
finally {
    if (Test-Path $offlineFile) {
        Remove-Item $offlineFile -Force -ErrorAction SilentlyContinue
    }
    Start-IisTarget -SiteName $BackendSiteName -AppPool $BackendAppPool
}

Restart-IisTarget -SiteName $FrontendSiteName -AppPool $FrontendAppPool

Write-Host "Frontend runtime config written to $(Join-Path $FrontendTargetPath 'env.js')."
Write-Host "Protected deploy files (not overwritten): $($protectedDeployFiles -join ', ')"
Write-Host "Textzy deployment completed."
$global:LASTEXITCODE = 0
