param(
  [string]$TargetPath
)

# ---------- Defaults ----------
$default = [ordered]@{
  ExtSet = @('.html','.css','.js','.ts','.json','.md','.txt','.php','.py','.cs','.xml','.yml','.yaml')
  DotFilesAllow = @('.gitignore','.gitattributes','.editorconfig','.env','.env.example')
  ExcludeRegex = '\\(\.git|\.vs|node_modules|dist|build|bin|obj)(\\|$)'
}

function Get-ConfigPaths {
  $userCfgDir = Join-Path $env:APPDATA "DumpToTxt"
  $userCfg    = Join-Path $userCfgDir "settings.json"
  $machCfgDir = Join-Path $env:PROGRAMDATA "DumpToTxt"
  $machCfg    = Join-Path $machCfgDir "settings.json"
  [pscustomobject]@{
    UserDir=$userCfgDir; User=$userCfg
    MachineDir=$machCfgDir; Machine=$machCfg
  }
}

function Load-Settings {
  $paths = Get-ConfigPaths
  foreach ($p in @($paths.User, $paths.Machine)) {
    if (Test-Path -LiteralPath $p) {
      try {
        $j = Get-Content -LiteralPath $p -Raw -Encoding UTF8 | ConvertFrom-Json
        return [pscustomobject]@{
          ExtSet = @($j.ExtSet) | ForEach-Object { "$_".ToLowerInvariant() }
          DotFilesAllow = @($j.DotFilesAllow)
          ExcludeRegex = [string]$j.ExcludeRegex
        }
      } catch { }
    }
  }
  return [pscustomobject]@{
    ExtSet = @($default.ExtSet)
    DotFilesAllow = @($default.DotFilesAllow)
    ExcludeRegex = [string]$default.ExcludeRegex
  }
}

function Save-Settings($settings) {
  $paths = Get-ConfigPaths
  if (-not (Test-Path -LiteralPath $paths.UserDir)) {
    New-Item -ItemType Directory -Path $paths.UserDir -Force | Out-Null
  }
  $obj = [ordered]@{
    ExtSet = @($settings.ExtSet)
    DotFilesAllow = @($settings.DotFilesAllow)
    ExcludeRegex = [string]$settings.ExcludeRegex
  }
  ($obj | ConvertTo-Json -Depth 6) | Out-File -LiteralPath $paths.User -Encoding UTF8 -Force
}

function SafeName([string]$name) {
  $name = $name -replace '[\\/:*?"<>|]+','_'
  if ([string]::IsNullOrWhiteSpace($name)) { $name = "dump" }
  return $name
}

