# EnemyAI 对白完整记录

记录日期：2026-10-15。基准语言：中文。新增 45 段、90 句，覆盖 9 个有效任务 ID；已有中文对白也在下方完整收录。

## 接入与触发规则

- 每个任务配置开始、成功、失败三段 realtime 演出（priority=100），通过 MissionManager 原有接口触发、抢占低优先级闲聊或排队。每句间隔 2.5 秒。
- 每个任务另有两段执行中闲聊（ambient，weight=3，priority=0，冷却 8 个全局回合）。参与者必须实际执行 missionIds 中的任务；切换任务会停止该段闲聊。
- 双人任务闲聊要求双方同层且相距不超过 5 米；任务闲聊不能在战斗中开启，搜索任务允许非战斗警觉状态。原有无 missionIds 的对白保留原触发规则。
- 新增对白不写入情报旗标，也不改变任务判定。任务演出仍受角色是否在场、对白占用和队列过期限制；不会让已移除的角色开口。
- 新增内容为中文。英文资源目录未覆盖的内容沿用当前语言回退机制。`ChargeActivate.asset` 与 `TakeCare 1.asset` 共享 objectActivate_01，使用同一组台词；空 ID 的 Object.asset 是未配置模板，本次未绑定。
- JSON 中的 {E01} 等占位符由游戏替换为名字；本记录保留原文。模糊档、关键词、内心独白和旗标均逐句记录，没有配置时不额外添加。

## 新增任务索引

| 任务 ID | 用途 | 角色 | 对白 ID 前缀 |
| --- | --- | --- | --- |
| patrol_test_01 | 酒水区单人巡逻 | 小刘（E02） | mission_patrol_test_01_ |
| patrol_test_02 | 水果区单人巡逻 | 老王（E01） | mission_patrol_test_02_ |
| patrol_test_03 | 水果区双人复查 | 老王（E01）、小刘（E02） | mission_patrol_test_03_ |
| patrol_test_04 | 第二水果区接续巡逻 | 老王（E01）、小刘（E02） | mission_patrol_test_04_ |
| patrol_test_05 | 第二水果区持续巡查 | 老王（E01）、小刘（E02） | mission_patrol_test_05_ |
| alert_search_02 | 走廊搜索 | 阿强（E03）、大陈（E04） | mission_alert_search_02_ |
| defend_zone_01 | 仓储区守卫 | 阿强（E03）、大陈（E04） | mission_defend_zone_01_ |
| objectActivate_01 | 启动目标设备 3004 | 阿强（E03） | mission_objectActivate_01_ |
| objectdefend_01 | 设备 3004 看护与巡逻 | 阿强（E03）、大陈（E04） | mission_objectdefend_01_ |

后缀：start=开始，success=成功，fail=失败，mutter/detail=执行中两种闲聊。MP_Phase01 任务池引用的九项均已绑定，原有成功/失败条件保持不变。

## 本次新增

### mission_alert_search_02_detail

- 文件：Assets/Resources/Dialogues/mission_alert_search_02_detail.json
- 话题：走廊搜索；触发：ambient；状态：any；模式：turn
- 角色：E03、E04；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：alert_search_02

1. **阿强（E03）**：门后是空的。你那边呢？
2. **大陈（E04）**：还在看。别急着说没人，转角那边我还没查。

### mission_alert_search_02_fail

- 文件：Assets/Resources/Dialogues/mission_alert_search_02_fail.json
- 话题：走廊搜索；触发：manual；状态：any；模式：realtime
- 角色：E03、E04；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：搜了这一阵，还是没摸着可靠的线索。
2. **大陈（E04）**：别再围着同一个角落转，把漏掉的地方补上。

### mission_alert_search_02_mutter

- 文件：Assets/Resources/Dialogues/mission_alert_search_02_mutter.json
- 话题：走廊搜索；触发：ambient；状态：any；模式：turn
- 角色：E03、E04；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：alert_search_02

1. **阿强（E03）**：刚才那声像是从这一头传过来的。
2. **大陈（E04）**：回声也能绕人。先看地上有没有新痕迹。

### mission_alert_search_02_start

- 文件：Assets/Resources/Dialogues/mission_alert_search_02_start.json
- 话题：走廊搜索；触发：manual；状态：any；模式：realtime
- 角色：E03、E04；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：走廊那边得重新搜，别光朝有灯的地方看。
2. **大陈（E04）**：你看门后，我盯转角。先别把人堵在一条线上。

### mission_alert_search_02_success

- 文件：Assets/Resources/Dialogues/mission_alert_search_02_success.json
- 话题：走廊搜索；触发：manual；状态：any；模式：realtime
- 角色：E03、E04；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：目标已经倒下，先确认周围还有没有别人。
2. **大陈（E04）**：别急着散开，把走廊再看一遍。

