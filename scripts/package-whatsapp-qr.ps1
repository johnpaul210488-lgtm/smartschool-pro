# Build an isolated Windows test-gateway payload. No linked account, key, QR,
# database, Firebase secret or locally generated session data is copied.
[CmdletBinding()]
param([string]$Destination = 'build-artifacts/api/whatsapp-gateway')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$gateway = Join-Path $root 'apps/whatsapp_gateway'
$dest = if ([IO.Path]::IsPathRooted($Destination)) { $Destination } else { Join-Path $root $Destination }
$node = (Get-Command node.exe -ErrorAction Stop).Source
$npm = (Get-Command npm.cmd -ErrorAction Stop).Source
$version = (& $node --version).Trim()
if ($LASTEXITCODE -ne 0 -or $version -ne 'v22.22.3') { throw 'Packaging requires Node v22.22.3.' }
Push-Location $gateway
try {
    & $npm ci --ignore-scripts --omit=dev --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    & $npm test
    if ($LASTEXITCODE -ne 0) { throw 'QR gateway tests failed.' }
} finally { Pop-Location }
New-Item -ItemType Directory -Path $dest -Force | Out-Null
Copy-Item (Join-Path $gateway 'src') $dest -Recurse -Force
Copy-Item (Join-Path $gateway 'node_modules') $dest -Recurse -Force
Copy-Item (Join-Path $gateway 'package.json') $dest -Force
Copy-Item (Join-Path $gateway 'package-lock.json') $dest -Force
Copy-Item (Join-Path $gateway 'NODE-LICENSE') $dest -Force
Copy-Item $node (Join-Path $dest 'node.exe') -Force
if (-not (Test-Path (Join-Path $dest 'src/index.js')) -or
    -not (Test-Path (Join-Path $dest 'node_modules/baileys/package.json')) -or
    -not (Test-Path (Join-Path $dest 'node.exe'))) { throw 'QR gateway payload incomplete.' }
Write-Host 'WhatsApp QR test gateway packaged (disabled by default).'
