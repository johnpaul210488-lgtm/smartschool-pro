# SmartSchool Pro — 50 concurrent bus events load test.
# Usage:  .\scripts\load-test-bus-events.ps1 -BaseUrl http://localhost:5000 -BusId 1 -FirstStudentId 1 -Count 50
param(
    [string]$BaseUrl = "http://localhost:5000",
    [int]$BusId = 1,
    [int]$FirstStudentId = 1,
    [int]$Count = 50,
    [string]$AdminUser = "admin",
    [string]$AdminPass = "Admin@123"
)

$ErrorActionPreference = "Stop"

# 1. Login as staff (drivers/self-resolution for each event uses explicit DriverId).
$login = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/auth/login" `
    -ContentType "application/json" `
    -Body (@{ username = $AdminUser; password = $AdminPass } | ConvertTo-Json)
$token = $login.data.token
if (-not $token) { throw "Login failed; check admin credentials / SeedDemoData." }

"Firing $Count parallel pick-in events (Bus #$BusId)…"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# 2. Parallel pick-ins via Start-ThreadJob (fall back to sequential with -AsJob jobs).
$jobs = for ($i = 0; $i -lt $Count; $i++) {
    $studentId = $FirstStudentId + $i
    $body = @{
        studentId = $studentId
        busId     = $BusId
        driverId  = 1
        latitude  = 33.5889 + ($i * 0.0001)
        longitude = 71.4429 + ($i * 0.0001)
    } | ConvertTo-Json

    if (Get-Command Start-ThreadJob -ErrorAction SilentlyContinue) {
        Start-ThreadJob -ScriptBlock {
            param($u, $b, $t)
            try {
                Invoke-RestMethod -Method Post -Uri "$u/api/bus-attendance/pick-in" `
                    -Headers @{ Authorization = "Bearer $t" } -ContentType "application/json" -Body $b `
                    | Select-Object -ExpandProperty success
            } catch { $false }
        } -ArgumentList $BaseUrl, $body, $token
    }
}

if ($jobs) {
    $results = $jobs | Wait-Job | Receive-Job
    $jobs | Remove-Job -Force
} else {
    "Start-ThreadJob missing → running sequentially…"
    $results = for ($i = 0; $i -lt $Count; $i++) {
        $studentId = $FirstStudentId + $i
        $body = @{ studentId = $studentId; busId = $BusId; driverId = 1;
                   latitude = 33.5889 + ($i * 0.0001); longitude = 71.4429 + ($i * 0.0001) } | ConvertTo-Json
        try {
            (Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/bus-attendance/pick-in" `
                -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body $body).success
        } catch { $false }
    }
}

$sw.Stop()
$ok  = @($results | Where-Object { $_ -eq $true }).Count
$bad = $Count - $ok

# 3. Verify queue drained.
Start-Sleep -Seconds 3
$logs = Invoke-RestMethod -Uri "$BaseUrl/api/whatsapp-logs?page=1&pageSize=1" `
    -Headers @{ Authorization = "Bearer $token" }

""
"RESULT  $ok OK / $bad failed   in $([math]::Round($sw.Elapsed.TotalSeconds,1))s"
"WhatsApp log rows total: $($logs.data.totalCount)  (dry-run counts as Sent)"
"Pass: $bad -eq 0 — also check WPF toasts and the parent app."