### mission_defend_zone_01_detail

- 文件：Assets/Resources/Dialogues/mission_defend_zone_01_detail.json
- 话题：仓储区守卫；触发：ambient；状态：idle；模式：turn
- 角色：E03、E04；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：defend_zone_01

1. **阿强（E03）**：箱子后面别站人，我从这儿看不到那一角。
2. **大陈（E04）**：知道。我往旁边挪，你把通道留出来。

### mission_defend_zone_01_fail

- 文件：Assets/Resources/Dialogues/mission_defend_zone_01_fail.json
- 话题：仓储区守卫；触发：manual；状态：idle；模式：realtime
- 角色：E03、E04；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：仓储区守不住了，先把情况报清楚！
2. **大陈（E04）**：别再说一切正常了，先确认还有谁能应声。

### mission_defend_zone_01_mutter

- 文件：Assets/Resources/Dialogues/mission_defend_zone_01_mutter.json
- 话题：仓储区守卫；触发：ambient；状态：idle；模式：turn
- 角色：E03、E04；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：defend_zone_01

1. **阿强（E03）**：外面刚才有声音，要不要过去看看？
2. **大陈（E04）**：先别动。咱们一走，这里就没人了。

### mission_defend_zone_01_start

- 文件：Assets/Resources/Dialogues/mission_defend_zone_01_start.json
- 话题：仓储区守卫；触发：manual；状态：idle；模式：realtime
- 角色：E03、E04；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：仓储区这边守住，别一有响动就都往外跑。
2. **大陈（E04）**：我看里侧。你要挪位置，先跟我说一声。

### mission_defend_zone_01_success

- 文件：Assets/Resources/Dialogues/mission_defend_zone_01_success.json
- 话题：仓储区守卫；触发：manual；状态：idle；模式：realtime
- 角色：E03、E04；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：这段时间仓储区没丢，里外也都接得上。
2. **大陈（E04）**：交接前再看一眼，别最后一会儿出事。

### mission_objectActivate_01_detail

- 文件：Assets/Resources/Dialogues/mission_objectActivate_01_detail.json
- 话题：启动目标设备 3004；触发：ambient；状态：idle；模式：turn
- 角色：E03；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：objectActivate_01

1. **阿强（E03）**：这动静不太对……先别急着下结论。
2. **阿强（E03）**：等我把该看的地方都确认完。

### mission_objectActivate_01_fail

- 文件：Assets/Resources/Dialogues/mission_objectActivate_01_fail.json
- 话题：启动目标设备 3004；触发：manual；状态：idle；模式：realtime
- 角色：E03；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：设备这边没办成，先别当它已经开了。
2. **阿强（E03）**：得重新确认一下，不能只看那盏灯。

### mission_objectActivate_01_mutter

- 文件：Assets/Resources/Dialogues/mission_objectActivate_01_mutter.json
- 话题：启动目标设备 3004；触发：ambient；状态：idle；模式：turn
- 角色：E03；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：objectActivate_01

1. **阿强（E03）**：每次都让我来开，出了毛病倒是谁都不认。
2. **阿强（E03）**：先看一眼再动手，免得回头又说我乱碰。

### mission_objectActivate_01_start

- 文件：Assets/Resources/Dialogues/mission_objectActivate_01_start.json
- 话题：启动目标设备 3004；触发：manual；状态：idle；模式：realtime
- 角色：E03；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：我去把那台设备开起来，先别碰它旁边的东西。
2. **阿强（E03）**：开关老是发涩，别又卡在半截。

### mission_objectActivate_01_success

- 文件：Assets/Resources/Dialogues/mission_objectActivate_01_success.json
- 话题：启动目标设备 3004；触发：manual；状态：idle；模式：realtime
- 角色：E03；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：设备已经启动了，声音听着还算平稳。
2. **阿强（E03）**：先让它转着，有异常再叫我。

### mission_objectdefend_01_detail

- 文件：Assets/Resources/Dialogues/mission_objectdefend_01_detail.json
- 话题：设备 3004 看护与巡逻；触发：ambient；状态：idle；模式：turn
- 角色：E03、E04；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：objectdefend_01

1. **阿强（E03）**：你离那台东西远一点，挡着我看外侧了。
2. **大陈（E04）**：行。我沿边上巡，你听见声音变了就叫我。

### mission_objectdefend_01_fail

- 文件：Assets/Resources/Dialogues/mission_objectdefend_01_fail.json
- 话题：设备 3004 看护与巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E03、E04；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：设备看护出了问题，先确认是哪一处断了。
2. **大陈（E04）**：我盯着周围。你别一边修，一边把背后空着。

