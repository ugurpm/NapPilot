# Shows NapPilot's data files, log tail and registered Task Scheduler tasks, for manual testing.
param([int]$LogLines = 20)

$data = Join-Path $env:APPDATA 'NapPilot'
"--- data ($data)"
Get-ChildItem $data -ErrorAction SilentlyContinue | Select-Object Name, LastWriteTime | Format-Table -AutoSize | Out-String -Width 200

if (Test-Path (Join-Path $data 'settings.json')) {
    "--- settings.json"
    Get-Content (Join-Path $data 'settings.json')
}

"--- tasks"
foreach ($task in Get-ScheduledTask -TaskName 'NapPilot-*' -ErrorAction SilentlyContinue) {
    $info = $task | Get-ScheduledTaskInfo
    "{0}  next={1}  last={2}  result={3}  wake={4}  {5}" -f $task.TaskName, $info.NextRunTime, $info.LastRunTime,
        $info.LastTaskResult, $task.Settings.WakeToRun, $task.Description
}

"--- log (last $LogLines lines)"
Get-Content (Join-Path $data 'nappilot.log') -Tail $LogLines -ErrorAction SilentlyContinue
