# Minimal MSI table readout via WindowsInstaller COM (plain SELECTs; MSI SQL's
# IN clause is finicky). Confirms the PATH append + HKLM\SOFTWARE\RevitAgent marker.
param([string]$Msi = "revit-agent.msi")

$msiPath = (Resolve-Path $Msi).Path
$wi = New-Object -ComObject WindowsInstaller.Installer
$db = $wi.OpenDatabase($msiPath, 0)

function Dump($sql, $cols, $label) {
  Write-Output "--- $label ---"
  try {
    $view = $db.OpenView($sql); $view.Execute()
    while ($r = $view.Fetch()) {
      $vals = @()
      for ($i = 1; $i -le $cols; $i++) { $vals += $r.StringData[$i] }
      Write-Output ("  " + ($vals -join " | "))
    }
    $view.Close()
  } catch { Write-Output ("  (read failed: {0})" -f $_.Exception.Message) }
}

Dump "SELECT Property, Value FROM Property" 2 "Property"
Dump "SELECT Environment, Name, Value, Component_ FROM Environment" 4 "Environment"
Dump "SELECT Registry, Root, Key, Name, Value, Component_ FROM Registry" 6 "Registry"

Write-Output "--- File count ---"
$view = $db.OpenView("SELECT COUNT(*) FROM File"); $view.Execute(); $r = $view.Fetch()
Write-Output ("  files = {0}" -f $r.StringData[1]); $view.Close()
