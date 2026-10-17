# One-time Wi-Fi card tweaks for gaming, with a backup and a way back.
#   Apply:   powershell -ExecutionPolicy Bypass -File wifi-tweaks.ps1
#   Revert:  powershell -ExecutionPolicy Bypass -File wifi-tweaks.ps1 -Revert
# Needs admin (changing an adapter setting restarts it: Wi-Fi drops for a few seconds and reconnects).
param([switch]$Revert, [string]$Adapter = 'Wi-Fi')

$ErrorActionPreference = 'Stop'
$backupDir = Join-Path $PSScriptRoot '..\backups'
New-Item -ItemType Directory -Force $backupDir | Out-Null
$log = Join-Path $backupDir 'wifi-tweaks.log'
function Log($m) { "$(Get-Date -Format s)  $m" | Tee-Object -FilePath $log -Append }

# Only settings with a clear reason. Roaming: at home with one router there is nothing to roam to,
# and each roaming scan can cause a ping spike.
$tweaks = @{ 'RoamAggressiveness' = '0' }   # 0 = "1. Lowest"

try {
  if ($Revert) {
    $latest = Get-ChildItem $backupDir -Filter 'wifi-before-*.json' | Sort-Object Name | Select-Object -Last 1
    if (-not $latest) { throw 'no backup found' }
    $saved = Get-Content $latest.FullName -Raw | ConvertFrom-Json
    foreach ($s in $saved) {
      if ($tweaks.ContainsKey($s.RegistryKeyword)) {
        Set-NetAdapterAdvancedProperty -Name $Adapter -RegistryKeyword $s.RegistryKeyword -RegistryValue $s.RegistryValue -NoRestart
        Log "reverted $($s.RegistryKeyword) -> $($s.RegistryValue) ($($s.DisplayValue))"
      }
    }
  } else {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    Get-NetAdapterAdvancedProperty -Name $Adapter |
      Select-Object DisplayName, DisplayValue, RegistryKeyword, @{ n = 'RegistryValue'; e = { $_.RegistryValue[0] } } |
      ConvertTo-Json | Set-Content (Join-Path $backupDir "wifi-before-$stamp.json") -Encoding UTF8
    Log "backup saved: wifi-before-$stamp.json"
    foreach ($k in $tweaks.Keys) {
      $cur = Get-NetAdapterAdvancedProperty -Name $Adapter -RegistryKeyword $k
      if ($cur.RegistryValue[0] -eq $tweaks[$k]) { Log "$k already $($cur.DisplayValue)"; continue }
      Set-NetAdapterAdvancedProperty -Name $Adapter -RegistryKeyword $k -RegistryValue $tweaks[$k] -NoRestart
      Log "$k : $($cur.DisplayValue) -> value $($tweaks[$k])"
    }
  }

  # One restart for all changes, then wait until Wi-Fi is back.
  Restart-NetAdapter -Name $Adapter
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    if ((Get-NetAdapter -Name $Adapter).Status -eq 'Up' -and (Test-Connection 1.1.1.1 -Count 1 -Quiet)) { Log "Wi-Fi back after $i s"; break }
  }
  Get-NetAdapterAdvancedProperty -Name $Adapter -RegistryKeyword @($tweaks.Keys) | ForEach-Object { Log "now: $($_.DisplayName) = $($_.DisplayValue)" }
} catch {
  Log "ERROR: $($_.Exception.Message)"
  exit 1
}
