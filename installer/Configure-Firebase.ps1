#Requires -Version 5.1
<#
  Configure the installed school API with a Firebase service-account key.
  This script never prints or embeds the key, and does not restart the API.
  Normal usage: run Configure-Firebase.bat on the school PC and select the JSON.
#>
[CmdletBinding()]
param(
    [string]$CredentialFile,
    [string]$ApiFolder
)

$ErrorActionPreference = 'Stop'
$ExpectedFirebaseProject = 'queen-school'

function Test-FirebaseServiceAccount {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'Service-account JSON file nahi mili.'
    }
    try {
        $data = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        # ConvertFrom-Json errors may contain fragments of the file. Do not print them.
        throw 'Service-account file valid JSON nahi hai; koi setting change nahi hui.'
    }
    if ($data.type -ne 'service_account' -or $data.project_id -ne $ExpectedFirebaseProject) {
        throw "Service-account key project '$ExpectedFirebaseProject' ki honi chahiye (Android client config nahi)."
    }
    if ([string]::IsNullOrWhiteSpace($data.client_email) -or
        [string]::IsNullOrWhiteSpace($data.private_key) -or
        -not ([string]$data.private_key).StartsWith('-----BEGIN PRIVATE KEY-----') -or
        -not ([string]$data.private_key).Contains('-----END PRIVATE KEY-----')) {
        throw 'Service-account JSON mein email ya private key missing hai.'
    }
}

function Get-FirebaseApiFolder {
    param([string]$RequestedFolder)

    if ($RequestedFolder) {
        if (-not (Test-Path -LiteralPath (Join-Path $RequestedFolder 'appsettings.json') -PathType Leaf)) {
            throw 'Diye huay API folder mein appsettings.json nahi mila.'
        }
        return (Resolve-Path -LiteralPath $RequestedFolder).Path
    }
    $candidates = @()
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'SmartSchool Server\api' }
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'SmartSchool Server\api' }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'appsettings.json') -PathType Leaf) {
            return $candidate
        }
    }
    throw 'School API ka appsettings.json nahi mila. Pehle server installer install karein.'
}

function Get-ApiReaderSids {
    param([string]$ApiFolder)

    # Current elevated account, logged-in console account and owner(s) of the
    # actual installed API may differ when UAC asks for another administrator.
    $sids = @([System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
    try {
        $user = (Get-CimInstance Win32_ComputerSystem).UserName
        if ($user) {
            $account = [System.Security.Principal.NTAccount]::new($user)
            $sids += $account.Translate([System.Security.Principal.SecurityIdentifier]).Value
        }
    } catch {
        Write-Host 'Console user detect nahi hua; setup usi Windows account se chalayein jo API chalata hai.' -ForegroundColor Yellow
    }
    try {
        $exe = Join-Path $ApiFolder 'SmartSchool.API.exe'
        foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name='SmartSchool.API.exe'")) {
            if ($process.ExecutablePath -and $process.ExecutablePath -ieq $exe) {
                $owner = Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid
                if ($owner.ReturnValue -eq 0 -and $owner.Sid) { $sids += $owner.Sid }
            }
        }
    } catch {
        Write-Host 'API process owner detect nahi hua; setup usi Windows account se chalayein jo API chalata hai.' -ForegroundColor Yellow
    }
    return @($sids | Select-Object -Unique)
}

function Set-PrivateDirectoryAcl {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string[]]$ReaderSids
    )

    # Disable inherited permissions before writing a credential or config backup.
    # Administrators and LocalSystem have full control; only the API user(s)
    # detected above get read access. Well-known SIDs avoid localized group names.
    $acl = [System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $inherit = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
               [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    $none = [System.Security.AccessControl.PropagationFlags]::None
    $allow = [System.Security.AccessControl.AccessControlType]::Allow
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            [System.Security.Principal.SecurityIdentifier]::new($sid),
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            $inherit, $none, $allow)
        [void]$acl.AddAccessRule($rule)
    }
    foreach ($sid in ($ReaderSids | Select-Object -Unique)) {
        if ($sid -in @('S-1-5-18', 'S-1-5-32-544')) { continue }
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            [System.Security.Principal.SecurityIdentifier]::new($sid),
            [System.Security.AccessControl.FileSystemRights]::ReadAndExecute,
            $inherit, $none, $allow)
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Set-FirebaseApiSettings {
    param(
        [Parameter(Mandatory = $true)][string]$ConfigPath,
        [Parameter(Mandatory = $true)][string]$InstalledKeyPath,
        [Parameter(Mandatory = $true)][string]$BackupDirectory
    )

    try {
        $settings = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        throw 'appsettings.json valid JSON nahi hai; original settings change nahi ki gayin.'
    }
    if ($settings -isnot [pscustomobject]) {
        throw 'appsettings.json ka root JSON object hona chahiye; original file nahi badli.'
    }
    if ($null -eq $settings.Firebase) {
        $settings | Add-Member -NotePropertyName Firebase -NotePropertyValue ([pscustomobject]@{}) -Force
    } elseif ($settings.Firebase -isnot [pscustomobject]) {
        throw 'appsettings.json mein Firebase section object nahi hai; original file nahi badli.'
    }
    $settings.Firebase | Add-Member -NotePropertyName Enabled -NotePropertyValue $true -Force
    $settings.Firebase | Add-Member -NotePropertyName CredentialsJsonPath -NotePropertyValue $InstalledKeyPath -Force
    $json = $settings | ConvertTo-Json -Depth 100
    # Ensure a valid serialization before touching the original file.
    $null = $json | ConvertFrom-Json
    $suffix = [guid]::NewGuid().ToString('N')
    $backup = Join-Path $BackupDirectory "appsettings-backup-$suffix.json"
    $temp = "$ConfigPath.$suffix.tmp"
    $replaceBackup = "$ConfigPath.$suffix.replace-backup"
    try {
        # These newly created backup bytes inherit the restricted directory ACL.
        [System.IO.File]::WriteAllBytes($backup, [System.IO.File]::ReadAllBytes($ConfigPath))
        [System.IO.File]::WriteAllText($temp, $json, [System.Text.UTF8Encoding]::new($false))
        # Windows PowerShell 5.1 converts a null backup argument to an invalid
        # empty path. Use a temporary same-volume backup for the atomic replace.
        [System.IO.File]::Replace($temp, $ConfigPath, $replaceBackup)
    } finally {
        # Cleanup errors must not make a successful replacement look like failure.
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
        if (Test-Path -LiteralPath $replaceBackup) { Remove-Item -LiteralPath $replaceBackup -Force -ErrorAction SilentlyContinue }
    }
    return $backup
}

