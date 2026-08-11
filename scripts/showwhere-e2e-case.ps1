param(
    [Parameter(Mandatory = $true)][string]$Question,
    [Parameter(Mandatory = $true)][string]$SettingsUri,
    [Parameter(Mandatory = $true)][string]$ExpectedLabels,
    [switch]$ClickFinalTarget
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultPath = Join-Path $projectRoot 'training\e2e-windows-sessions.jsonl'
$debugPath = Join-Path $env:LOCALAPPDATA 'ShowWhere\windows-debug.log'

trap {
    $line = $_.InvocationInfo.ScriptLineNumber
    Write-Host "E2E ERROR line $line : $($_.Exception.Message)" -ForegroundColor Red
    try { Activate-VsCode } catch { }
    exit 1
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if (-not ('ShowWhereE2ENative' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShowWhereE2ENative {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@
}

function Invoke-ScreenClick([double]$x, [double]$y) {
    [ShowWhereE2ENative]::SetCursorPos([int][Math]::Round($x), [int][Math]::Round($y)) | Out-Null
    Start-Sleep -Milliseconds 45
    [ShowWhereE2ENative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    # Hold the button long enough for ShowWhere's 35 ms interaction monitor to
    # observe the pressed state even on fast machines.
    Start-Sleep -Milliseconds 55
    [ShowWhereE2ENative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Get-ProcessWindows([int]$processId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $processId)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        $condition)
}

function Get-ShowWherePanel {
    $process = Get-Process ShowWhere -ErrorAction Stop | Select-Object -First 1
    return @(Get-ProcessWindows $process.Id) | Where-Object {
        $_.Current.Name -eq 'ShowWhere' -and $_.Current.ClassName -eq 'Window'
    } | Select-Object -First 1
}

function Get-FloatingAssistant {
    $process = Get-Process ShowWhere -ErrorAction Stop | Select-Object -First 1
    return @(Get-ProcessWindows $process.Id) | Where-Object {
        $bounds = $_.Current.BoundingRectangle
        $_.Current.Name -eq '' -and $bounds.Width -ge 45 -and $bounds.Width -le 100 -and $bounds.Height -ge 45 -and $bounds.Height -le 100
    } | Select-Object -First 1
}

function Get-Descendants($window) {
    return $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
}

function Find-Control($window, [string]$automationId, [string]$name, [string]$typeName) {
    return @(Get-Descendants $window) | Where-Object {
        ($automationId -eq '' -or $_.Current.AutomationId -eq $automationId) -and
        ($name -eq '' -or $_.Current.Name -eq $name) -and
        ($typeName -eq '' -or $_.Current.ControlType.ProgrammaticName -eq $typeName)
    } | Select-Object -Last 1
}

function Invoke-Control($control) {
    if (-not $control) { throw 'Requested UI control was not found or was not ready.' }
    $pattern = $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Invoke-ValidatedTarget($target, $overlay) {
    Invoke-ScreenClick ($overlay.X + $overlay.Width / 2) ($overlay.Y + $overlay.Height / 2)
    Start-Sleep -Milliseconds 140
    if (-not $target) { return }
    try {
        $target.Element.SetFocus()
        $pattern = $target.Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        return
    } catch { }
    try {
        $pattern = $target.Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $pattern.Select()
        return
    } catch { }
    try {
        $pattern = $target.Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $pattern.Toggle()
    } catch { }
}

function Activate-VsCode {
    $code = Get-Process Code -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
    if ($code) {
        $shell = New-Object -ComObject WScript.Shell
        $shell.SendKeys('%')
        Start-Sleep -Milliseconds 40
        [ShowWhereE2ENative]::ShowWindow($code.MainWindowHandle, 9) | Out-Null
        [ShowWhereE2ENative]::SetForegroundWindow($code.MainWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 250
    }
}

function Activate-Settings {
    $settings = Get-Process ApplicationFrameHost -ErrorAction SilentlyContinue |
        Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
    if (-not $settings) { throw 'Windows Settings window was not found.' }
    $shell = New-Object -ComObject WScript.Shell
    $shell.SendKeys('%')
    Start-Sleep -Milliseconds 40
    [ShowWhereE2ENative]::ShowWindow($settings.MainWindowHandle, 9) | Out-Null
    [ShowWhereE2ENative]::SetForegroundWindow($settings.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 300
    return $settings
}

function Open-ShowWherePanel($settings) {
    $assistant = Get-FloatingAssistant
    if (-not $assistant) { throw 'ShowWhere floating assistant was not found.' }
    $panel = Get-ShowWherePanel
    if ($panel) {
        $bounds = $assistant.Current.BoundingRectangle
        Invoke-ScreenClick ($bounds.X + $bounds.Width / 2) ($bounds.Y + $bounds.Height / 2)
        Start-Sleep -Milliseconds 180
    }
    [ShowWhereE2ENative]::SetForegroundWindow($settings.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 120
    $assistant = Get-FloatingAssistant
    $bounds = $assistant.Current.BoundingRectangle
    Invoke-ScreenClick ($bounds.X + $bounds.Width / 2) ($bounds.Y + $bounds.Height / 2)
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        Start-Sleep -Milliseconds 80
        $panel = Get-ShowWherePanel
        if ($panel -and (Find-Control $panel 'GoalInput' '' 'ControlType.Edit')) { return $panel }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'ShowWhere panel did not open.'
}

function Get-NewOverlayEvent([int]$afterLine) {
    $deadline = [DateTime]::UtcNow.AddSeconds(18)
    do {
        Start-Sleep -Milliseconds 100
        $lines = if (Test-Path $debugPath) { @(Get-Content $debugPath -Encoding utf8) } else { @() }
        for ($index = $afterLine; $index -lt $lines.Count; $index++) {
            if ($lines[$index] -match 'event=overlay_show_target x=([\d.-]+) y=([\d.-]+) width=([\d.-]+) height=([\d.-]+)') {
                return [pscustomobject]@{ Line=$index + 1; X=[double]$matches[1]; Y=[double]$matches[2]; Width=[double]$matches[3]; Height=[double]$matches[4] }
            }
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    return $null
}

function Find-ElementAtHighlight($settings, $overlay) {
    $windows = @(Get-ProcessWindows $settings.Id)
    $candidates = foreach ($window in $windows) {
        foreach ($element in @(Get-Descendants $window)) {
            $current = $element.Current
            $bounds = $current.BoundingRectangle
            if (-not $current.Name -or $current.IsOffscreen -or $bounds.Width -le 0 -or $bounds.Height -le 0) { continue }
            $intersectionWidth = [Math]::Max(0, [Math]::Min($bounds.Right, $overlay.X + $overlay.Width) - [Math]::Max($bounds.X, $overlay.X))
            $intersectionHeight = [Math]::Max(0, [Math]::Min($bounds.Bottom, $overlay.Y + $overlay.Height) - [Math]::Max($bounds.Y, $overlay.Y))
            $intersection = $intersectionWidth * $intersectionHeight
            if ($intersection -le 0) { continue }
            $overlayArea = [Math]::Max(1, $overlay.Width * $overlay.Height)
            $elementArea = [Math]::Max(1, $bounds.Width * $bounds.Height)
            [pscustomobject]@{ Element=$element; Name=$current.Name; Role=$current.ControlType.ProgrammaticName; Bounds=$bounds; Score=$intersection / [Math]::Max($overlayArea, $elementArea) }
        }
    }
    return $candidates | Sort-Object Score -Descending | Select-Object -First 1
}

function Bring-ExpectedElementIntoView($settings, [string]$expectedName, [string]$expectedRole) {
    $deadline = [DateTime]::UtcNow.AddSeconds(2)
    do {
        foreach ($window in @(Get-ProcessWindows $settings.Id)) {
            $match = @(Get-Descendants $window) | Where-Object {
                $_.Current.Name -eq $expectedName -and
                (-not $expectedRole -or $_.Current.ControlType.ProgrammaticName -eq $expectedRole)
            } | Select-Object -First 1
            if (-not $match) { continue }
            if (-not $match.Current.IsOffscreen) { return }
            try {
                $pattern = $match.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern)
                $pattern.ScrollIntoView()
                Start-Sleep -Milliseconds 250
            } catch { }
            return
        }
        Start-Sleep -Milliseconds 80
    } while ([DateTime]::UtcNow -lt $deadline)
}

function Get-LatestAssistantText($panel) {
    $messages = @(Get-Descendants $panel) | Where-Object { $_.Current.AutomationId -eq 'BubbleText' }
    return ($messages | Select-Object -Last 1).Current.Name
}

function Mark-LatestAnswer($panel, [bool]$correct) {
    $prefix = if ($correct) { 'O ' } else { 'X ' }
    $button = @(Get-Descendants $panel) | Where-Object {
        $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and $_.Current.Name.StartsWith($prefix) -and $_.Current.IsEnabled
    } | Select-Object -Last 1
    if ($button) { Invoke-Control $button; Start-Sleep -Milliseconds 120 }
}

function Stop-ActiveGuidance($panel) {
    if (-not $panel) { return }
    try {
        $input = Find-Control $panel 'GoalInput' '' 'ControlType.Edit'
        $inputBounds = $input.Current.BoundingRectangle
        $button = @(Get-Descendants $panel) | Where-Object {
            $bounds = $_.Current.BoundingRectangle
            $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and $_.Current.IsEnabled -and
            -not $_.Current.Name.StartsWith('O ') -and -not $_.Current.Name.StartsWith('X ') -and
            $bounds.Y -gt $inputBounds.Bottom
        } | Sort-Object { $_.Current.BoundingRectangle.Y }, { $_.Current.BoundingRectangle.X } -Descending | Select-Object -First 1
        if ($button) { Invoke-Control $button; Start-Sleep -Milliseconds 100 }
    } catch { }
}

function Write-StepRecord($record) {
    $directory = Split-Path -Parent $resultPath
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $line = ($record | ConvertTo-Json -Compress -Depth 8) + [Environment]::NewLine
    [System.IO.File]::AppendAllText(
        $resultPath,
        $line,
        (New-Object System.Text.UTF8Encoding($false)))
}

$runId = [Guid]::NewGuid().ToString('N')
$expectedLabelList = @($ExpectedLabels -split '\|' | Where-Object { $_ })
Write-Host "CASE $Question" -ForegroundColor Cyan
Activate-VsCode
Start-Process $SettingsUri
$settings = $null
$deadline = [DateTime]::UtcNow.AddSeconds(5)
do {
    Start-Sleep -Milliseconds 100
    $settings = Get-Process ApplicationFrameHost -ErrorAction SilentlyContinue |
        Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
} while (-not $settings -and [DateTime]::UtcNow -lt $deadline)
Start-Sleep -Milliseconds 650
$settings = Activate-Settings
$panel = Open-ShowWherePanel $settings
$input = Find-Control $panel 'GoalInput' '' 'ControlType.Edit'
$valuePattern = $input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$valuePattern.SetValue($Question)
$inputBounds = $input.Current.BoundingRectangle
$send = $null
$sendDeadline = [DateTime]::UtcNow.AddSeconds(2)
do {
    $send = @(Get-Descendants $panel) | Where-Object {
        $bounds = $_.Current.BoundingRectangle
        $centerX = $bounds.X + $bounds.Width / 2
        $centerY = $bounds.Y + $bounds.Height / 2
        $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and $_.Current.IsEnabled -and
        $centerX -ge $inputBounds.X -and $centerX -le $inputBounds.Right -and
        $centerY -ge $inputBounds.Y -and $centerY -le $inputBounds.Bottom
    } | Select-Object -First 1
    if (-not $send) { Start-Sleep -Milliseconds 40 }
} while (-not $send -and [DateTime]::UtcNow -lt $sendDeadline)
$debugLine = if (Test-Path $debugPath) { @(Get-Content $debugPath -Encoding utf8).Count } else { 0 }
Invoke-Control $send

$casePassed = $true
for ($step = 0; $step -lt $expectedLabelList.Count; $step++) {
    $expected = $expectedLabelList[$step]
    $expectedParts = $expected -split '#', 2
    $expectedName = $expectedParts[0]
    $expectedRole = if ($expectedParts.Count -gt 1) { $expectedParts[1] } else { '' }
    Bring-ExpectedElementIntoView $settings $expectedName $expectedRole
    $overlay = Get-NewOverlayEvent $debugLine
    $panel = Get-ShowWherePanel
    $message = if ($panel) { Get-LatestAssistantText $panel } else { '' }
    if (-not $overlay) {
        Write-StepRecord ([ordered]@{schemaVersion='showwhere-e2e-v1';runId=$runId;timestamp=(Get-Date).ToUniversalTime().ToString('o');question=$Question;settingsUri=$SettingsUri;step=$step+1;expectedLabel=$expectedName;expectedRole=$expectedRole;actualLabel=$null;assistantMessage=$message;verdict='fail_no_overlay'})
        Write-Host "FAIL step $($step+1): overlay missing" -ForegroundColor Red
        $casePassed = $false
        break
    }
    $debugLine = $overlay.Line
    $target = Find-ElementAtHighlight $settings $overlay
    $actual = if ($target) { $target.Name } else { $null }
    $actualRole = if ($target) { $target.Role } else { $null }
    $nameMatches = $actual -and ($actual -eq $expectedName -or $actual -like "*$expectedName*")
    $roleMatches = -not $expectedRole -or $actualRole -eq $expectedRole
    $correct = $nameMatches -and $roleMatches
    $targetBounds = $null
    if ($target) {
        $targetBounds = @{x=$target.Bounds.X;y=$target.Bounds.Y;width=$target.Bounds.Width;height=$target.Bounds.Height}
    }
    $verdict = if ($correct) { 'correct' } else { 'incorrect' }
    Write-StepRecord ([ordered]@{schemaVersion='showwhere-e2e-v1';runId=$runId;timestamp=(Get-Date).ToUniversalTime().ToString('o');question=$Question;settingsUri=$SettingsUri;step=$step+1;expectedLabel=$expectedName;expectedRole=$expectedRole;actualLabel=$actual;actualRole=$actualRole;assistantMessage=$message;overlay=@{x=$overlay.X;y=$overlay.Y;width=$overlay.Width;height=$overlay.Height};targetBounds=$targetBounds;verdict=$verdict})
    $panel = Get-ShowWherePanel
    if ($panel) { Mark-LatestAnswer $panel $correct }
    if (-not $correct) {
        Write-Host "FAIL step $($step+1): expected '$expectedName' [$expectedRole], actual '$actual' [$actualRole]" -ForegroundColor Red
        $casePassed = $false
        break
    }
    Write-Host "PASS step $($step+1): $actual" -ForegroundColor Green
    if ($step -lt $expectedLabelList.Count - 1 -or $ClickFinalTarget) {
        Invoke-ValidatedTarget $target $overlay
    }
}

$panel = Get-ShowWherePanel
Stop-ActiveGuidance $panel
Activate-VsCode
$resultLabel = if ($casePassed) { 'PASS' } else { 'FAIL' }
$resultColor = if ($casePassed) { 'Green' } else { 'Red' }
Write-Host "RESULT $resultLabel -> $resultPath" -ForegroundColor $resultColor
if (-not $casePassed) { exit 2 }