### mission_objectdefend_01_mutter

- 文件：Assets/Resources/Dialogues/mission_objectdefend_01_mutter.json
- 话题：设备 3004 看护与巡逻；触发：ambient；状态：idle；模式：turn
- 角色：E03、E04；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：objectdefend_01

1. **阿强（E03）**：刚才转得好好的，别又有人碰了什么。
2. **大陈（E04）**：我先看周围。真有问题，也别两个人都扑上去。

### mission_objectdefend_01_start

- 文件：Assets/Resources/Dialogues/mission_objectdefend_01_start.json
- 话题：设备 3004 看护与巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E03、E04；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：设备转起来以后更得看着，别让人靠过去乱碰。
2. **大陈（E04）**：你巡里面，我照看外侧。有问题先喊一声。

### mission_objectdefend_01_success

- 文件：Assets/Resources/Dialogues/mission_objectdefend_01_success.json
- 话题：设备 3004 看护与巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E03、E04；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：设备这阵子一直在转，没再断过。
2. **大陈（E04）**：交接时把情况说清楚，别换个人就又从头折腾。

### mission_patrol_test_01_detail

- 文件：Assets/Resources/Dialogues/mission_patrol_test_01_detail.json
- 话题：酒水区单人巡逻；触发：ambient；状态：idle；模式：turn
- 角色：E02；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：patrol_test_01

1. **小刘（E02）**：货架后头最容易落东西，光看过道有什么用。
2. **小刘（E02）**：唉，蹲一趟起来，膝盖都响。

### mission_patrol_test_01_fail

- 文件：Assets/Resources/Dialogues/mission_patrol_test_01_fail.json
- 话题：酒水区单人巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：酒水区还没查完，这边已经不对劲了。
2. **小刘（E02）**：先别放人进来，少了什么等会儿再数。

### mission_patrol_test_01_mutter

- 文件：Assets/Resources/Dialogues/mission_patrol_test_01_mutter.json
- 话题：酒水区单人巡逻；触发：ambient；状态：idle；模式：turn
- 角色：E02；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：patrol_test_01

1. **小刘（E02）**：这瓶口还是湿的。谁刚在值班时喝过？
2. **小刘（E02）**：别又算在我头上，我今天可一直醒着。

### mission_patrol_test_01_start

- 文件：Assets/Resources/Dialogues/mission_patrol_test_01_start.json
- 话题：酒水区单人巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：酒水区交给我。我先把货架后面转一遍。
2. **小刘（E02）**：老王说少碰那些瓶子……我可没那么馋。

### mission_patrol_test_01_success

- 文件：Assets/Resources/Dialogues/mission_patrol_test_01_success.json
- 话题：酒水区单人巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：这边看过了，货架和过道都没什么异样。
2. **小刘（E02）**：地上那几个空瓶子，等清洁的人来收吧。

### mission_patrol_test_02_detail

- 文件：Assets/Resources/Dialogues/mission_patrol_test_02_detail.json
- 话题：水果区单人巡逻；触发：ambient；状态：idle；模式：turn
- 角色：E01；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：patrol_test_02

1. **老王（E01）**：巡逻单上只写了水果区，连个具体位置都懒得标。
2. **老王（E01）**：我多转半圈，总比回头挨问强。

### mission_patrol_test_02_fail

- 文件：Assets/Resources/Dialogues/mission_patrol_test_02_fail.json
- 话题：水果区单人巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E01；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：水果区这边出了岔子，先别急着交班。
2. **老王（E01）**：少一箱货还能查，混进个人就麻烦了。

### mission_patrol_test_02_mutter

- 文件：Assets/Resources/Dialogues/mission_patrol_test_02_mutter.json
- 话题：水果区单人巡逻；触发：ambient；状态：idle；模式：turn
- 角色：E01；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：patrol_test_02

1. **老王（E01）**：这味道……也不知道是水果烂了，还是排水又堵了。
2. **老王（E01）**：回头得让人把墙边那摊水擦了。

### mission_patrol_test_02_start

- 文件：Assets/Resources/Dialogues/mission_patrol_test_02_start.json
- 话题：水果区单人巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E01；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：我去水果区走一圈，别把后面的过道堵上。
2. **老王（E01）**：东西堆得这么高，进个人都不一定看得见。

### mission_patrol_test_02_success

- 文件：Assets/Resources/Dialogues/mission_patrol_test_02_success.json
- 话题：水果区单人巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E01；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：这一圈走完了，暂时没见着生面孔。
2. **老王（E01）**：烂掉的那箱还在，味道倒是一天比一天冲。

### mission_patrol_test_03_detail

