# ============================================================================
# SmartSchool — appsettings.json auto-fix + GitHub reports enable
# ADMIN PowerShell mein chalaya jata hai:
#     powershell -ExecutionPolicy Bypass -File Fix-GitHub-Config.ps1
# Kya karta hai:
#   1) API folder dhoondhta hai (Program Files ke dono variants)
#   2) appsettings.json ka backup banata hai
#   3) File ka JSON test hota hai — toota hua ho to KNOWN-GOOD content likhta hai
$token = Read-Host "Apna GitHub token paste karein (github_pat_...)"
if ($token.Length -lt 20) { Write-Host "Token chhota lag raha hai — script band." -ForegroundColor Red; exit 1 }

$ErrorActionPreference = 'Stop'

# ---- 0) API folder dhondo ----
$candidates = @(
    "$env:ProgramFiles\SmartSchool Server\api",
    "${env:ProgramFiles(x86)}\SmartSchool Server\api",
    "C:\Program Files\SmartSchool Server\api",
    "C:\Program Files (x86)\SmartSchool Server\api"
)
$apiFolder = $candidates | Where-Object { Test-Path (Join-Path $_ 'appsettings.json') } | Select-Object -First 1

if (-not $apiFolder) {
    Write-Host "appsettings.json api folder mein nahi mila. Folder path bata dein (jahan SmartSchool.API.exe hai):"
    $manual = Read-Host "Path"
    if (Test-Path (Join-Path $manual 'appsettings.json')) { $apiFolder = $manual }
    else { Write-Host "Wahan appsettings.json nahi mila. Script band." -ForegroundColor Red; exit 1 }
}
$file = Join-Path $apiFolder 'appsettings.json'
Write-Host "File mili: $file" -ForegroundColor Green

# ---- 1) Backup ----
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $apiFolder "appsettings.backup-$stamp.json"
Copy-Item $file $backup -Force
Write-Host "Backup: $backup" -ForegroundColor Yellow

# ---- 2) Current content parse — fail ho to known-good likho ----
$jsonOk = $true
try {
    $raw = Get-Content $file -Raw
    $null = $raw | ConvertFrom-Json
} catch { $jsonOk = $false }

$KNOWN_GOOD = @'
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=SmartSchoolPro;Integrated Security=true;MultipleActiveResultSets=true;TrustServerCertificate=True"
  },
  "Jwt": {
    "Issuer": "SmartSchoolPro",
    "Audience": "SmartSchoolPro.Clients",
    "Key": "zhOj5oRrK46FdNaUc7fS9Y3gvGy8eMPnubCs1Ttiw0D2JLpIqQVHmxZXAlEBWk",
    "ExpiresMinutes": 720
  },
  "WhatsApp": {
    "Provider": "LinkedDevice",
    "Language": "en",
    "NotifyMother": false,
    "LinkedDevice": {
      "BaseUrl": "http://127.0.0.1:5110",
      "SecretsFile": "%LOCALAPPDATA%/SmartSchool/WhatsAppGateway/client-secret.json",
      "RequestTimeoutSeconds": 90,
      "AutoStart": true,
      "NodeExecutable": "../whatsapp-gateway/SmartSchool.WhatsAppGateway.exe",
      "EntryPoint": "../whatsapp-gateway/src/index.js"
    },
    "Queue": {
      "Capacity": 10000,
      "MaxAttempts": 3,
      "RetryWindowSeconds": 120,
      "InitialRetryDelaySeconds": 15,
      "SweepIntervalSeconds": 5
    }
  },
  "Twilio": {
    "AccountSid": "",
    "AuthToken": "",
    "WhatsAppFrom": "whatsapp:+14155238886"
  },
  "BusAttendance": { "DebounceSeconds": 30 },
  "GoogleMaps": { "ApiKey": "" },
  "Firebase": { "Enabled": false, "CredentialsJsonPath": "" },
  "Updates": {
    "Repo": "johnpaul210488-lgtm/smartschool-pro",
    "GitHubToken": "",
    "CacheMinutes": 30,
    "AdminAsset": "SmartSchool.Admin.exe",
    "DriverAsset": "driver-release.apk",
    "ParentAsset": "parent-release.apk"
  },
  "ThingWorx": {
    "Enabled": false,
    "BaseUrl": "https://iotmobility.etisalatdigital.ae",
    "Username": "__TW_USER__",
    "Password": "__TW_PASS__",
    "AppKey": "",
    "PollSeconds": 15,
    "Things": [],
    "BusMap": [],
    "MaxStaleMinutes": 30
  },
  "SeedDemoData": true,
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information",
        "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "C:/ProgramData/SmartSchool/logs/smartschool-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 14
        }
      }
    ]
  },
  "AllowedHosts": "*"
}
'@

if (-not $jsonOk) {
    Write-Host "WARNING: File ka JSON tuta hua tha (is liye API start nahi ho rahi thi)." -ForegroundColor Red
    Write-Host "Known-good content likh raha hun (backup upar saved hai)…" -ForegroundColor Yellow
    [System.IO.File]::WriteAllText($file, $KNOWN_GOOD, (New-Object System.Text.UTF8Encoding($false)))
}

# ---- 3) GitHub section set karo ----
$cfg = (Get-Content $file -Raw | ConvertFrom-Json)
if ($cfg.PSObject.Properties.Name -contains 'GitHub') {
    $cfg.PSObject.Properties.Remove('GitHub')
}
$cfg | Add-Member -MemberType NoteProperty -Name 'GitHub' -Value ([PSCustomObject]@{
    Enabled = $true
    Owner   = 'johnpaul210488-lgtm'
    Repo    = 'smartschool-pro'
    Token   = $token
})

$out = $cfg | ConvertTo-Json -Depth 14
[System.IO.File]::WriteAllText($file, $out, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "GitHub section set: Enabled=true" -ForegroundColor Green

# ---- 4) Final validate ----
try {
    $test = (Get-Content $file -Raw | ConvertFrom-Json)
    if (-not $test.GitHub.Enabled) { throw 'GitHub.Enabled true nahi hua' }
    Write-Host "JSON valid hai — sab set!" -ForegroundColor Green
} catch {
    Write-Host "File phir bhi valid nahi — backup wapis kar raha hun: $_" -ForegroundColor Red
    Copy-Item $backup $file -Force
    exit 1
}

Write-Host ""
Write-Host "Ab sirf yeh karein:" -ForegroundColor Cyan
Write-Host "1) Taskbar kutay ki tray ya Startup folder se 'SmartSchool API (background)' dobara start karein"
Write-Host "   (ya PC sign-out/sign-in — API khud uth jayegi)"
Write-Host "2) Admin app kholain, login karein → 🐛 button se TEST report bhejein"
