<#
.SYNOPSIS
    Renne 项目的 Unity 自动化测试 CLI。

.DESCRIPTION
    统一入口，自动判断该走 batchmode（编辑器没开）还是 Editor 通信桥（编辑器开着）。

    子命令：
      run      进入 PlayMode，自动跳过玩家回合，模拟 N 个回合后收尾
      logs     打印最近一次运行的 Console 日志
      errors   只打印最近一次运行的 Error / Exception
      turns    打印每回合的状态快照（任务进度 / 敌人位置 / PlanProgress）
      summary  打印最近一次运行的结果摘要
      status   查看当前状态（编辑器是否开着、桥在干什么）
      compile  只检查脚本能否编译通过
      tests    跑 Unity Test Framework 用例

.EXAMPLE
    .\Tools\autotest.ps1 run -Turns 10
    .\Tools\autotest.ps1 run -Turns 20 -Scene SampleScene -StopOnError
    .\Tools\autotest.ps1 errors
    .\Tools\autotest.ps1 turns -Tail 20
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('run', 'logs', 'errors', 'turns', 'summary', 'status', 'compile', 'tests')]
    [string]$Command = 'status',

    [int]$Turns = 10,
    [float]$Timeout = 180,
    [float]$PlayerDelay = 0.15,
    [switch]$NoAutoPass,
    [switch]$StopOnError,
    [string]$Scene = '',
    [string]$Note = '',

    [ValidateSet('auto', 'batch', 'bridge')]
    [string]$Mode = 'auto',

    [int]$Tail = 120,
    [switch]$All,

    [ValidateSet('EditMode', 'PlayMode')]
    [string]$Platform = 'EditMode',
    [switch]$ProbePaths,
    [switch]$ProbeDialogue,
    [ValidateSet('','zh','en')]
    [string]$Language = '',
    [switch]$WithGraphics,
    [string]$UnityPath = ''
)

$ErrorActionPreference = 'Stop'

# ══════════════════════════════════════════════════════════════════════════════
# 路径
# ══════════════════════════════════════════════════════════════════════════════

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$OutRoot     = Join-Path $ProjectRoot 'Logs\AutoTest'
$CommandFile = Join-Path $OutRoot 'command.json'
$StatusFile  = Join-Path $OutRoot 'status.json'
$LatestDir   = Join-Path $OutRoot 'latest'
$LockFile    = Join-Path $ProjectRoot 'Temp\UnityLockfile'

function Ensure-OutRoot {
    if (-not (Test-Path $OutRoot)) { New-Item -ItemType Directory -Path $OutRoot -Force | Out-Null }
}

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    $enc = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
}

# Start-Process -ArgumentList 会把数组用空格拼起来，但不会给含空格的元素加引号，
# 结果 -autotestNote "带 空格 的备注" 会被 Unity 拆成好几个参数。这里手动补引号。
function Format-UnityArgs([object[]]$Items) {
    $out = @()
    foreach ($i in $Items) {
        $s = [string]$i
        if ($s -match '[\s"]') { $out += '"' + ($s -replace '"', '\"') + '"' }
        else                   { $out += $s }
    }
    return ,$out
}