function Show-SettingsGui {
  Add-Type -AssemblyName System.Windows.Forms
  Add-Type -AssemblyName System.Drawing

  $s = Load-Settings

  $knownExt = @(
    '.html','.css','.js','.ts','.json','.md','.txt',
    '.yml','.yaml','.xml','.toml','.ini',
    '.py','.php','.cs','.java','.kt','.go','.rs','.rb','.cpp','.c','.h','.sql'
  )
  $knownExclAlts = @(
    '\.git','\.vs','node_modules','dist','build','bin','obj',
    '\.vscode','\.idea','coverage','\.next','\.nuxt','out'
  )

  $form = New-Object Windows.Forms.Form
  $form.Text = "DumpToTxt Settings"
  # Ensure the window opens fully (avoid clipped bottom controls on some DPI/font settings)
$form.Width = 860
$form.Height = 600
$form.MinimumSize = New-Object System.Drawing.Size(820, 560)
  $form.StartPosition = "CenterScreen"

  $gExt = New-Object Windows.Forms.GroupBox
  $gExt.Text = "Legible file types"
  $gExt.Left = 12; $gExt.Top = 12; $gExt.Width = 400; $gExt.Height = 410
  $form.Controls.Add($gExt)

  $clbExt = New-Object Windows.Forms.CheckedListBox
  $clbExt.Left = 12; $clbExt.Top = 22; $clbExt.Width = 370; $clbExt.Height = 300
  $clbExt.CheckOnClick = $true
  [void]$gExt.Controls.Add($clbExt)

  foreach ($e in $knownExt) { [void]$clbExt.Items.Add($e) }

  $curExt = @($s.ExtSet) | ForEach-Object { $_.ToLowerInvariant() }
  for ($i=0; $i -lt $clbExt.Items.Count; $i++) {
    if ($curExt -contains ($clbExt.Items[$i].ToString().ToLowerInvariant())) {
      $clbExt.SetItemChecked($i, $true)
    }
  }

  $lblExtCustom = New-Object Windows.Forms.Label
  $lblExtCustom.Text = "Enter manual file types (comma-separated). Example: .ps1, .iss"
  $lblExtCustom.Left = 12; $lblExtCustom.Top = 332; $lblExtCustom.Width = 370
  [void]$gExt.Controls.Add($lblExtCustom)

  $tbExtCustom = New-Object Windows.Forms.TextBox
  $tbExtCustom.Left = 12; $tbExtCustom.Top = 352; $tbExtCustom.Width = 370
  $tbExtCustom.Text = ""
  [void]$gExt.Controls.Add($tbExtCustom)

  $gEx = New-Object Windows.Forms.GroupBox
  $gEx.Text = "Excluded folders"
  $gEx.Left = 430; $gEx.Top = 12; $gEx.Width = 400; $gEx.Height = 410
  $form.Controls.Add($gEx)

  $clbEx = New-Object Windows.Forms.CheckedListBox
  $clbEx.Left = 12; $clbEx.Top = 22; $clbEx.Width = 370; $clbEx.Height = 300
  $clbEx.CheckOnClick = $true
  [void]$gEx.Controls.Add($clbEx)

  foreach ($x in $knownExclAlts) { [void]$clbEx.Items.Add($x) }

  $rx = [string]$s.ExcludeRegex
  for ($i=0; $i -lt $clbEx.Items.Count; $i++) {
    $tok = $clbEx.Items[$i].ToString()
    if ($rx -like "*$tok*") { $clbEx.SetItemChecked($i, $true) }
  }

  $lblExCustom = New-Object Windows.Forms.Label
$lblExCustom.Text = "Additional folders to skip (comma-separated). Example: cache, temp"

  $lblExCustom.Left = 12; $lblExCustom.Top = 332; $lblExCustom.Width = 370
  [void]$gEx.Controls.Add($lblExCustom)

  $tbExCustom = New-Object Windows.Forms.TextBox
  $tbExCustom.Left = 12; $tbExCustom.Top = 352; $tbExCustom.Width = 370
  $tbExCustom.Text = ""
  [void]$gEx.Controls.Add($tbExCustom)

  $lblDot = New-Object Windows.Forms.Label
  $lblDot.Text = "Allowed dotfiles (comma-separated):"
  $lblDot.Left = 12; $lblDot.Top = 430; $lblDot.Width = 500
  $form.Controls.Add($lblDot)

  $tbDot = New-Object Windows.Forms.TextBox
  $tbDot.Left = 12; $tbDot.Top = 452; $tbDot.Width = 818
  $tbDot.Text = ($s.DotFilesAllow -join ", ")
  $form.Controls.Add($tbDot)

  $btnSave = New-Object Windows.Forms.Button
  $btnSave.Text = "Save"
  $btnSave.Left = 12; $btnSave.Top = 486; $btnSave.Width = 120
  $form.Controls.Add($btnSave)

  $btnReset = New-Object Windows.Forms.Button
  $btnReset.Text = "Reset defaults"
  $btnReset.Left = 142; $btnReset.Top = 486; $btnReset.Width = 140
  $form.Controls.Add($btnReset)

  $btnClose = New-Object Windows.Forms.Button
  $btnClose.Text = "Close"
  $btnClose.Left = 710; $btnClose.Top = 486; $btnClose.Width = 120
  $form.Controls.Add($btnClose)

  $status = New-Object Windows.Forms.Label
  $status.Left = 300; $status.Top = 492; $status.Width = 380
  $status.Text = ""
  $form.Controls.Add($status)

  function Build-ExtSet {
    $picked = @()
    foreach ($item in $clbExt.CheckedItems) { $picked += $item.ToString().ToLowerInvariant() }
    $custom = $tbExtCustom.Text.Split(",") | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ -match '^\.' }
    $picked += $custom
    $picked | Select-Object -Unique
  }

  function Build-ExcludeRegex {
    $alts = @()
    foreach ($item in $clbEx.CheckedItems) { $alts += $item.ToString() }
$custom = $tbExCustom.Text.Trim()
if ($custom) {
  $customParts = $custom.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
  $alts += $customParts
}

    $alts = $alts | Where-Object { $_ -ne "" } | Select-Object -Unique
    if ($alts.Count -eq 0) { return $default.ExcludeRegex }
    return '\\(' + ($alts -join '|') + ')(\\|$)'
  }

  $btnReset.Add_Click({
    for ($i=0; $i -lt $clbExt.Items.Count; $i++) { $clbExt.SetItemChecked($i, $false) }
    foreach ($e in $default.ExtSet) {
      $idx = $clbExt.Items.IndexOf($e)
      if ($idx -ge 0) { $clbExt.SetItemChecked($idx, $true) }
    }
    for ($i=0; $i -lt $clbEx.Items.Count; $i++) { $clbEx.SetItemChecked($i, $false) }
    $tbExtCustom.Text = ""
    $tbExCustom.Text = ""
    $tbDot.Text = ($default.DotFilesAllow -join ", ")
    $status.Text = "Defaults loaded (not saved yet)."
  })

  $btnSave.Add_Click({
    $newExt = Build-ExtSet
    if ($newExt.Count -eq 0) { $status.Text = "❌ Pick at least 1 extension."; return }
    $newRx = Build-ExcludeRegex
    try { [regex]::new($newRx) | Out-Null } catch { $status.Text = "❌ Exclude regex invalid."; return }
    $dot = $tbDot.Text.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }

    Save-Settings ([pscustomobject]@{
      ExtSet = @($newExt)
      DotFilesAllow = @($dot)
      ExcludeRegex = [string]$newRx
    })
    $status.Text = "✅ Saved."
  })

  $btnClose.Add_Click({ $form.Close() })
  [void]$form.ShowDialog()
}