function Select-FirebaseKeyFile {
    Add-Type -AssemblyName System.Windows.Forms
    $picker = [System.Windows.Forms.OpenFileDialog]::new()
    try {
        $picker.Title = 'queen-school Firebase service-account JSON select karein'
        $picker.Filter = 'JSON files (*.json)|*.json|All files (*.*)|*.*'
        $picker.CheckFileExists = $true
        if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
            throw 'File select nahi hui; koi setting change nahi ki gayi.'
        }
        return $picker.FileName
    } finally {
        $picker.Dispose()
    }
}

function Invoke-FirebaseSetup {
    param([string]$KeyPath, [string]$RequestedApiFolder)

    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Configure-Firebase.bat ko Administrator ke taur par chalayein aur UAC allow karein.'
    }
    $api = Get-FirebaseApiFolder -RequestedFolder $RequestedApiFolder
    if (-not $KeyPath) { $KeyPath = Select-FirebaseKeyFile }
    Test-FirebaseServiceAccount -Path $KeyPath

    # Keep this separate from the writable SmartSchool logs directory. Do not
    # follow an existing junction/symlink planted in place of the private folder.
    $secretDir = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'SmartSchool-Private'
    [void](New-Item -ItemType Directory -Path $secretDir -Force)
    if (([System.IO.File]::GetAttributes($secretDir) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Private folder symlink/junction hai; is mein key nahi rakhi gayi.'
    }
    $readers = @(Get-ApiReaderSids -ApiFolder $api)
    Set-PrivateDirectoryAcl -Path $secretDir -ReaderSids $readers
    $target = Join-Path $secretDir ("service-account-" + [guid]::NewGuid().ToString('N') + '.json')
    try {
        # Write new bytes (rather than copying the source ACL) into the private folder.
        [System.IO.File]::WriteAllBytes($target, [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $KeyPath).Path))
        $backup = Set-FirebaseApiSettings -ConfigPath (Join-Path $api 'appsettings.json') `
            -InstalledKeyPath $target -BackupDirectory $secretDir
    } catch {
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
        throw
    }
    Write-Host ''
    Write-Host "Firebase project $ExpectedFirebaseProject ki server configuration save ho gayi." -ForegroundColor Green
    Write-Host 'Key aur settings backup restricted ProgramData folder mein hain; doosri settings rakhi gayi hain.'
    Write-Host "Backup: $backup"
    Write-Host ''
    Write-Host 'ZAROORI: API restart hone par purane queued WhatsApp messages dobara bheje ja sakte hain.' -ForegroundColor Yellow
    Write-Host 'Queue pehle review karein, phir server API restart karein. Yeh tool khud restart nahi karta.' -ForegroundColor Yellow
    Write-Host 'API agar doosre Windows user se chalti ho, us user ko key par read access chahiye.'
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Invoke-FirebaseSetup -KeyPath $CredentialFile -RequestedApiFolder $ApiFolder
    } catch {
        Write-Host "Setup nahi hua: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}