- 文件：Assets/Resources/Dialogues/mission_patrol_test_03_detail.json
- 话题：水果区双人复查；触发：ambient；状态：idle；模式：turn
- 角色：E01、E02；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：patrol_test_03

1. **老王（E01）**：{E01}，你听见没有，里面像是碰了一下。
2. **小刘（E02）**：先看清楚。别一听响，就把整排货都翻了。

### mission_patrol_test_03_fail

- 文件：Assets/Resources/Dialogues/mission_patrol_test_03_fail.json
- 话题：水果区双人复查；触发：manual；状态：idle；模式：realtime
- 角色：E01、E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：刚才那一段没接上，不能算查完。
2. **小刘（E02）**：别各走各的，先把漏下的位置对清楚。

### mission_patrol_test_03_mutter

- 文件：Assets/Resources/Dialogues/mission_patrol_test_03_mutter.json
- 话题：水果区双人复查；触发：ambient；状态：idle；模式：turn
- 角色：E01、E02；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：patrol_test_03

1. **老王（E01）**：你刚才查过这一排没有？
2. **小刘（E02）**：查过。你别一转身又把我那边重走一遍。

### mission_patrol_test_03_start

- 文件：Assets/Resources/Dialogues/mission_patrol_test_03_start.json
- 话题：水果区双人复查；触发：manual；状态：idle；模式：realtime
- 角色：E01、E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：{E02}，跟我把水果区再过一遍。
2. **小刘（E02）**：知道了。我看货架后面，你看通道。

### mission_patrol_test_03_success

- 文件：Assets/Resources/Dialogues/mission_patrol_test_03_success.json
- 话题：水果区双人复查；触发：manual；状态：idle；模式：realtime
- 角色：E01、E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：前后都看过了，没发现新动静。
2. **小刘（E02）**：记上吧，省得又说咱们只在门口站着。

### mission_patrol_test_04_detail

- 文件：Assets/Resources/Dialogues/mission_patrol_test_04_detail.json
- 话题：第二水果区接续巡逻；触发：ambient；状态：idle；模式：turn
- 角色：E01、E02；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：patrol_test_04

1. **老王（E01）**：两片地方长得一个样，转多了真会记岔。
2. **小刘（E02）**：记人别光记货架。有人来过，东西未必会动。

### mission_patrol_test_04_fail

- 文件：Assets/Resources/Dialogues/mission_patrol_test_04_fail.json
- 话题：第二水果区接续巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E01、E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：这片还没核完，先别在本子上打勾。
2. **小刘（E02）**：知道了，我从刚才停下的地方接着看。

### mission_patrol_test_04_mutter

- 文件：Assets/Resources/Dialogues/mission_patrol_test_04_mutter.json
- 话题：第二水果区接续巡逻；触发：ambient；状态：idle；模式：turn
- 角色：E01、E02；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：patrol_test_04

1. **老王（E01）**：这一片怎么比刚才那边还暗？
2. **小刘（E02）**：灯坏了几天了。你脚下慢点，别踢着箱子。

### mission_patrol_test_04_start

- 文件：Assets/Resources/Dialogues/mission_patrol_test_04_start.json
- 话题：第二水果区接续巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E01、E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：这边查完了，接着去另一片水果区。
2. **小刘（E02）**：我跟着。上回漏看的边角，这次也算上。

### mission_patrol_test_04_success

- 文件：Assets/Resources/Dialogues/mission_patrol_test_04_success.json
- 话题：第二水果区接续巡逻；触发：manual；状态：idle；模式：realtime
- 角色：E01、E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：这片也走过了，能交差了吧？
2. **小刘（E02）**：先记清楚是哪一片，别又把两处写成一处。

### mission_patrol_test_05_detail

- 文件：Assets/Resources/Dialogues/mission_patrol_test_05_detail.json
- 话题：第二水果区持续巡查；触发：ambient；状态：idle；模式：turn
- 角色：E01、E02；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：patrol_test_05

1. **老王（E01）**：你别老跟着我走同一边，另一头会空出来。
2. **小刘（E02）**：知道，我去外侧。听见我喊再靠过来。

### mission_patrol_test_05_fail

- 文件：Assets/Resources/Dialogues/mission_patrol_test_05_fail.json
- 话题：第二水果区持续巡查；触发：manual；状态：idle；模式：realtime
- 角色：E01、E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：这回巡查没接住，时间全耗在别的动静上了。
2. **小刘（E02）**：先把眼前这段稳住，别再漏第二次。

### mission_patrol_test_05_mutter

- 文件：Assets/Resources/Dialogues/mission_patrol_test_05_mutter.json
- 话题：第二水果区持续巡查；触发：ambient；状态：idle；模式：turn
- 角色：E01、E02；优先级：0；权重：3；可重复：True；冷却：8
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 实际执行任务限定：patrol_test_05