function Read-JsonFile([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    try {
        $raw = [System.IO.File]::ReadAllText($Path)
        if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
        return $raw | ConvertFrom-Json
    } catch { return $null }
}

# ══════════════════════════════════════════════════════════════════════════════
# Unity 可执行文件定位
# ══════════════════════════════════════════════════════════════════════════════

function Get-ProjectUnityVersion {
    $p = Join-Path $ProjectRoot 'ProjectSettings\ProjectVersion.txt'
    if (-not (Test-Path $p)) { return $null }
    $line = Select-String -Path $p -Pattern '^m_EditorVersion:\s*(.+)$' | Select-Object -First 1
    if ($null -eq $line) { return $null }
    return $line.Matches[0].Groups[1].Value.Trim()
}

function Resolve-UnityExe {
    if ($UnityPath -ne '') {
        if (Test-Path $UnityPath) { return $UnityPath }
        throw "指定的 -UnityPath 不存在：$UnityPath"
    }

    $version = Get-ProjectUnityVersion
    if ($null -eq $version) { throw '读不到 ProjectSettings/ProjectVersion.txt' }

    # 1) Unity Hub 的 editors.json
    $editorsJson = Join-Path $env:APPDATA 'UnityHub\editors.json'
    $editors = Read-JsonFile $editorsJson
    if ($null -ne $editors) {
        $entry = $editors.PSObject.Properties | Where-Object { $_.Name -eq $version } | Select-Object -First 1
        if ($null -ne $entry -and $null -ne $entry.Value.location) {
            foreach ($loc in @($entry.Value.location)) {
                if (Test-Path $loc) { return $loc }
            }
        }
    }

    # 2) 常见安装路径
    $candidates = @(
        "C:\Program Files\Unity $version\Editor\Unity.exe",
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "E:\unity\$version\Editor\Unity.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }

    throw "找不到 Unity $version 的安装位置，请用 -UnityPath 手动指定"
}

# ══════════════════════════════════════════════════════════════════════════════
# 编辑器是否开着
# ══════════════════════════════════════════════════════════════════════════════

function Test-EditorOpen {
    if (-not (Test-Path $LockFile)) { return $false }
    # 锁文件在崩溃后可能残留，再确认一下真的有 Unity 进程
    $procs = @(Get-Process Unity -ErrorAction SilentlyContinue)
    return $procs.Count -gt 0
}

function Resolve-Mode {
    if ($Mode -ne 'auto') { return $Mode }
    if (Test-EditorOpen) { return 'bridge' }
    return 'batch'
}

# ══════════════════════════════════════════════════════════════════════════════
# 结果展示
# ══════════════════════════════════════════════════════════════════════════════

function Get-LatestRunDir {
    $latestTxt = Join-Path $OutRoot 'latest.txt'
    if (Test-Path $latestTxt) {
        $id = ([System.IO.File]::ReadAllText($latestTxt)).Trim()
        $d = Join-Path (Join-Path $OutRoot 'runs') $id
        if (Test-Path $d) { return $d }
    }
    if (Test-Path $LatestDir) { return $LatestDir }
    return $null
}

# 开跑前清掉上一次的结果，避免这次失败时把旧摘要当成本次结果读出来
function Clear-LatestResults {
    foreach ($n in @('summary.json', 'console.log', 'errors.log', 'turns.jsonl')) {
        $p = Join-Path $LatestDir $n
        if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
    }
}

function Show-Summary {
    $sumPath = Join-Path $LatestDir 'summary.json'
    $sum = Read-JsonFile $sumPath
    if ($null -eq $sum) {
        Write-Host '没有找到 summary.json，可能还没跑过或者运行被中断了。' -ForegroundColor Yellow
        return $null
    }

    $verdict = '失败'
    $color   = 'Red'
    if ($sum.passed) { $verdict = '通过'; $color = 'Green' }

    Write-Host ''
    Write-Host "─── 运行结果 [$verdict] ───────────────────────────────" -ForegroundColor $color
    Write-Host ("  runId        : {0}" -f $sum.runId)
    Write-Host ("  结束原因     : {0}" -f $sum.reason)
    Write-Host ("  完成回合     : {0} / {1}" -f $sum.turnsCompleted, $sum.maxTurns)
    Write-Host ("  耗时         : {0:N1}s" -f $sum.elapsedSeconds)
    Write-Host ("  日志         : {0} 条 info, {1} 条 warning" -f $sum.logs, $sum.warnings)
    Write-Host ("  错误         : {0} 个 error, {1} 个 exception" -f $sum.errors, $sum.exceptions) -ForegroundColor $color
    if ($sum.note -ne '') { Write-Host ("  备注         : {0}" -f $sum.note) }

    if ($sum.errorSamples -and @($sum.errorSamples).Count -gt 0) {
        Write-Host ''
        Write-Host '─── 错误明细 ──────────────────────────────────────' -ForegroundColor Red
        $i = 0
        foreach ($e in $sum.errorSamples) {
            $i++
            Write-Host ("  {0,2}. {1}" -f $i, $e) -ForegroundColor Red
        }
    }

    Write-Host ''
    Write-Host '─── 产物 ──────────────────────────────────────────'
    Write-Host ("  完整日志 : {0}" -f (Join-Path $LatestDir 'console.log'))
    Write-Host ("  回合快照 : {0}" -f (Join-Path $LatestDir 'turns.jsonl'))
    Write-Host ("  错误汇总 : {0}" -f (Join-Path $LatestDir 'errors.log'))
    Write-Host ''
    return $sum
}

function Show-File([string]$Path, [string]$Label) {
    if (-not (Test-Path $Path)) {
        Write-Host "$Label 不存在：$Path" -ForegroundColor Yellow
        return
    }
    Write-Host "─── $Label ($Path) ───" -ForegroundColor Cyan
    if ($All) {
        Get-Content $Path
    } else {
        Get-Content $Path -Tail $Tail
    }
}

# ══════════════════════════════════════════════════════════════════════════════
# run —— batchmode
# ══════════════════════════════════════════════════════════════════════════════

function Invoke-BatchRun {
    $unity = Resolve-UnityExe
    Ensure-OutRoot

    $runId  = Get-Date -Format 'yyyyMMdd-HHmmss'
    $runDir = Join-Path (Join-Path $OutRoot 'runs') $runId
    New-Item -ItemType Directory -Path $runDir -Force | Out-Null
    Clear-LatestResults
    $unityLog = Join-Path $runDir 'unity.log'

    # 注意：$args 是 PowerShell 自动变量，这里必须换个名字
    $unityArgs = @(
        '-batchmode',
        '-projectPath', $ProjectRoot,
        '-logFile', $unityLog,
        '-executeMethod', 'RenneAutoTest.AutoTestBatch.RunPlayMode',
        '-autotestRunId', $runId,
        '-autotestTurns', $Turns,
        '-autotestTimeout', $Timeout,
        '-autotestPlayerDelay', $PlayerDelay
    )
    # ⚠ 不要加 -quit：本方法返回后 Unity 要继续活着跑 PlayMode，
    #   退出由 AutoTestDriver 调 EditorApplication.Exit(code) 负责
    if (-not $WithGraphics) { $unityArgs = @('-nographics') + $unityArgs }
    if ($StopOnError)       { $unityArgs += '-autotestStopOnError' }
    if ($NoAutoPass)        { $unityArgs += '-autotestNoAutoPass' }
    if ($ProbePaths)        { $unityArgs += '-autotestProbePaths' }
    if ($ProbeDialogue)     { $unityArgs += '-autotestProbeDialogue' }
    if ($Language -ne '')   { $unityArgs += @('-autotestLanguage', $Language) }
    if ($Scene -ne '')      { $unityArgs += @('-autotestScene', $Scene) }
    if ($Note  -ne '')      { $unityArgs += @('-autotestNote',  $Note) }

    Write-Host "模式 : batchmode（编辑器未打开）" -ForegroundColor Cyan
    Write-Host "Unity: $unity"
    Write-Host "运行 : runId=$runId turns=$Turns timeout=${Timeout}s"
    Write-Host '正在运行，请稍候…'

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $unity -ArgumentList (Format-UnityArgs $unityArgs) -PassThru -Wait -NoNewWindow
    $sw.Stop()

    $code = $proc.ExitCode
    Write-Host ("Unity 退出码 {0}（耗时 {1:N1}s）" -f $code, $sw.Elapsed.TotalSeconds)

    # ⚠ 不能只看退出码：脚本编译失败时 Unity 打印
    #   "Aborting batchmode due to failure: Scripts have compiler errors."
    #   之后仍然以 0 退出，于是整次运行会被误判成成功——跑都没跑起来却报"通过"。
    #   判据改成"有没有产出 summary.json"：那是驱动器真的跑完才会写的。
    if (-not (Test-Path (Join-Path $LatestDir 'summary.json'))) {
        if ($code -eq 0) { $code = 6 }   # 退出码 6 = 编译失败
        Write-Host ''
        Write-Host '没有产出 summary.json —— 本次运行没有真正执行。' -ForegroundColor Red
        Write-Host '多半是脚本编译失败或 Unity 启动失败。' -ForegroundColor Yellow
        Show-CompileErrors $unityLog
        Write-Host "Unity 原始日志：$unityLog"
        return $code
    }

    Show-Summary | Out-Null
    Show-CompileErrors $unityLog
    Write-Host "Unity 原始日志：$unityLog"
    return $code
}

function Show-CompileErrors([string]$UnityLog) {
    if (-not (Test-Path $UnityLog)) { return }
    $hits = @(Select-String -Path $UnityLog -Pattern 'error CS\d+' -ErrorAction SilentlyContinue |
              Select-Object -First 40)
    if ($hits.Count -eq 0) { return }

    Write-Host ''
    Write-Host '─── 编译错误 ──────────────────────────────────────' -ForegroundColor Red
    foreach ($h in $hits) { Write-Host ("  " + $h.Line.Trim()) -ForegroundColor Red }
    Write-Host ''
}

# ══════════════════════════════════════════════════════════════════════════════
# run —— Editor 桥
# ══════════════════════════════════════════════════════════════════════════════

function Send-BridgeCommand([hashtable]$Payload) {
    Ensure-OutRoot
    $seq = 1
    $old = Read-JsonFile $CommandFile
    if ($null -ne $old -and $null -ne $old.seq) { $seq = [int]$old.seq + 1 }

    $Payload['seq'] = $seq
    Write-Utf8NoBom $CommandFile ($Payload | ConvertTo-Json -Depth 4)
    return $seq
}

function Wait-BridgeDone([int]$Seq, [double]$TimeoutSeconds) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $lastState = ''
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        Start-Sleep -Milliseconds 700
        $st = Read-JsonFile $StatusFile
        if ($null -ne $st -and [int]$st.seq -eq $Seq) {
            if ($st.state -ne $lastState) {
                $lastState = $st.state
                Write-Host ("  桥状态：{0}" -f $st.state)
            }
            if ($st.state -eq 'done' -or $st.state -eq 'error') { return $st }
        }
    }
    return $null
}

