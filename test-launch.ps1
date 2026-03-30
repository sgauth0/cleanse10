Start-Process 'C:\Users\sgaut\cleanse10\publish\Cleanse10.exe'
Start-Sleep 10
$p = Get-Process -Name Cleanse10 -ErrorAction SilentlyContinue
if ($p -ne $null) {
    Write-Output "Window launched OK PID=$($p.Id)"
    Stop-Process -InputObject $p
} else {
    Write-Output "NOT RUNNING - checking crash log"
    $log = 'C:\Users\sgaut\cleanse10\publish\cleanse10-crash.log'
    if (Test-Path $log) { Get-Content $log } else { "No crash log found." }
}
