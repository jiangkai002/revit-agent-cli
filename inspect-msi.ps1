# Admin-extracts the MSI (no admin/UAC needed — verifies the cabinet + file table
# extract every file intact) and reads the Property/Environment/Registry tables via
# the WindowsInstaller COM API to confirm the PATH append + HKLM marker are present.
param([string]$Msi = "revit-agent.msi", [string]$ExtractDir = "msi-admin-extract")

$msiPath = (Resolve-Path $Msi).Path
$target = Join-Path (Get-Location) $ExtractDir

Write-Output "=== admin-extract (msiexec /a) ==="
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
$ret = & "$env:SystemRoot\System32\msiexec.exe" /a "$msiPath" /qn "TARGETDIR=$target"
Write-Output ("msiexec /a exit code: {0}" -f $LASTEXITCODE)

$extracted = (Get-ChildItem $target -Recurse -File -ErrorAction SilentlyContinue).Count
Write-Output ("extracted files: {0} (expect ~318)" -f $extracted)
if (Test-Path (Join-Path $target "revit-agent.exe")) { Write-Output "revit-agent.exe: present" } else { Write-Output "revit-agent.exe: MISSING" }
Get-ChildItem $target -Directory | ForEach-Object { Write-Output ("  dir: {0} ({1} files)" -f $_.Name, (Get-ChildItem $_ -Recurse -File).Count) }

Write-Output ""
Write-Output "=== MSI table inspection (WindowsInstaller COM) ==="
$wi = New-Object -ComObject WindowsInstaller.Installer
$db = $wi.OpenDatabase($msiPath, 0)

function Read-Table($sql, $cols) {
  $view = $db.OpenView($sql); $view.Execute()
  while ($r = $view.Fetch()) {
    $vals = @()
    for ($i = 1; $i -le $cols; $i++) { $vals += $r.StringData[$i] }
    Write-Output ("  " + ($vals -join " | "))
  }
  $view.Close()
}

Write-Output "--- Property (key ones) ---"
$view = $db.OpenView("SELECT Property, Value FROM Property WHERE Property IN ('ProductName','ProductVersion','ProductCode','Manufacturer','UpgradeCode','ALLUSERS')")
$view.Execute()
while ($r = $view.Fetch()) { Write-Output ("  {0} = {1}" -f $r.StringData[1], $r.StringData[2]) }
$view.Close()

Write-Output "--- Environment (expect 1 row: PATH append) ---"
Read-Table "SELECT Environment, Name, Value, Component_ FROM Environment" 4

Write-Output "--- Registry (expect HKLM\SOFTWARE\RevitAgent InstallDir) ---"
Read-Table "SELECT Registry, Root, Key, Name, Value, Component_ FROM Registry" 6

Write-Output "--- File count ---"
$view = $db.OpenView("SELECT COUNT(*) FROM File"); $view.Execute(); $r = $view.Fetch(); Write-Output ("  files = {0}" -f $r.StringData[1]); $view.Close()
