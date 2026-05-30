# Sets the computer name at first boot. Mode + fixed name are injected by the builder.
$ErrorActionPreference = 'Continue'
$log = Join-Path $env:WINDIR 'Setup\Scripts\win11builder-firstboot.log'
function Write-Log($m) { "$([DateTime]::Now.ToString('s')) [name] $m" | Out-File -FilePath $log -Append -Encoding utf8 }

$mode  = '{{MODE}}'
$fixed = '{{FIXEDNAME}}'

# NetBIOS-safe name: strip invalid chars, cap at 15, reject placeholder serials.
function Get-SafeName([string]$raw) {
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    $placeholders = @('To be filled by O.E.M.', 'Default string', 'System Serial Number', 'None', '0', 'Not Specified')
    if ($placeholders -contains $raw.Trim()) { return $null }
    $clean = ($raw -replace '[^A-Za-z0-9-]', '')
    if ($clean.Length -gt 15) { $clean = $clean.Substring(0, 15) }
    if ([string]::IsNullOrWhiteSpace($clean)) { return $null }
    return $clean
}

switch ($mode) {
    'Fixed'  { $name = Get-SafeName $fixed }
    'Serial' { $name = Get-SafeName ((Get-CimInstance Win32_BIOS -ErrorAction SilentlyContinue).SerialNumber) }
    default  { $name = $null }   # Random
}

if (-not $name) {
    $rand = -join ((48..57) + (65..70) | Get-Random -Count 6 | ForEach-Object { [char]$_ })
    $name = "WIN-$rand"
    Write-Log "Falling back to generated name."
}

Write-Log "Computer name -> $name"
try {
    Rename-Computer -NewName $name -Force -ErrorAction Stop
    Write-Log "Rename succeeded (applies after reboot)."
}
catch {
    Write-Log "Rename failed: $($_.Exception.Message)"
}
