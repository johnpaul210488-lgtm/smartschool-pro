# Generate a private per-install JWT signing key only for a fresh installer
# template. Existing school configuration is never rewritten during upgrade.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$ConfigPath)
$ErrorActionPreference = 'Stop'
$config = [IO.File]::ReadAllText($ConfigPath)
$pattern = '"Key"\s*:\s*"__JWT_KEY__"'
$matches = [regex]::Matches($config, $pattern)
if ($matches.Count -eq 0) { exit 0 } # Existing key/config: preserve byte-for-byte.
if ($matches.Count -ne 1) { throw 'Ambiguous JWT template; no changes made.' }
$bytes = New-Object byte[] 48
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
$key = [Convert]::ToBase64String($bytes)
$updated = ([regex]::new($pattern)).Replace($config, ('"Key": "' + $key + '"'), 1)
# Validate before replacing. The installer template is strict JSON.
$parsed = $updated | ConvertFrom-Json
if ($parsed.Jwt.Key -ne $key -or $key.Length -lt 32) { throw 'JWT generation failed.' }
$temp = "$ConfigPath.tmp-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.File]::WriteAllText($temp, $updated, (New-Object Text.UTF8Encoding($false)))
    [IO.File]::Copy($temp, $ConfigPath, $true)
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
}