function Invoke-BridgeRun {
    Write-Host '模式 : Editor 通信桥（检测到 Unity 编辑器正在运行）' -ForegroundColor Cyan
    Write-Host '运行 : ' -NoNewline
    Write-Host "turns=$Turns timeout=${Timeout}s"

    $payload = @{
        action             = 'run'
        turns              = $Turns
        timeout            = $Timeout
        playerDelay        = $PlayerDelay
        autoPassPlayerTurn = (-not $NoAutoPass)
        stopOnError        = [bool]$StopOnError
        scene              = $Scene
        note               = $Note
        language           = $Language
    }
    Clear-LatestResults
    $seq = Send-BridgeCommand $payload
    Write-Host "已提交命令 seq=$seq，等待编辑器接手（最长 0.5s 轮询一次）…"

    $st = Wait-BridgeDone $seq ($Timeout + 120)
    if ($null -eq $st) {
        Write-Host ''
        Write-Host '等待超时。请确认 Unity 编辑器窗口是激活的——编辑器失焦时' -ForegroundColor Yellow
        Write-Host 'EditorApplication.update 会降频甚至暂停，桥可能来不及响应。' -ForegroundColor Yellow
        Write-Host '也可以直接在编辑器里用菜单 Tools > AutoTest > 控制台 手动触发。' -ForegroundColor Yellow
        return 2
    }

    $code = 0
    if ($null -ne $st.exitCode) { $code = [int]$st.exitCode }

    if ($st.reason -eq 'compile_failed') {
        Write-Host ''
        Write-Host '编辑器里有编译错误，已拒绝进入 PlayMode。' -ForegroundColor Red
        Write-Host '请看 Unity Console，或先运行：.\Tools\autotest.ps1 compile' -ForegroundColor Red
        return $code
    }

    Show-Summary | Out-Null
    return $code
}