1. **老王（E01）**：又绕回这儿了，我连地上哪块砖松都记住了。
2. **小刘（E02）**：记住就好，真多出点什么，你也该看得出来。

### mission_patrol_test_05_start

- 文件：Assets/Resources/Dialogues/mission_patrol_test_05_start.json
- 话题：第二水果区持续巡查；触发：manual；状态：idle；模式：realtime
- 角色：E01、E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：这片还得继续巡，别以为走过一遍就算完。
2. **小刘（E02）**：行，我把外侧的通道也带上。

### mission_patrol_test_05_success

- 文件：Assets/Resources/Dialogues/mission_patrol_test_05_success.json
- 话题：第二水果区持续巡查；触发：manual；状态：idle；模式：realtime
- 角色：E01、E02；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：这阵子一直在转，没见谁往里面钻。
2. **小刘（E02）**：那就先按平常的路线走，别一下全松了。


## 已有

### alert_generic_hide

- 文件：Assets/Resources/Dialogues/alert_generic_hide.json
- 话题：警觉；触发：ambient；状态：alerted；模式：turn
- 角色：A；优先级：；权重：1；可重复：True；冷却：3
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **A**：有人吗？……别躲了。

### alert_generic_sound

- 文件：Assets/Resources/Dialogues/alert_generic_sound.json
- 话题：警觉；触发：ambient；状态：alerted；模式：turn
- 角色：A；优先级：；权重：1；可重复：True；冷却：3
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **A**：什么声音？

### e01_alert_bluff

- 文件：Assets/Resources/Dialogues/e01_alert_bluff.json
- 话题：警觉；触发：ambient；状态：alerted；模式：turn
- 角色：E01；优先级：；权重：1；可重复：True；冷却：3
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：出来吧，我看见你了。

### e01_alert_ears

- 文件：Assets/Resources/Dialogues/e01_alert_ears.json
- 话题：警觉；触发：ambient；状态：alerted；模式：turn
- 角色：E01；优先级：；权重：1；可重复：True；冷却：3
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：谁？……老了，耳朵也不中用了。

### e01_mutter_back

- 文件：Assets/Resources/Dialogues/e01_mutter_back.json
- 话题：抱怨；触发：ambient；状态：idle；模式：turn
- 角色：E01；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：哎哟，这腰……
2. **老王（E01）**：早晚得断在这儿。

### e01_mutter_retire

- 文件：Assets/Resources/Dialogues/e01_mutter_retire.json
- 话题：抱怨；触发：ambient；状态：idle；模式：turn
- 角色：E01；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：明年就退了。
2. **老王（E01）**：退了就清净了。

### e01_mutter_smell

- 文件：Assets/Resources/Dialogues/e01_mutter_smell.json
- 话题：地下室；触发：ambient；状态：idle；模式：turn
- 角色：E01；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：地下室那股味儿，洗三遍澡都洗不掉。
   - keyword：地下室
   - hint：什么味儿，会洗不掉？
   - flag：intel_basement_smell

### e01_mutter_thirty

- 文件：Assets/Resources/Dialogues/e01_mutter_thirty.json
- 话题：抱怨；触发：ambient；状态：idle；模式：turn
- 角色：E01；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：三十年了，还在这儿看大门。
2. **老王（E01）**：图什么呢。

### e01_mutter_xiaoliu

- 文件：Assets/Resources/Dialogues/e01_mutter_xiaoliu.json
- 话题：同事；触发：ambient；状态：idle；模式：turn
- 角色：E01；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：{E02}那孩子，最近魂不守舍的。
   - keyword：{E02}

### e01_mutter_zhou

- 文件：Assets/Resources/Dialogues/e01_mutter_zhou.json
- 话题：老周；触发：ambient；状态：idle；模式：turn
- 角色：E01；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：老周那事儿……谁也别再提了。
   - keyword：老周
   - hint：老周是谁？为什么不能提？
   - flag：intel_zhou

### e02_alert_cat

- 文件：Assets/Resources/Dialogues/e02_alert_cat.json
- 话题：警觉；触发：ambient；状态：alerted；模式：turn
- 角色：E02；优先级：；权重：1；可重复：True；冷却：3
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：是猫吧……肯定是猫。

### e02_alert_scared

- 文件：Assets/Resources/Dialogues/e02_alert_scared.json
- 话题：警觉；触发：ambient；状态：alerted；模式：turn
- 角色：E02；优先级：；权重：1；可重复：True；冷却：3
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：别、别吓我啊……出来！

### e02_alert_who

