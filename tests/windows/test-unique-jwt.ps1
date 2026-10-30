$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot '..\..\installer\Ensure-UniqueJwt.ps1'
$dir = Join-Path $env:TEMP ('jwt-test-' + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    $a = Join-Path $dir 'fresh-a.json'; $b = Join-Path $dir 'fresh-b.json'; $existing = Join-Path $dir 'existing.json'
    $template = '{"Jwt":{"Key":"__JWT_KEY__"},"Firebase":{"CredentialsJsonPath":""}}'
    [IO.File]::WriteAllText($a, $template); [IO.File]::WriteAllText($b, $template)
    $original = '{ "Jwt": { "Key": "private-school-fixture-existing-key" }, "Other": "preserve" }'
    [IO.File]::WriteAllText($existing, $original)
    & $script -ConfigPath $a; & $script -ConfigPath $b; & $script -ConfigPath $existing
    $keyA = (Get-Content $a -Raw | ConvertFrom-Json).Jwt.Key
    $keyB = (Get-Content $b -Raw | ConvertFrom-Json).Jwt.Key
    if ($keyA.Length -lt 32 -or $keyB.Length -lt 32 -or $keyA -eq $keyB) { throw 'Fresh installations must have distinct strong keys.' }
    if ([IO.File]::ReadAllText($existing) -cne $original) { throw 'Existing production settings were changed.' }
    $again = [IO.File]::ReadAllText($a); & $script -ConfigPath $a
    if ([IO.File]::ReadAllText($a) -cne $again) { throw 'Running on an existing config changed it.' }
    Write-Host 'Fresh JWT keys are unique; existing config remains unchanged.'
} finally { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