# ══════════════════════════════════════════════════════════════════════════════
# tests
# ══════════════════════════════════════════════════════════════════════════════

function Invoke-Tests {
    if (Test-EditorOpen) {
        Write-Host 'Unity 编辑器正在运行，batchmode 无法启动（项目被 Temp/UnityLockfile 锁住）。' -ForegroundColor Yellow
        Write-Host '请先关闭编辑器，或者在编辑器里用 Window > General > Test Runner 手动跑。' -ForegroundColor Yellow
        return 7
    }

    $unity = Resolve-UnityExe
    Ensure-OutRoot
    $stamp   = Get-Date -Format 'yyyyMMdd-HHmmss'
    $runDir  = Join-Path (Join-Path $OutRoot 'runs') "tests-$stamp"
    New-Item -ItemType Directory -Path $runDir -Force | Out-Null
    $results = Join-Path $runDir 'results.xml'
    $log     = Join-Path $runDir 'unity.log'

    $unityArgs = @(
        '-batchmode', '-nographics',
        '-projectPath', $ProjectRoot,
        '-runTests',
        '-testPlatform', $Platform,
        '-testResults', $results,
        '-logFile', $log
    )

    Write-Host "跑测试：platform=$Platform"
    $proc = Start-Process -FilePath $unity -ArgumentList (Format-UnityArgs $unityArgs) -PassThru -Wait -NoNewWindow
    Write-Host ("Unity 退出码 {0}" -f $proc.ExitCode)

    if (Test-Path $results) {
        try {
            [xml]$xml = Get-Content $results
            $r = $xml.'test-run'
            Write-Host ''
            Write-Host '─── 测试结果 ──────────────────────────────────────' -ForegroundColor Cyan
            Write-Host ("  总计 {0} | 通过 {1} | 失败 {2} | 跳过 {3}" -f $r.total, $r.passed, $r.failed, $r.skipped)
            if ([int]$r.failed -gt 0) {
                $failed = $xml.SelectNodes("//test-case[@result='Failed']")
                foreach ($f in $failed) {
                    Write-Host ("  ✗ {0}" -f $f.fullname) -ForegroundColor Red
                    if ($f.failure -and $f.failure.message) {
                        Write-Host ("     {0}" -f ($f.failure.message.'#cdata-section' -replace '\s+', ' ')) -ForegroundColor DarkRed
                    }
                }
            }
            Write-Host ("  结果 XML：{0}" -f $results)
        } catch {
            Write-Host "解析结果 XML 失败：$_" -ForegroundColor Yellow
        }
    } else {
        Write-Host '没有生成结果 XML —— 项目里可能一个测试用例都还没有。' -ForegroundColor Yellow
        Show-CompileErrors $log
    }
    Write-Host "Unity 原始日志：$log"
    return $proc.ExitCode
}

