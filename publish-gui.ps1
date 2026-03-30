$job = Start-Job -ScriptBlock {
    & 'C:\Users\sgaut\.dotnet\dotnet.exe' publish 'C:\Users\sgaut\cleanse10\src\Cleanse10.GUI\Cleanse10.GUI.csproj' --configuration Release --output 'C:\Users\sgaut\cleanse10\publish' 2>&1
}
Write-Output "Publish job started (ID=$($job.Id)). Waiting up to 10 minutes..."
$job | Wait-Job -Timeout 600
Receive-Job $job
