# Safe diagnostic for the Fee Plan API route. Does not read or print appsettings, tokens, or passwords.
$ErrorActionPreference = 'Continue'

Write-Output '=== Fee Plan API diagnostic ==='
Write-Output "Checked at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output ''

try {
    $listeners = @(Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction Stop)
    if ($listeners.Count -eq 0) {
        Write-Output 'Port 5000: no listener found.'
    } else {
        foreach ($listener in $listeners) {
            Write-Output "Port 5000 listener PID: $($listener.OwningProcess)"
            $proc = Get-CimInstance Win32_Process -Filter "ProcessId = $($listener.OwningProcess)" -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Output "Process name: $($proc.Name)"
                Write-Output "Executable path: $($proc.ExecutablePath)"
                if ($proc.ExecutablePath -and (Test-Path -LiteralPath $proc.ExecutablePath)) {
                    $file = Get-Item -LiteralPath $proc.ExecutablePath
                    Write-Output "Executable modified: $($file.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
                    Write-Output "File version: $($file.VersionInfo.FileVersion)"
                }
            }
        }
    }
} catch {
    Write-Output "Could not inspect port 5000: $($_.Exception.Message)"
}

Write-Output ''
Write-Output 'Unauthenticated route probe (401 is expected if the Fee Plan route exists):'
try {
    $response = Invoke-WebRequest -Uri 'http://localhost:5000/api/fee-plans/heads' -Method Get -TimeoutSec 10 -UseBasicParsing
    Write-Output "HTTP status: $([int]$response.StatusCode)"
} catch {
    $response = $_.Exception.Response
    if ($response -and $response.StatusCode) {
        Write-Output "HTTP status: $([int]$response.StatusCode)"
    } else {
        Write-Output "Request failed: $($_.Exception.Message)"
    }
}

Write-Output ''
Write-Output 'If sharing this output, you may redact your Windows account name from any path.'
