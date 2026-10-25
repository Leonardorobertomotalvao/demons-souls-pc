# make_launcher_release.ps1 — reproduce the Windows launcher package locally.
# Creates: release/<zip name>  with  Launcher\ (self-contained publish) and,
# OPTIONALY, Game\des_boot.exe — but ONLY when the hash matches an approved
# runtime identity. Game-derived material is otherwise never packaged and the
# runtime is never committed to the repository.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\make_launcher_release.ps1
#   powershell ... -File scripts\make_launcher_release.ps1 -ApprovedRuntime "E:\path\des_boot.exe"
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$StagingRoot = "release",
    [string]$ApprovedRuntime = ""
)
$ErrorActionPreference = "Stop"
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)   # repo root

$project = "launcher-alpha02-hotfix\DemonsSouls.Launcher.csproj"
if (!(Test-Path $project)) { throw "Projeto do launcher não encontrado: $project" }

$staging = Join-Path $StagingRoot "staging"
$launcherDir = Join-Path $staging "Launcher"
$tempBuild = Join-Path $StagingRoot "publish"
Remove-Item -Recurse -Force $staging,$tempBuild -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $launcherDir,$tempBuild | Out-Null

Write-Host "• dotnet publish (self-contained $RuntimeIdentifier)…"
dotnet publish $project -c $Configuration -r $RuntimeIdentifier --self-contained true -o $tempBuild
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou ($LASTEXITCODE)." }
Copy-Item -Path (Join-Path $tempBuild "*") -Destination $launcherDir -Recurse

Write-Host "• documentação do pacote…"
Copy-Item README.md -Destination $staging -Force
Copy-Item docs\03-setup.md -Destination (New-Item -ItemType Directory -Force -Path (Join-Path $staging "docs")).FullName -Force

# aprovado: do arquivo do usuário ou de onde o mantenedor indicar (nunca do repositório)
$runtimeNote = "Executável nativo não incluído (política: só entra no pacote uma cópia com identidade aprovada confirmada aqui)."
if ($ApprovedRuntime -ne "") {
    $approveSha = (Get-FileHash -Algorithm SHA256 $ApprovedRuntime).Hash.ToLower()
    $approved = @(
        "88087725d7cb858b644d8d183329eb72e1c820b365ed2b34ebd6e2ac281f0933",
        "83b23813254c542575244f394ed31a3be2bbd276e6a1d70372d2c6c12a77ae7e"
    )
    if ($approved -contains $approveSha) {
        New-Item -ItemType Directory -Force -Path (Join-Path $staging "Game") | Out-Null
        Copy-Item $ApprovedRuntime -Destination (Join-Path $staging "Game\des_boot.exe")
        $runtimeNote = "Executável nativo incluído em Game\des_boot.exe (identidade aprovada $approveSha)."
    } else {
        Write-Warning "SHA-256 de -ApprovedRuntime não é uma identidade aprovada; o pacote fica sem runtime."
    }
}
Write-Host $runtimeNote

$releaseInfo = (Get-Item (Join-Path $tempBuild "DemonsSouls.Launcher.exe")).VersionInfo
$version = ($releaseInfo.ProductVersion -split '\+')[0]
foreach ($pattern in @("*.iso","*.elf","*.self","*.bin","*.BIN","*.pkg","*.rap","*_keys*")) { Remove-Item (Join-Path $staging $pattern) -ErrorAction SilentlyContinue }
$zip = Join-Path $StagingRoot "DemonSoulsPC-Launcher-Windows-x64-$version.zip"
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zip -Force
$zipSha = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLower()
Write-Host "PACOTE OK: $zip ($zipSha)"
Write-Host "Versão: $($releaseInfo.ProductVersion) | Launcher dir: $launcherDir"
