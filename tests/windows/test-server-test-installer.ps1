# Disposable runner only: ensure a manual-test build refuses unattended upgrades
# without explicit backup acknowledgement, then preserves config and replaces old gateway files.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Installer)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path $Installer).Path
$root = Join-Path $env:TEMP ('school-upgrade-test-' + [Guid]::NewGuid())
$api = Join-Path $root 'api'
$legacyGateway = Join-Path $api 'whatsapp-gateway'
New-Item -ItemType Directory -Path $legacyGateway -Force | Out-Null
$config = Join-Path $api 'appsettings.json'
$original = '{"Jwt":{"Key":"existing-test-only-private-key-kept-on-upgrade"},"Firebase":{"CredentialsJsonPath":"C:\\private\\firebase.json"},"marker":"leave unchanged"}'
[IO.File]::WriteAllText($config, $original)
[IO.File]::WriteAllText((Join-Path $legacyGateway 'legacy.js'), 'obsolete duplicate gateway sentinel')
$log = Join-Path $env:TEMP ('school-test-setup-' + [Guid]::NewGuid() + '.log')
try {
    $p = Start-Process -FilePath $exe -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',("/DIR=`"$root`""),("/LOG=`"$log`"") -PassThru
    if (-not $p.WaitForExit(45000)) { $p.Kill(); throw 'Unacknowledged installer did not refuse promptly.' }
    if ($p.ExitCode -eq 0) { throw 'Installer accepted an unacknowledged backup.' }
    if ([IO.File]::ReadAllText($config) -cne $original) { throw 'Refused install changed existing config.' }
    $p = Start-Process -FilePath $exe -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/I_HAVE_VERIFIED_DB_AND_CONFIG_BACKUPS',("/DIR=`"$root`""),("/LOG=`"$log`"") -PassThru
    if (-not $p.WaitForExit(600000)) { $p.Kill(); throw 'Installer upgrade timed out.' }
    if ($p.ExitCode -ne 0) { throw "Installer upgrade failed with exit code $($p.ExitCode). See $log" }
    if ([IO.File]::ReadAllText($config) -cne $original) { throw 'Upgrade changed the existing config.' }
    if (Test-Path $legacyGateway) { throw 'Retired duplicate API gateway payload was not removed.' }
    if (-not (Test-Path (Join-Path $root 'whatsapp-gateway\SmartSchool.WhatsAppGateway.exe')) -or
        -not (Test-Path (Join-Path $api 'SmartSchool.API.exe'))) { throw 'Gateway/API payload missing.' }
    Write-Host 'Manual installer backup acknowledgement and config-preserving upgrade passed.'
} finally {
    Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $log -Force -ErrorAction SilentlyContinue
}