- 文件：Assets/Resources/Dialogues/e02_alert_who.json
- 话题：警觉；触发：ambient；状态：alerted；模式：turn
- 角色：E02；优先级：；权重：1；可重复：True；冷却：3
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：嗯？……谁在那儿？

### e02_mutter_downstairs

- 文件：Assets/Resources/Dialogues/e02_mutter_downstairs.json
- 话题：地下；触发：ambient；状态：idle；模式：turn
- 角色：E02；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：下面的事，跟你没关系。
   - keyword：下面
   - hint：下面……是说楼下，还是更下面？
2. **小刘（E02）**：……别想了，别想了。

### e02_mutter_key

- 文件：Assets/Resources/Dialogues/e02_mutter_key.json
- 话题：钥匙；触发：ambient；状态：idle；模式：turn
- 角色：E02；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：钥匙到底还回来没有……老张也真是。
   - keyword：钥匙
   - hint：什么钥匙，让他这么放不下？
   - flag：intel_key_mentioned

### e02_mutter_legs

- 文件：Assets/Resources/Dialogues/e02_mutter_legs.json
- 话题：抱怨；触发：ambient；状态：idle；模式：turn
- 角色：E02；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：腿都站麻了。
2. **小刘（E02）**：再熬几个钟头就好了。

### e02_mutter_radio

- 文件：Assets/Resources/Dialogues/e02_mutter_radio.json
- 话题：对讲机；触发：ambient；状态：idle；模式：turn
- 角色：E02；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：{E01}，你那边正常吧？……喂？
   - keyword：{E01}
2. **小刘（E02）**：这破对讲机，又没信号。

### e02_mutter_title

- 文件：Assets/Resources/Dialogues/e02_mutter_title.json
- 话题：抱怨；触发：ambient；状态：idle；模式：turn
- 角色：E02；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：刘队刘队，叫得倒是好听……
   - keyword：刘队
   - nameOf：E02
2. **小刘（E02）**：活儿全是我的。

### e02_mutter_twoclock

- 文件：Assets/Resources/Dialogues/e02_mutter_twoclock.json
- 话题：换班；触发：ambient；状态：idle；模式：turn
- 角色：E02；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **小刘（E02）**：两点……两点。可千万别睡过去。
   - keyword：两点
   - hint：两点有什么事，值得他这么惦记？
   - flag：intel_two_oclock

### e03_alert_lazy

- 文件：Assets/Resources/Dialogues/e03_alert_lazy.json
- 话题：警觉；触发：ambient；状态：alerted；模式：turn
- 角色：E03；优先级：；权重：1；可重复：True；冷却：3
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：谁啊……让我歇会儿行不行。

### e03_alert_rat

- 文件：Assets/Resources/Dialogues/e03_alert_rat.json
- 话题：警觉；触发：ambient；状态：alerted；模式：turn
- 角色：E03；优先级：；权重：1；可重复：True；冷却：3
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：啥动静？……不会是耗子吧。

### e03_mutter_car

- 文件：Assets/Resources/Dialogues/e03_mutter_car.json
- 话题：车；触发：ambient；状态：idle；模式：turn
- 角色：E03；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：昨晚楼下，好像有辆车开进来。
   - keyword：车
   - hint：这么晚，谁会把车开进来？
   - flag：intel_night_car

### e03_mutter_chen

- 文件：Assets/Resources/Dialogues/e03_mutter_chen.json
- 话题：同事；触发：ambient；状态：idle；模式：turn
- 角色：E03；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：{E04}又在那儿神神叨叨的。
   - keyword：{E04}

### e03_mutter_hungry

- 文件：Assets/Resources/Dialogues/e03_mutter_hungry.json
- 话题：吃饭；触发：ambient；状态：idle；模式：turn
- 角色：E03；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：饿死了……食堂几点开来着？

### e03_mutter_pork

- 文件：Assets/Resources/Dialogues/e03_mutter_pork.json
- 话题：吃饭；触发：ambient；状态：idle；模式：turn
- 角色：E03；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：昨天那红烧肉，咸得能齁死人。
2. **阿强（E03）**：今天可别再是红烧肉了。

### e03_mutter_sleepy

- 文件：Assets/Resources/Dialogues/e03_mutter_sleepy.json
- 话题：抱怨；触发：ambient；状态：idle；模式：turn
- 角色：E03；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：我是真不想上夜班了。
2. **阿强（E03）**：困……

### e03_mutter_snack

- 文件：Assets/Resources/Dialogues/e03_mutter_snack.json
- 话题：吃饭；触发：ambient；状态：idle；模式：turn
- 角色：E03；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：抽屉里还有半包饼干……
2. **阿强（E03）**：留着，留着下半夜吃。

### e04_alert_knew

