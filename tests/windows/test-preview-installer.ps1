# Verify the preview refuses common traces of an existing school installation
# before it copies files, starts an API, changes services or installs LocalDB.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Installer)
$ErrorActionPreference = 'Stop'
$installerPath = (Resolve-Path $Installer).Path
$adminDir = Join-Path $env:LOCALAPPDATA 'Programs\SmartSchool Admin'
$sessionDir = Join-Path $env:LOCALAPPDATA 'SmartSchoolAdmin'
if ((Test-Path $adminDir) -or (Test-Path $sessionDir)) { throw 'Fixture requires a clean Windows runner.' }
$fixtures = @(
    @{ Dir = $adminDir; File = 'SmartSchool.Admin.exe' },
    @{ Dir = $adminDir; File = 'appsettings.json' },
    @{ Dir = $sessionDir; File = 'session.bin' }
)
foreach ($fixture in $fixtures) {
    $dir = $fixture.Dir
    $fake = Join-Path $dir $fixture.File
    $log = Join-Path $env:TEMP ("server-preview-guard-" + [Guid]::NewGuid() + '.log')
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    [IO.File]::WriteAllText($fake, 'test-only sentinel')
    try {
        $p = Start-Process -FilePath $installerPath -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',("/LOG=`"$log`"") -PassThru
        if (-not $p.WaitForExit(45000)) {
            $detail = if (Test-Path $log) { (Get-Content $log -Raw) } else { 'no setup log' }
            $p.Kill()
            throw "Preview did not promptly refuse $($fixture.File). Setup log: $detail"
        }
        if ($p.ExitCode -eq 0) { throw "Preview accepted existing $($fixture.File)." }
        if (-not (Test-Path $log) -or (Get-Content $log -Raw) -notmatch 'PREVIEW_BLOCK_EXISTING_INSTALL') {
            throw "The existing-install guard did not run for $($fixture.File)."
        }
        if ([IO.File]::ReadAllText($fake) -ne 'test-only sentinel') { throw 'Preview changed existing data.' }
    } finally {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    }
}
Write-Host 'Preview refused existing executable, configuration and saved login without modifying them.'
