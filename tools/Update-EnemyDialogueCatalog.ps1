param([switch]$Generate)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$dialogueRoot = Join-Path $projectRoot 'Assets/Resources/Dialogues'
$source = Get-Content (Join-Path $PSScriptRoot 'dialogue_expansion_20261015.json') -Raw | ConvertFrom-Json
$utf8 = [Text.UTF8Encoding]::new($false)
$missions = @{}
foreach ($mission in $source.missions) { $missions[$mission.id] = $mission }
if ($Generate) {
    foreach ($mission in $source.missions) {
        foreach ($kind in @('start','success','fail','mutter','detail')) {
            $ambient = $kind -in @('mutter','detail')
            $id = 'mission_' + $mission.id + '_' + $kind
            $lines = @()
            for ($i=0; $i -lt $mission.$kind.Count; $i++) {
                $lines += [ordered]@{ speaker=$mission.actors[$i % $mission.actors.Count]; text=$mission.$kind[$i] }
            }
            $data = [ordered]@{
                id=$id; trigger=$(if($ambient){'ambient'}else{'manual'}); topic=$mission.title
                priority=$(if($ambient){0}else{100}); weight=$(if($ambient){3}else{0})
                state=$(if($mission.id -eq 'alert_search_02'){'any'}else{'idle'})
                mode=$(if($ambient){'turn'}else{'realtime'}); linesPerTurn=0; secondsPerLine=2.5
                clearRadius=6; keywordRadius=10; audibleRadius=15; maxSpeakerDistance=5
                alertsOthers=$false; repeatable=$true; cooldownTurns=$(if($ambient){8}else{0})
                speakers=@($mission.actors); lines=$lines
            }
            if($ambient) { $data.missionIds=@($mission.id) }
            $path=Join-Path $dialogueRoot ($id+'.json')
            [IO.File]::WriteAllText($path,($data | ConvertTo-Json -Depth 20)+"`n",$utf8)
            if(-not (Test-Path ($path+'.meta'))) {
                [IO.File]::WriteAllText(($path+'.meta'),"fileFormatVersion: 2`nguid: $([Guid]::NewGuid().ToString('N'))`n",$utf8)
            }
        }
    }
    foreach($asset in Get-ChildItem (Join-Path $projectRoot 'Assets/Data/Mission') -Filter '*.asset') {
        $text=[IO.File]::ReadAllText($asset.FullName)
        $match=[regex]::Match($text,'(?m)^  missionId: (\S+)\s*$')
        if(-not $match.Success -or -not $missions.ContainsKey($match.Groups[1].Value)) {continue}
        $id=$match.Groups[1].Value
        foreach($pair in @(@('dialogueOnStart','start'),@('dialogueOnSuccess','success'),@('dialogueOnFail','fail'))) {
            $pattern='(?m)^  '+$pair[0]+':[^\r\n]*'
            $replacement='  '+$pair[0]+': mission_'+$id+'_'+$pair[1]
            $text=[regex]::Replace($text,$pattern,$replacement)
        }
        [IO.File]::WriteAllText($asset.FullName,$text,$utf8)
    }
}