- 文件：Assets/Resources/Dialogues/e04_alert_knew.json
- 话题：警觉；触发：ambient；状态：alerted；模式：turn
- 角色：E04；优先级：；权重：1；可重复：True；冷却：3
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **大陈（E04）**：我就说有东西！……谁？

### e04_alert_shadow

- 文件：Assets/Resources/Dialogues/e04_alert_shadow.json
- 话题：警觉；触发：ambient；状态：alerted；模式：turn
- 角色：E04；优先级：；权重：1；可重复：True；冷却：3
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **大陈（E04）**：刚才那边……是不是有个人影？

### e04_mutter_building

- 文件：Assets/Resources/Dialogues/e04_mutter_building.json
- 话题：怪事；触发：ambient；状态：idle；模式：turn
- 角色：E04；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **大陈（E04）**：我跟你说，这楼不对劲。
2. **大陈（E04）**：一到晚上就有动静。

### e04_mutter_camera

- 文件：Assets/Resources/Dialogues/e04_mutter_camera.json
- 话题：监控；触发：ambient；状态：idle；模式：turn
- 角色：E04；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **大陈（E04）**：这摄像头……是不是一直对着我？

### e04_mutter_inspect

- 文件：Assets/Resources/Dialogues/e04_mutter_inspect.json
- 话题：查岗；触发：ambient；状态：idle；模式：turn
- 角色：E04；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **大陈（E04）**：上头这周肯定要来查岗。
2. **大陈（E04）**：肯定。

### e04_mutter_radio

- 文件：Assets/Resources/Dialogues/e04_mutter_radio.json
- 话题：对讲机；触发：ambient；状态：idle；模式：turn
- 角色：E04；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **大陈（E04）**：{E03}，别睡了，醒醒！
   - keyword：{E03}
2. **大陈（E04）**：……又没人回。

### e04_mutter_voice

- 文件：Assets/Resources/Dialogues/e04_mutter_voice.json
- 话题：对讲机；触发：ambient；状态：idle；模式：turn
- 角色：E04；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **大陈（E04）**：对讲机里刚才那一声……不是我们的人。
   - keyword：对讲机
   - hint：不是自己人……那是谁在用这个频道？
   - flag：intel_radio_voice

### e04_mutter_window

- 文件：Assets/Resources/Dialogues/e04_mutter_window.json
- 话题：窗；触发：ambient；状态：idle；模式：turn
- 角色：E04；优先级：；权重：1；可重复：True；冷却：5
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **大陈（E04）**：二楼那扇窗，昨晚明明是开着的。
   - keyword：窗
   - hint：谁会半夜去开那扇窗？
   - flag：intel_open_window

### guard_basement_followup

- 文件：Assets/Resources/Dialogues/guard_basement_followup.json
- 话题：地下室；触发：ambient；状态：idle；模式：turn
- 角色：E01、E02；优先级：100；权重：3；可重复：False；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米
- 条件：{"target":"StoryFlag","check":"FlagValue","op":"Equals","flagKey":"intel_basement_shift","boolValue":true}

1. **小刘（E02）**：下面那扇门，你别自己开。
2. **老王（E01）**：为什么？刘队。
   - keyword：刘队
   - nameOf：E02
3. **小刘（E02）**：上回老周开了一次，第二天就调走了。
   - blurred：老周……第二天就……
   - keyword：老周
   - hint：调走了。是真的调走了吗？
   - flag：intel_zhou_transferred

### guard_shift_talk

- 文件：Assets/Resources/Dialogues/guard_shift_talk.json
- 话题：换班；触发：ambient；状态：idle；模式：turn
- 角色：E01、E02；优先级：100；权重：3；可重复：True；冷却：10
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：{E02}，换班还有多久？
   - keyword：{E02}
2. **小刘（E02）**：两点。地下室今晚归你。
   - blurred：……地下室……归你
   - keyword：地下室
   - hint：这栋楼……有地下室？
   - flag：intel_basement_shift
3. **老王（E01）**：又是我。钥匙呢？
4. **小刘（E02）**：老张拿着。别去烦他，他这两天脾气不好。
   - blurred：老张……别去烦他
   - keyword：老张
   - hint：脾气不好……这两天出什么事了？
   - flag：intel_key_holder
5. **老王（E01）**：行吧。

### idle_complain_shift

- 文件：Assets/Resources/Dialogues/idle_complain_shift.json
- 话题：抱怨；触发：ambient；状态：idle；模式：turn
- 角色：A、B；优先级：；权重：1；可重复：True；冷却：4
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **A**：这鞋穿一晚上，脚都肿了。
2. **B**：忍着吧。

### idle_meal

