# Audio A/B diagnostic runner for the Demon's Souls PC runtime.
#
# Replays the latest launch.json produced by the launcher inside a fresh,
# isolated directory, toggles the audio-notify fix knobs via environment
# (never writing to managed profiles), and terminates the process
# automatically after <Seconds> so runs are comparable and unattended.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\audio-ab-diagnostic.ps1 `
#     -RuntimePath <absolute path to des_boot.exe> [-FixOff] [-Stages] `
#     [-Seconds 110] [-Tag fixon]
#
# The runtime prints its audio counters when these flags are set:
#   AUDIO_PEAK/AUDIO_RATE/AUDIO_GAPS/AUDIO_IO_DIAG  — always set by this script
#   AUDIO_STAGES/AUDIO_FLOW                          — set with -Stages
#   DES_AUDIO_NOTIFY_FIX                             — 0 with -FixOff, else 1
#
# Read the [audio-notify-fix], [audio-rate], [audio-peak], [audio-ring],
# [audio-notify] and [audio-stage] lines from the run's stderr.log.
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$RuntimePath,
  [switch]$FixOff,
  [switch]$Stages,
  [int]$Seconds = 110,
  [string]$Tag = 'ab'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $RuntimePath -PathType Leaf)) { throw "Runtime não encontrado: $RuntimePath" }
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$logRoot = Join-Path $repoRoot 'logs'
$launchFile = Get-ChildItem -LiteralPath $logRoot -Filter 'launch.json' -File -Recurse |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $launchFile) { throw "Nenhum launch.json em $logRoot — execute o launcher uma vez antes de diagnosticar." }
$j = Get-Content -LiteralPath $launchFile.FullName -Raw | ConvertFrom-Json
foreach ($name in @('elf','cwd','session','settings')) {
  if (-not $j.PSObject.Properties[$name]) { throw "Campo '$name' ausente em $($launchFile.FullName)" }
}

$diagRoot = Join-Path $repoRoot 'AudioDiagnostics'
$diag = Join-Path $diagRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Tag)
New-Item -ItemType Directory -Path $diag -Force | Out-Null
Copy-Item -LiteralPath ([string]$j.settings.DES_SETTINGS) -Destination (Join-Path $diag 'settings.ini')
foreach ($dir in @('logs','gamedata\dev_hdd0','shader_cache','interface','appdata\roaming','appdata\local','temp','capturas','perfil')) {
  New-Item -ItemType Directory -Path (Join-Path $diag $dir) -Force | Out-Null
}

$si = New-Object System.Diagnostics.ProcessStartInfo
$si.FileName = [IO.Path]::GetFullPath($RuntimePath)
$si.WorkingDirectory = [string]$j.cwd
$si.Arguments = '"' + [string]$j.elf + '"'
$si.UseShellExecute = $false
$si.RedirectStandardOutput = $true
$si.RedirectStandardError = $true
# Strip launcher-controlled variables, then reapply only this run's settings,
# redirecting profile-managed paths into the isolated diagnostic directory.
$strip = '^(PAD_|DES_|PS3_|RSX_|SPU_|SPURS_|PPU_|CELLMARK_|CELLGCM_|AUDIO_|FS_|LIBSRE_|GCM|VBLANK|HLE_|SEM_|LBP_|CONST|TEX|FIFO_|PROF_|YZ_|FP_|VP_|RTT_)'
foreach ($key in @($si.EnvironmentVariables.Keys)) {
  if ($key -match $strip) { $si.EnvironmentVariables.Remove($key) }
}
$prefix = ([string]$j.session).TrimEnd('\') + '\'
foreach ($prop in $j.settings.PSObject.Properties) {
  $value = [string]$prop.Value
  if ($value.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    $value = Join-Path $diag ($value.Substring($prefix.Length))
  }
  $si.EnvironmentVariables[$prop.Name] = $value
}
$si.EnvironmentVariables['PS3_HDD0_ROOT'] = Join-Path $diag 'gamedata\dev_hdd0'
$si.EnvironmentVariables['PS3_SHADER_CACHE'] = Join-Path $diag 'shader_cache'
$si.EnvironmentVariables['DES_DATA_DIR'] = Join-Path $diag 'interface'
$si.EnvironmentVariables['DES_SETTINGS'] = Join-Path $diag 'settings.ini'
$si.EnvironmentVariables['APPDATA'] = Join-Path $diag 'appdata\roaming'
$si.EnvironmentVariables['LOCALAPPDATA'] = Join-Path $diag 'appdata\local'
$si.EnvironmentVariables['TEMP'] = Join-Path $diag 'temp'
$si.EnvironmentVariables['TMP'] = Join-Path $diag 'temp'
$si.EnvironmentVariables['AUDIO_PEAK'] = '1'
$si.EnvironmentVariables['AUDIO_RATE'] = '1'
$si.EnvironmentVariables['AUDIO_GAPS'] = '1'
$si.EnvironmentVariables['AUDIO_IO_DIAG'] = '1'
if ($Stages) { $si.EnvironmentVariables['AUDIO_STAGES'] = '1'; $si.EnvironmentVariables['AUDIO_FLOW'] = '1' }
$si.EnvironmentVariables['DES_AUDIO_NOTIFY_FIX'] = if ($FixOff) { '0' } else { '1' }

$snapshot = [ordered]@{
  sourceLaunchJson = $launchFile.FullName
  runtime = $si.FileName
  runtimeSha256 = (Get-FileHash -LiteralPath $RuntimePath -Algorithm SHA256).Hash
  eboot = [string]$j.elf
  fix = if ($FixOff) { 'DES_AUDIO_NOTIFY_FIX=0' } else { 'DES_AUDIO_NOTIFY_FIX=1' }
  stages = [bool]$Stages
  plannedSeconds = $Seconds
  at = (Get-Date -Format o)
}
$snapshot | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $diag 'diagnostic-launch.json') -Encoding UTF8

$proc = [System.Diagnostics.Process]::Start($si)
if (-not $proc) { throw "Process.Start falhou: $($si.FileName)" }
Write-Host "PID=$($proc.Id) FIX=$(if ($FixOff) {'OFF'} else {'ON'}) DIAG=$diag"
$outTask = $proc.StandardOutput.ReadToEndAsync()
$errTask = $proc.StandardError.ReadToEndAsync()
$waited = 0
while (-not $proc.WaitForExit(1000) -and $waited -lt $Seconds) { $waited++ }
if ($waited -ge $Seconds) {
  try { $proc.Kill(); $proc.WaitForExit(10000) } catch {}
  Write-Host "Encerrado automaticamente após $waited s"
}
$outTask.Result | Set-Content -LiteralPath (Join-Path $diag 'stdout.log') -Encoding UTF8
$errTask.Result | Set-Content -LiteralPath (Join-Path $diag 'stderr.log') -Encoding UTF8
"exitCode=$($proc.ExitCode) stoppedAfter=${waited}s stoppedBy=$(if ($waited -ge $Seconds) {'kill'} else {'exit'})" |
  Set-Content -LiteralPath (Join-Path $diag 'process-exit.txt') -Encoding UTF8
Write-Host "LOGS=$diag — procure [audio-rate]/[audio-notify]/[audio-peak] no stderr.log"