# ══════════════════════════════════════════════════════════════════════════════
# compile
# ══════════════════════════════════════════════════════════════════════════════

function Invoke-Compile {
    if (Test-EditorOpen) {
        Write-Host '模式 : Editor 通信桥' -ForegroundColor Cyan
        $seq = Send-BridgeCommand @{ action = 'compile' }
        $st  = Wait-BridgeDone $seq 120
        if ($null -eq $st) { Write-Host '等待超时' -ForegroundColor Yellow; return 2 }
        if ($st.reason -eq 'compile_ok') {
            Write-Host '编译通过 ✓' -ForegroundColor Green
            return 0
        }
        Write-Host '有编译错误 ✗（详见 Unity Console）' -ForegroundColor Red
        return 6
    }

    $unity = Resolve-UnityExe
    Ensure-OutRoot
    $log = Join-Path $OutRoot 'compile.log'
    $unityArgs = @(
        '-batchmode', '-nographics', '-quit',
        '-projectPath', $ProjectRoot,
        '-logFile', $log,
        '-executeMethod', 'RenneAutoTest.AutoTestBatch.CompileOnly'
    )
    Write-Host '模式 : batchmode，正在编译…' -ForegroundColor Cyan
    $proc = Start-Process -FilePath $unity -ArgumentList (Format-UnityArgs $unityArgs) -PassThru -Wait -NoNewWindow

    if ($proc.ExitCode -eq 0) {
        Write-Host '编译通过 ✓' -ForegroundColor Green
    } else {
        Write-Host ("编译失败 ✗（退出码 {0}）" -f $proc.ExitCode) -ForegroundColor Red
        Show-CompileErrors $log
        Write-Host "Unity 原始日志：$log"
    }
    return $proc.ExitCode
}

