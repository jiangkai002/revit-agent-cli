# Admin-extract the MSI and inspect its important database tables without installing it.
param([string]$Msi = "revit-agent.msi", [string]$ExtractDir = "msi-admin-extract")

$ErrorActionPreference = "Stop"
$msiPath = (Resolve-Path $Msi).Path
$workspace = [IO.Path]::GetFullPath((Get-Location).Path).TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$target = [IO.Path]::GetFullPath((Join-Path (Get-Location) $ExtractDir))
if (-not $target.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Extraction target must stay inside the workspace: $target"
}

Write-Output "=== admin-extract (msiexec /a) ==="
if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}
$arguments = @("/a", "`"$msiPath`"", "/qn", "TARGETDIR=`"$target`"")
$process = Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" `
    -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
Write-Output ("msiexec /a exit code: {0}" -f $process.ExitCode)
if ($process.ExitCode -ne 0) { throw "MSI administrative extraction failed." }

$files = @(Get-ChildItem -LiteralPath $target -Recurse -File)
$gui = $files | Where-Object Name -eq "RevitAgent.Gui.exe" | Select-Object -First 1
$cli = $files | Where-Object Name -eq "revit-agent.exe" | Select-Object -First 1
Write-Output ("extracted files: {0}" -f $files.Count)
Write-Output ("RevitAgent.Gui.exe: {0}" -f $(if ($gui) { "present" } else { "MISSING" }))
Write-Output ("revit-agent.exe: {0}" -f $(if ($cli) { "present" } else { "MISSING" }))
if (-not $gui -or -not $cli) { throw "Required application hosts are missing from the MSI." }

Write-Output ""
Write-Output "=== MSI table inspection (WindowsInstaller COM) ==="
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msiPath, 0)

function Read-Rows([string]$sql, [int]$columns) {
    $view = $database.OpenView($sql)
    $null = $view.Execute()
    try {
        while ($record = $view.Fetch()) {
            $values = for ($i = 1; $i -le $columns; $i++) {
                $record.StringData($i)
            }
            [pscustomobject]@{ Values = [string[]]$values }
        }
    } finally {
        $null = $view.Close()
    }
}

$properties = @(Read-Rows 'SELECT `Property`, `Value` FROM `Property`' 2)
Write-Output "--- Property (key values) ---"
foreach ($row in $properties | Where-Object { $_.Values[0] -in @(
    "ProductName", "ProductVersion", "ProductCode", "Manufacturer",
    "UpgradeCode", "ALLUSERS", "ARPPRODUCTICON") }) {
    Write-Output ("  {0} = {1}" -f $row.Values[0], $row.Values[1])
}

Write-Output "--- Environment ---"
foreach ($row in Read-Rows 'SELECT `Environment`, `Name`, `Value`, `Component_` FROM `Environment`' 4) {
    Write-Output ("  " + ($row.Values -join " | "))
}

Write-Output "--- Registry ---"
foreach ($row in Read-Rows 'SELECT `Registry`, `Root`, `Key`, `Name`, `Value`, `Component_` FROM `Registry`' 6) {
    Write-Output ("  " + ($row.Values -join " | "))
}

Write-Output "--- Shortcut ---"
foreach ($row in Read-Rows 'SELECT `Shortcut`, `Name`, `Target`, `Icon_` FROM `Shortcut`' 4) {
    Write-Output ("  " + ($row.Values -join " | "))
}

$icons = @(Read-Rows 'SELECT `Name` FROM `Icon`' 1)
Write-Output ("--- Icon rows: {0} ---" -f $icons.Count)
foreach ($row in $icons) { Write-Output ("  " + $row.Values[0]) }

$fileRows = @(Read-Rows 'SELECT `File` FROM `File`' 1)
Write-Output ("--- File rows: {0} ---" -f $fileRows.Count)

if (-not ($properties | Where-Object { $_.Values[0] -eq "ARPPRODUCTICON" -and $_.Values[1] -eq "ProductIcon" })) {
    throw "ARPPRODUCTICON is not configured."
}
if (-not ($icons | Where-Object { $_.Values[0] -eq "ProductIcon" })) {
    throw "ProductIcon is missing from the MSI Icon table."
}

Write-Output "MSI validation passed."