$names=@{}
$speakerData=Get-Content (Join-Path $projectRoot 'Assets/Resources/DialogueSpeakers.json') -Raw | ConvertFrom-Json
foreach($speaker in $speakerData.speakers){$names[$speaker.id]=$speaker.displayName}
$records=@()
foreach($file in Get-ChildItem $dialogueRoot -Filter '*.json' | Sort-Object Name) {
    $data=Get-Content $file.FullName -Raw | ConvertFrom-Json
    $new=$file.BaseName.StartsWith('mission_')
    $category=if($new){'本次新增'}elseif($file.BaseName.StartsWith('_')){'系统/测试'}else{'已有'}
    $records += [ordered]@{category=$category;path=('Assets/Resources/Dialogues/'+$file.Name);data=$data}
}
$builder=[Text.StringBuilder]::new()
[void]$builder.AppendLine('# EnemyAI 对白完整记录')
[void]$builder.AppendLine('')
[void]$builder.AppendLine('记录日期：2026-10-15。基准语言：中文。新增 45 段、90 句，覆盖 9 个有效任务 ID；已有中文对白也在下方完整收录。')
[void]$builder.AppendLine('')
[void]$builder.AppendLine('## 接入与触发规则')
[void]$builder.AppendLine('')
[void]$builder.AppendLine('- 每个任务配置开始、成功、失败三段 realtime 演出（priority=100），通过 MissionManager 原有接口触发、抢占低优先级闲聊或排队。每句间隔 2.5 秒。')
[void]$builder.AppendLine('- 每个任务另有两段执行中闲聊（ambient，weight=3，priority=0，冷却 8 个全局回合）。参与者必须实际执行 missionIds 中的任务；切换任务会停止该段闲聊。')
[void]$builder.AppendLine('- 双人任务闲聊要求双方同层且相距不超过 5 米；任务闲聊不能在战斗中开启，搜索任务允许非战斗警觉状态。原有无 missionIds 的对白保留原触发规则。')
[void]$builder.AppendLine('- 新增对白不写入情报旗标，也不改变任务判定。任务演出仍受角色是否在场、对白占用和队列过期限制；不会让已移除的角色开口。')
[void]$builder.AppendLine('- 新增内容为中文。英文资源目录未覆盖的内容沿用当前语言回退机制。`ChargeActivate.asset` 与 `TakeCare 1.asset` 共享 objectActivate_01，使用同一组台词；空 ID 的 Object.asset 是未配置模板，本次未绑定。')
[void]$builder.AppendLine('- JSON 中的 {E01} 等占位符由游戏替换为名字；本记录保留原文。模糊档、关键词、内心独白和旗标均逐句记录，没有配置时不额外添加。')
[void]$builder.AppendLine('')
[void]$builder.AppendLine('## 新增任务索引')
[void]$builder.AppendLine('')
[void]$builder.AppendLine('| 任务 ID | 用途 | 角色 | 对白 ID 前缀 |')
[void]$builder.AppendLine('| --- | --- | --- | --- |')
foreach($mission in $source.missions) {
    $actors=($mission.actors | ForEach-Object {$names[$_]+'（'+$_+'）'}) -join '、'
    [void]$builder.AppendLine("| $($mission.id) | $($mission.title) | $actors | mission_$($mission.id)_ |")
}
[void]$builder.AppendLine('')
[void]$builder.AppendLine('后缀：start=开始，success=成功，fail=失败，mutter/detail=执行中两种闲聊。MP_Phase01 任务池引用的九项均已绑定，原有成功/失败条件保持不变。')
foreach($category in @('本次新增','已有','系统/测试')) {
    [void]$builder.AppendLine("`n## $category`n")
    foreach($record in $records | Where-Object {$_.category -eq $category}) {
        $d=$record.data
        [void]$builder.AppendLine("### $($d.id)`n")
        [void]$builder.AppendLine("- 文件：$($record.path)")
        [void]$builder.AppendLine("- 话题：$($d.topic)；触发：$($d.trigger)；状态：$($d.state)；模式：$($d.mode)")
        [void]$builder.AppendLine("- 角色：$($d.speakers -join '、')；优先级：$($d.priority)；权重：$($d.weight)；可重复：$($d.repeatable)；冷却：$($d.cooldownTurns)")
        [void]$builder.AppendLine("- 距离（清晰／关键词／可闻）：$($d.clearRadius)／$($d.keywordRadius)／$($d.audibleRadius) 米；对话双方距离：$($d.maxSpeakerDistance) 米")
        if($d.missionIds){[void]$builder.AppendLine("- 实际执行任务限定：$($d.missionIds -join '、')")}
        if($d.conditions){[void]$builder.AppendLine('- 条件：'+($d.conditions | ConvertTo-Json -Depth 15 -Compress))}
        [void]$builder.AppendLine('')
        $n=0
        foreach($line in $d.lines) {
            $n++
            $who=if($names.ContainsKey($line.speaker)){$names[$line.speaker]+'（'+$line.speaker+'）'}else{$line.speaker}
            [void]$builder.AppendLine("$n. **$who**：$($line.text)")
            foreach($key in @('blurred','keyword','hint','flag','nameOf')) {
                if($line.$key){[void]$builder.AppendLine("   - ${key}：$($line.$key)")}
            }
        }
        [void]$builder.AppendLine('')
    }
}
[void]$builder.AppendLine('## 其他语言文件')
[void]$builder.AppendLine('')
foreach($file in Get-ChildItem (Join-Path $dialogueRoot 'en') -Filter '*.json' -ErrorAction SilentlyContinue) {
    [void]$builder.AppendLine("### en/$($file.BaseName)`n")
    $data=Get-Content $file.FullName -Raw | ConvertFrom-Json
    foreach($line in $data.lines){[void]$builder.AppendLine("- $($line.speaker)：$($line.text)")}
    [void]$builder.AppendLine('')
}
[IO.File]::WriteAllText((Join-Path $projectRoot 'docs/EnemyAI对白完整记录.md'),$builder.ToString(),$utf8)
[IO.File]::WriteAllText((Join-Path $projectRoot 'docs/enemy_dialogue_catalog.json'),($records | ConvertTo-Json -Depth 30)+"`n",$utf8)
$newRecords=@($records | Where-Object {$_.category -eq '本次新增'})
$newLines=($newRecords | ForEach-Object {$_.data.lines.Count} | Measure-Object -Sum).Sum
Write-Output "Catalog: $($records.Count) base-language dialogues; added $($newRecords.Count) dialogues / $newLines lines."
