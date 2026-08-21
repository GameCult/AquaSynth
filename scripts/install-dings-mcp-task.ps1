[CmdletBinding()]
param(
    [string] $TaskName = 'GameCult AquaSynth Dings MCP',
    [string] $ListenUrl = 'http://127.0.0.1:17878'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $repositoryRoot 'tools\AquaSynthDingsMcp\bin\Release\net10.0\AquaSynthDingsMcp.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "AquaSynthDingsMcp.exe is not built: $executable"
}

$action = New-ScheduledTaskAction `
    -Execute $executable `
    -Argument "--http $ListenUrl" `
    -WorkingDirectory $repositoryRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew `
    -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Limited

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description 'One shared loopback Streamable HTTP MCP endpoint for AquaSynth agent notifications.' `
    -Force | Out-Null

Start-ScheduledTask -TaskName $TaskName
Get-ScheduledTask -TaskName $TaskName | Get-ScheduledTaskInfo
