param(
    [string]$Extension = "",
    [switch]$Clean = $false,
    [switch]$RestoreOnly = $false,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$TargetFramework = "net10.0"
$ExtensionsDir = Join-Path $RepoRoot "Extensions"
$OutDir = Join-Path $RepoRoot "Server" "bin" $Configuration $TargetFramework "Extensions"

# Extension build order (lower dependency first)
$ExtensionOrder = @("Chat", "Trade")

function Restore-Extensions {
    foreach ($Name in $ExtensionOrder) {
        $Proj = Join-Path $ExtensionsDir $Name "Server" "${Name}Extension.Server.csproj"
        if (Test-Path $Proj) {
            Write-Host "[RESTORE] $Name..." -ForegroundColor Gray
            dotnet restore $Proj | Out-Null
        }
    }
}

function Build-Extension($Name) {
    $Proj = Join-Path $ExtensionsDir $Name "Server" "${Name}Extension.Server.csproj"
    $ContractsProj = Join-Path $ExtensionsDir $Name "Contracts" "${Name}Extension.csproj"

    if (-not (Test-Path $Proj)) {
        Write-Error "Extension '$Name' project not found: $Proj"
        return
    }

    Write-Host "[BUILD] $Name (Server)..." -ForegroundColor Cyan
    dotnet build $Proj `
        -c $Configuration `
        --no-restore `
        /p:TargetFramework="$TargetFramework"
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $Name" }

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

    # Copy Server extension DLL
    $DllSrc = Join-Path $ExtensionsDir $Name "Server" "bin" $Configuration $TargetFramework "${Name}Extension.Server.dll"
    Copy-Item $DllSrc $OutDir -Force

    # Copy Contracts DLL (required at runtime for protobuf types etc.)
    if (Test-Path $ContractsProj) {
        $ContractsDllSrc = Join-Path $ExtensionsDir $Name "Contracts" "bin" $Configuration $TargetFramework "${Name}Extension.dll"
        if (Test-Path $ContractsDllSrc) {
            Copy-Item $ContractsDllSrc $OutDir -Force
        }
    }

    Write-Host "[OK] $Name -> $OutDir/" -ForegroundColor Green
}

# --- main ---
Set-Location $RepoRoot

if ($Clean) {
    Write-Host "[CLEAN] Removing extension outputs..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $OutDir -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Write-Host "[OK] Cleaned." -ForegroundColor Green
    return
}

if ($RestoreOnly) {
    Restore-Extensions
    return
}

Restore-Extensions

if ($Extension) {
    Build-Extension $Extension
} else {
    foreach ($Name in $ExtensionOrder) {
        Build-Extension $Name
    }
}

Write-Host ""
Write-Host "=== All extensions deployed ===" -ForegroundColor Green