# ══════════════════════════════════════════════════════════════════════════════
# status
# ══════════════════════════════════════════════════════════════════════════════

function Show-Status {
    $editorOpen = Test-EditorOpen
    Write-Host ''
    Write-Host '─── AutoTest 状态 ─────────────────────────────────' -ForegroundColor Cyan
    Write-Host ("  项目       : {0}" -f $ProjectRoot)
    try   { Write-Host ("  Unity 版本 : {0}" -f (Get-ProjectUnityVersion)) }
    catch { Write-Host '  Unity 版本 : 读取失败' }
    try   { Write-Host ("  Unity 路径 : {0}" -f (Resolve-UnityExe)) }
    catch { Write-Host ("  Unity 路径 : {0}" -f $_.Exception.Message) -ForegroundColor Yellow }

    if ($editorOpen) {
        Write-Host '  编辑器     : 已打开 → run 会走 Editor 通信桥' -ForegroundColor Green
    } else {
        Write-Host '  编辑器     : 未打开 → run 会走 batchmode'
    }

    $st = Read-JsonFile $StatusFile
    if ($null -ne $st) {
        Write-Host ("  最近命令   : action={0} seq={1} state={2} @ {3}" -f $st.action, $st.seq, $st.state, $st.updatedAt)
        if ($null -ne $st.compileFailed -and $st.compileFailed) {
            Write-Host '  ⚠ 编辑器里存在编译错误' -ForegroundColor Red
        }
    } else {
        Write-Host '  最近命令   : （还没有运行记录）'
    }
    Write-Host ("  输出目录   : {0}" -f $OutRoot)
    Write-Host ''
}

# ══════════════════════════════════════════════════════════════════════════════
# 主分发
# ══════════════════════════════════════════════════════════════════════════════

$exit = 0
switch ($Command) {
    'run' {
        $m = Resolve-Mode
        if ($m -eq 'bridge') { $exit = Invoke-BridgeRun }
        else                 { $exit = Invoke-BatchRun }
    }
    'logs'    { Show-File (Join-Path $LatestDir 'console.log') 'Console 日志' }
    'errors'  { Show-File (Join-Path $LatestDir 'errors.log')  '错误汇总' }
    'turns'   { Show-File (Join-Path $LatestDir 'turns.jsonl') '回合快照' }
    'summary' { Show-Summary | Out-Null }
    'status'  { Show-Status }
    'compile' { $exit = Invoke-Compile }
    'tests'   { $exit = Invoke-Tests }
}

exit $exit
