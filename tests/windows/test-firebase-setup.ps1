# Run with Windows PowerShell 5.1: powershell -NoProfile -File tests/windows/test-firebase-setup.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\installer\Configure-Firebase.ps1')

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw "FAIL: $message" }
}
function Assert-Throws([scriptblock]$action, [string]$message) {
    $threw = $false
    try { & $action } catch { $threw = $true }
    Assert-True $threw $message
}
function Write-Utf8([string]$path, [string]$content) {
    [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
}

$testRoot = Join-Path $env:TEMP ("smartschool-firebase-test-" + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $testRoot)
try {
    $key = Join-Path $testRoot 'fake-service-account.json'
    $config = Join-Path $testRoot 'appsettings.json'
    $backups = Join-Path $testRoot 'private-backups'
    [void](New-Item -ItemType Directory -Path $backups)
    $reader = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    Set-PrivateDirectoryAcl -Path $backups -ReaderSids @($reader)
    $acl = Get-Acl -LiteralPath $backups
    Assert-True $acl.AreAccessRulesProtected 'private directory must not inherit permissions'
    $allowed = @($acl.Access | Where-Object { $_.AccessControlType -eq 'Allow' } | ForEach-Object {
        $_.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
    })
    Assert-True ($allowed -contains 'S-1-5-18') 'LocalSystem must be allowed'
    Assert-True ($allowed -contains 'S-1-5-32-544') 'Administrators must be allowed'
    Assert-True (-not ($allowed -contains 'S-1-1-0')) 'Everyone must not be allowed'
    Assert-True (-not ($allowed -contains 'S-1-5-32-545')) 'All local users must not be allowed'

    $fake = [ordered]@{
        type = 'service_account'
        project_id = 'queen-school'
        client_email = 'fake@queen-school.iam.gserviceaccount.com'
        private_key = "-----BEGIN PRIVATE KEY-----`nNOT-A-REAL-KEY`n-----END PRIVATE KEY-----`n"
    }
    Write-Utf8 $key ($fake | ConvertTo-Json)
    Test-FirebaseServiceAccount -Path $key
    $fake.project_id = 'other-project'
    Write-Utf8 $key ($fake | ConvertTo-Json)
    Assert-Throws { Test-FirebaseServiceAccount -Path $key } 'wrong project must be rejected'
    Write-Utf8 $key '{"project_info":{"project_id":"queen-school"}}'
    Assert-Throws { Test-FirebaseServiceAccount -Path $key } 'Android client config is not a server key'
    Write-Utf8 $key '{"private_key":"DO-NOT-LOG-THIS'
    $parseError = $null
    try { Test-FirebaseServiceAccount -Path $key } catch { $parseError = $_.Exception.Message }
    Assert-True ($null -ne $parseError) 'malformed key must be rejected'
    Assert-True ($parseError -notmatch 'DO-NOT-LOG-THIS') 'key fragments must never appear in parse errors'

    $before = '{"Firebase":{"Enabled":false,"CredentialsJsonPath":"old","Custom":17},"Jwt":{"Key":"existing"},"Twilio":{"Enabled":true},"Other":[1,2]}'
    Write-Utf8 $config $before
    $backup = Set-FirebaseApiSettings -ConfigPath $config -InstalledKeyPath 'C:\secure\firebase.json' -BackupDirectory $backups
    $after = Get-Content -LiteralPath $config -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($after.Firebase.Enabled -eq $true) 'Firebase must be enabled'
    Assert-True ($after.Firebase.CredentialsJsonPath -eq 'C:\secure\firebase.json') 'new key path must be set'
    Assert-True ($after.Firebase.Custom -eq 17) 'unrelated Firebase properties must be preserved'
    Assert-True ($after.Jwt.Key -eq 'existing' -and $after.Twilio.Enabled -and $after.Other.Count -eq 2) 'other settings must be preserved'
    Assert-True ((Get-Content -LiteralPath $backup -Raw -Encoding UTF8) -eq $before) 'backup must contain the original config'

    Write-Utf8 $config '{"Firebase":null,"Other":"keep"}'
    $null = Set-FirebaseApiSettings -ConfigPath $config -InstalledKeyPath 'C:\secure\key.json' -BackupDirectory $backups
    $after = Get-Content -LiteralPath $config -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($after.Firebase.Enabled -eq $true -and $after.Other -eq 'keep') 'null Firebase section must be created safely'

    foreach ($invalid in @('{"Firebase":"bad","Other":"keep"}', '{"Firebase": ')) {
        Write-Utf8 $config $invalid
        Assert-Throws { Set-FirebaseApiSettings -ConfigPath $config -InstalledKeyPath 'C:\x.json' -BackupDirectory $backups } 'bad config must fail'
        Assert-True ((Get-Content -LiteralPath $config -Raw -Encoding UTF8) -eq $invalid) 'bad config must remain byte-for-byte unchanged'
    }
    Write-Host 'Firebase server setup tests passed.'
} finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