# ✅ CRITICAL FIX: If no target path -> open settings and exit (no Resolve-Path!)
if ([string]::IsNullOrWhiteSpace($TargetPath)) {
  Show-SettingsGui
  exit 0
}

# ---------- Dumper ----------
try { $TargetPath = (Resolve-Path -LiteralPath $TargetPath).Path } catch {}

if ([string]::IsNullOrWhiteSpace($TargetPath) -or -not (Test-Path -LiteralPath $TargetPath)) {
  Add-Type -AssemblyName System.Windows.Forms
  [System.Windows.Forms.MessageBox]::Show("Target path is missing or does not exist.", "DumpToTxt", "OK", "Error") | Out-Null
  exit 1
}

$settings = Load-Settings

if (Test-Path -LiteralPath $TargetPath -PathType Leaf) {
  $root = Split-Path -Parent $TargetPath
  $baseNameRaw = [IO.Path]::GetFileNameWithoutExtension($TargetPath)
} else {
  $root = $TargetPath
  $baseNameRaw = Split-Path -Leaf $TargetPath
  if ([string]::IsNullOrWhiteSpace($baseNameRaw)) { $baseNameRaw = "folder" }
}

$desktop = [Environment]::GetFolderPath("Desktop")
$timeTag = Get-Date -Format "HH-mm"
$out = Join-Path $desktop ("{0}-dump-{1}.txt" -f (SafeName $baseNameRaw), $timeTag)

if (Test-Path -LiteralPath $out) {
  $i = 1
  while ($true) {
    $candidate = Join-Path $desktop ("{0}-dump-{1}-{2:00}.txt" -f (SafeName $baseNameRaw), $timeTag, $i)
    if (-not (Test-Path -LiteralPath $candidate)) { $out = $candidate; break }
    $i++
  }
}

$excludeRegex = $settings.ExcludeRegex
$extSet = @($settings.ExtSet)
$dotAllow = @($settings.DotFilesAllow)

function IsLegibleFile($fi) {
  $ext = $fi.Extension.ToLowerInvariant()
  if ($extSet -contains $ext) { return $true }
  if ($dotAllow -contains $fi.Name) { return $true }
  return $false
}

"===== DIRECTORY LIST (filtered) =====" | Out-File -Encoding UTF8 $out
"ROOT: $root" | Out-File -Encoding UTF8 -Append $out
"" | Out-File -Encoding UTF8 -Append $out

Get-ChildItem -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue |
Where-Object { $_.FullName -notmatch $excludeRegex } |
Select-Object -ExpandProperty FullName |
Out-File -Encoding UTF8 -Append $out

"`n`n===== FILE CONTENTS (LEGIBLE ONLY) =====" | Out-File -Encoding UTF8 -Append $out

$outNameOnly = [IO.Path]::GetFileName($out)

if (Test-Path -LiteralPath $TargetPath -PathType Leaf) {
  $fi = Get-Item -LiteralPath $TargetPath -ErrorAction Stop
  if ($fi -is [System.IO.FileInfo] -and (IsLegibleFile $fi) -and ($fi.FullName -notmatch $excludeRegex)) {
    "`n==============================`n$($fi.FullName)`n==============================" | Out-File -Encoding UTF8 -Append $out
    Get-Content -Encoding UTF8 -LiteralPath $fi.FullName -Raw | Out-File -Encoding UTF8 -Append $out
  } else {
    "`n[Skipped: file not considered legible or is excluded]" | Out-File -Encoding UTF8 -Append $out
    $fi.FullName | Out-File -Encoding UTF8 -Append $out
  }
} else {
  Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue |
  Where-Object {
    $_.FullName -notmatch $excludeRegex -and
    $_.Name -notmatch '\.min\.' -and
    $_.Name -ne $outNameOnly -and
    (IsLegibleFile $_)
  } |
  Sort-Object FullName |
  ForEach-Object {
    "`n=============================="
    $_.FullName
    "=============================="
    Get-Content -Encoding UTF8 -LiteralPath $_.FullName -Raw
  } | Out-File -Encoding UTF8 -Append $out
}

Start-Process notepad.exe -ArgumentList "`"$out`""
