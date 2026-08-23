# Generates a WiX fragment (payload-fragment.wxs) that installs every file under
# installer-payload\ into INSTALLFOLDER, preserving the directory tree. WiX v7 has
# no `heat` subcommand, so this walker is the version-independent way to harvest the
# payload. Run from the repo root; emits payload-fragment.wxs beside installer.wxs.
#
# Output shape (WiX v4+ schema, http://wixtoolset.org/schemas/v4/wxs):
#   <Fragment>
#     <DirectoryRef Id="<folderId>">  one per folder (root=INSTALLFOLDER)
#       <Directory Id="d_..." Name="<leaf>" />   for each subfolder
#       <Component Id="c_N" Directory="<folderId>">  for each file
#         <File Id="f_N" Source="installer-payload\<rel>" KeyPath="yes" />
#       </Component>
#     </DirectoryRef>
#     ...
#     <ComponentGroup Id="PayloadFiles">
#       <ComponentRef Id="c_N" />  one per file
#     </ComponentGroup>
#   </Fragment>
# Components omit Guid (WiX v4+ auto-generates a stable Guid from the Component Id).

param(
    [string]$Payload = "installer-payload",
    [string]$OutFile = "payload-fragment.wxs"
)

$root = (Resolve-Path $Payload).Path.TrimEnd('\', '/')
$rootRel = $Payload.TrimEnd('\', '/')  # for Source= prefix

# --- collect ALL folders (incl. intermediates with no direct files, e.g. runtimes\
#     win-x64 which contain only the native\ subfolder) so the Directory tree is complete ---
$folders = @('')  # root always present
Get-ChildItem $root -Recurse -Directory | ForEach-Object {
    $rel = $_.FullName.Substring($root.Length).TrimStart('\', '/')
    $folders += $rel
}
# --- collect files (rel path + parent folder) ---
$files = @()
Get-ChildItem $root -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($root.Length).TrimStart('\', '/')
    $folder = Split-Path $rel -Parent  # '' for root
    $files += [pscustomobject]@{ Rel = $rel; Folder = $folder }
}

# --- folder -> WiX Id map ---
function Sanitize-Id([string]$s) {
    # WiX Ids: letters, digits, dot, underscore. Map anything else to _.
    ($s -replace '[^A-Za-z0-9._]', '_')
}
$folderId = @{}
foreach ($f in $folders) {
    $folderId[$f] = if ([string]::IsNullOrEmpty($f)) { 'INSTALLFOLDER' } else { 'd_' + (Sanitize-Id $f) }
}

# --- children: subfolders + files of a given folder ---
function Get-Subfolders([string]$parent) {
    $folders | Where-Object { $_ -ne '' -and (Split-Path $_ -Parent) -eq $parent } |
        Sort-Object
}
function Get-Files([string]$parent) {
    $files | Where-Object { $_.Folder -eq $parent }
}

# --- emit fragment ---
$nl = "`r`n"
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("<Wix xmlns=`"http://wixtoolset.org/schemas/v4/wxs`">$nl")
[void]$sb.Append("  <Fragment>$nl")

$compCount = 0
$compRefs = New-Object System.Collections.Generic.List[string]

# one DirectoryRef per folder (root first, then subfolders by depth)
$orderedFolders = $folders | Sort-Object { ($_ -split '\\').Count }, { $_ }
foreach ($f in $orderedFolders) {
    $id = $folderId[$f]
    [void]$sb.Append("    <DirectoryRef Id=`"$id`">$nl")

    foreach ($sub in (Get-Subfolders $f)) {
        $leaf = Split-Path $sub -Leaf
        $subId = $folderId[$sub]
        [void]$sb.Append("      <Directory Id=`"$subId`" Name=`"$leaf`" />$nl")
    }

    foreach ($fl in (Get-Files $f)) {
        $compCount++
        $cId = "c_$compCount"
        $fId = "f_$compCount"
        $src = "$rootRel\" + ($fl.Rel -replace '/', '\')
        [void]$sb.Append("      <Component Id=`"$cId`" Directory=`"$id`" Bitness=`"always64`">$nl")
        [void]$sb.Append("        <File Id=`"$fId`" Source=`"$src`" KeyPath=`"yes`" />$nl")
        [void]$sb.Append("      </Component>$nl")
        $compRefs.Add("      <ComponentRef Id=`"$cId`" />")
    }

    [void]$sb.Append("    </DirectoryRef>$nl")
}

[void]$sb.Append("    <ComponentGroup Id=`"PayloadFiles`">$nl")
foreach ($r in $compRefs) { [void]$sb.Append("$r$nl") }
[void]$sb.Append("    </ComponentGroup>$nl")

[void]$sb.Append("  </Fragment>$nl")
[void]$sb.Append("</Wix>$nl")

Set-Content -Path $OutFile -Value $sb.ToString() -Encoding UTF8
Write-Output ("Generated {0}: {1} folders, {2} files ({3} components)" -f $OutFile, $folders.Count, $files.Count, $compCount)