- 文件：Assets/Resources/Dialogues/idle_meal.json
- 话题：吃饭；触发：ambient；状态：idle；模式：turn
- 角色：A、B；优先级：；权重：1；可重复：True；冷却：4
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **A**：你晚上吃了没？
2. **B**：食堂那个卤蛋又没了。
3. **A**：每次都这样。

### idle_salary

- 文件：Assets/Resources/Dialogues/idle_salary.json
- 话题：工资；触发：ambient；状态：idle；模式：turn
- 角色：A、B；优先级：；权重：1；可重复：True；冷却：4
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **A**：这个月工资发了没？
2. **B**：发了，又扣了。
3. **A**：扣啥？
4. **B**：谁知道。

### idle_smoke

- 文件：Assets/Resources/Dialogues/idle_smoke.json
- 话题：抽烟；触发：ambient；状态：idle；模式：turn
- 角色：A、B；优先级：；权重：0.8；可重复：True；冷却：4
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **A**：{B}，借个火。
   - keyword：{B}
2. **B**：少抽点，上头查得紧。
3. **A**：查得紧的又不止这个。

### idle_weather

- 文件：Assets/Resources/Dialogues/idle_weather.json
- 话题：天气；触发：ambient；状态：idle；模式：turn
- 角色：A、B；优先级：；权重：1；可重复：True；冷却：4
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **A**：外面下雨了？
2. **B**：听着像。
3. **A**：又得湿着回去。

### pair_e01_e02_water

- 文件：Assets/Resources/Dialogues/pair_e01_e02_water.json
- 话题：同事；触发：ambient；状态：idle；模式：turn
- 角色：E01、E02；优先级：；权重：1.5；可重复：True；冷却：6
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **老王（E01）**：{E02}，给我倒杯热水。
   - keyword：{E02}
2. **小刘（E02）**：您自己倒吧，我这儿走不开。
3. **老王（E01）**：现在的年轻人啊……

### pair_e03_e04_car

- 文件：Assets/Resources/Dialogues/pair_e03_e04_car.json
- 话题：车；触发：ambient；状态：idle；模式：turn
- 角色：E03、E04；优先级：；权重：3；可重复：True；冷却：6
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：昨晚那辆车，你看见没？
   - keyword：车
   - hint：又是那辆车……
   - flag：intel_night_car
2. **大陈（E04）**：别看。看见了也当没看见。
3. **阿强（E03）**：为啥？
4. **大陈（E04）**：上回老周就多看了一眼。
   - keyword：老周
   - hint：老周……又是他？
   - flag：intel_zhou

### pair_e03_e04_food

- 文件：Assets/Resources/Dialogues/pair_e03_e04_food.json
- 话题：吃饭；触发：ambient；状态：idle；模式：turn
- 角色：E03、E04；优先级：；权重：1.5；可重复：True；冷却：6
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **阿强（E03）**：你吃了没？
2. **大陈（E04）**：没胃口。
3. **阿强（E03）**：那你那份给我？
4. **大陈（E04）**：……拿去。

### pair_e03_e04_monitor

- 文件：Assets/Resources/Dialogues/pair_e03_e04_monitor.json
- 话题：监控；触发：ambient；状态：idle；模式：turn
- 角色：E03、E04；优先级：；权重：2；可重复：True；冷却：6
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：5 米

1. **大陈（E04）**：你说，监控室到底有没有人？
   - keyword：监控室
   - hint：没人看的监控，为什么一直开着？
   - flag：intel_monitor_room
2. **阿强（E03）**：谁知道，反正我没见过。
3. **大陈（E04）**：那它一直亮着红灯，是给谁看的？


## 系统/测试

### _speaker_lost

- 文件：Assets/Resources/Dialogues/_speaker_lost.json
- 话题：走散；触发：manual；状态：any；模式：turn
- 角色：A；优先级：；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：0 米

1. **A**：人呢？

### _test_highpri

- 文件：Assets/Resources/Dialogues/_test_highpri.json
- 话题：；触发：manual；状态：any；模式：realtime
- 角色：A；优先级：100；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：99 米

1. **A**：【测试】高优先级任务演出

### _test_lowpri

- 文件：Assets/Resources/Dialogues/_test_lowpri.json
- 话题：；触发：manual；状态：any；模式：realtime
- 角色：A、B；优先级：0；权重：0；可重复：True；冷却：0
- 距离（清晰／关键词／可闻）：6／10／15 米；对话双方距离：99 米

1. **A**：【测试】低优先级占位第一句
2. **B**：【测试】低优先级占位第二句

## 其他语言文件

### en/guard_shift_talk

- E01：{E02}, how long until the shift change?
- E02：Two o'clock. The basement is yours tonight.
- E01：Me again. Where's the key?
- E02：Old Zhang has it. Don't bother him, he's been in a mood.
- E01：Fine.

