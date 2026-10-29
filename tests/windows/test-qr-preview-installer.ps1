# Verify the preview refuses a simulated existing school installation before
# it copies files, starts an API, changes services or installs LocalDB.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Installer)
$ErrorActionPreference = 'Stop'
$installerPath = (Resolve-Path $Installer).Path
$fakeDir = Join-Path $env:LOCALAPPDATA 'Programs\SmartSchool Admin'
if (Test-Path $fakeDir) { throw 'Fixture requires a clean Windows runner.' }
New-Item -ItemType Directory -Path $fakeDir -Force | Out-Null
$fakeExe = Join-Path $fakeDir 'SmartSchool.Admin.exe'
$log = Join-Path $env:TEMP ("qr-preview-guard-" + [Guid]::NewGuid() + '.log')
[IO.File]::WriteAllText($fakeExe, 'test-only sentinel')
try {
    $p = Start-Process -FilePath $installerPath -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',("/LOG=`"$log`"") -PassThru
    if (-not $p.WaitForExit(20000)) {
        $p.Kill()
        throw 'Preview did not promptly refuse an existing school installation.'
    }
    if ($p.ExitCode -eq 0) { throw 'Preview accepted an existing school installation.' }
    if (-not (Test-Path $log) -or (Get-Content $log -Raw) -notmatch 'PREVIEW_BLOCK_EXISTING_INSTALL') {
        throw 'The explicit existing-install guard did not run.'
    }
    if ([IO.File]::ReadAllText($fakeExe) -ne 'test-only sentinel') { throw 'Preview changed the existing app.' }
    Write-Host 'Preview refused an existing installation without modifying it.'
} finally {
    Remove-Item -LiteralPath $fakeDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
}
