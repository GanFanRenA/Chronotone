# Chronotone 游戏设定与架构文档

> 本文档记录 Chronotone 的游戏设定（玩法规则、数据结构）与场景架构。
>
> 当前阶段目标：把项目从「单场景多关卡 + 数据/场景双份 + 运行时反复重建」重构为
> **「一场景一关卡 + 场景为唯一来源 + 烘焙一次、运行只读」**。
>
> 状态：**代码实现已完成**；Player 共享 Prefab 提取与场景迁移需在 Unity 编辑器手动完成（见 §10、§11）。

---

## 目录

- [1. 项目概述](#1-项目概述)
- [2. Node（节点）定义](#2-node节点定义)
- [3. 时间轴与时钟](#3-时间轴与时钟)
- [4. 全局设置 GameSettingsSO](#4-全局设置-gamesettingsso)
- [5. 判定与玩法流程](#5-判定与玩法流程)
- [6. 旧架构（迁移前基线）](#6-旧架构迁移前基线)
- [7. 新架构：一场景一关卡](#7-新架构一场景一关卡)
- [8. 烘焙一次、运行只读](#8-烘焙一次运行只读)
- [9. Tilemap 分层 + 动画演出](#9-tilemap-分层--动画演出)
- [10. 场景内组件结构与编辑器操作](#10-场景内组件结构与编辑器操作)
- [11. 迁移路径](#11-迁移路径)
- [12. 版本与后续](#12-版本与后续)

---

## 1. 项目概述

- 类型：2D 横版节奏跳跃游戏（Unity，URP）。
- 核心玩法：玩家角色沿一条“完美轨道”前进，在拍点按任意键（键盘任意键 / 触摸）完成判定；
  判定成功继续跟轨，Miss / 脱轨受阻触发死亡回溯。
- 数据流（目标）：
  `场景（NodeMarker + Tilemap + 烘焙缓存）` → `ChartPlayer` → `Player`（物理 + 判定）。

---

## 2. Node（节点）定义

节点是谱面的最小单位。**逻辑数据**用 `NodeData`（序列化类），**场景表现**用 `NodeMarker`
（挂在 `Assets/Prefabs/Node.prefab` 上的 MonoBehaviour）。

> 新架构下：`NodeMarker` 是**唯一编辑来源**（直接摆在场景里）；
> `NodeData` 是烘焙时从 `NodeMarker` 生成的**运行缓存**，序列化在场景组件里，运行时只读。

### 2.1 枚举

```csharp
public enum NodeAction { None = 0, Jump = 1, FastFall = 2, Respawn = 3 }
public enum NodeGravity { None = 0, Normal = 1, Reset = 2 }

// 以下两个枚举仅用于旧谱面迁移，不再参与运行逻辑
public enum NodeType    { Jump = 0, Respawn = 1, Gravity = 2, FastFall = 3,
                          GravityFastFall = 4, WeakGravity = 5, GravityJump = 6 }
public enum NodeTrigger { Manual = 0, Auto = 1, None = 2 }
```

### 2.2 NodeData 字段（运行缓存）

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `beat` | `float` | — | 拍点（逻辑时间） |
| `order` | `int` | — | 同一 beat 内的排序号 |
| `action` | `NodeAction` | `Jump` | 动作：None / Jump（起跳）/ FastFall（快速下落）/ Respawn（重生检查点） |
| `gravity` | `NodeGravity` | `None` | 重力：None / Normal（反转方向并保留竖直速度）/ Reset（反转方向并清零竖直速度）。**重力转换永远先于同节点动作执行** |
| `auto` | `bool` | `false` | 自动节点：不判定、不计分、不显示判定提示，始终跟随轨道；Respawn 自动时经过即保存检查点 |
| `visible` | `bool` | `true` | 关闭后运行时隐藏节点，编辑模式仍可见 |
| `strength` | `float` | `0` | 动作力度：Jump = 起跳速度，FastFall = 沿当前重力方向的速度脉冲；`0` 表示使用 `GameSettings` 默认值 |
| `position` | `Vector2` | — | 世界坐标（烘焙时派生） |
| `deltaX` | `float` | — | 与上一节点的 X 距离（派生） |
| `type` / `trigger` | 旧枚举 | — | 旧数据迁移用 |

派生语义：

- `IsManuallyTriggered` → `!auto`（手动节点才参与判定）。
- `HasRouteEffect` → `action` 为 Jump / FastFall，或 `gravity != None`（影响轨道形状）。
- `ResolveStrength(strength, default)`：`strength > 0` 用之，否则用默认。
- `GetDefaultStrength(action, settings)`：Jump → `settings.JumpVelocity`；FastFall → `settings.FastFallImpulse`；其余 → 0。

### 2.3 NodeMarker 字段（场景编辑源）

逻辑字段与 `NodeData` 一致（`beat/order/action/gravity/auto/visible/strength`），另有：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | `int` | 排序后的序号（烘焙写入） |
| `deltaX` | `float` | 与上一节点的 X 距离 |
| `gravityDot` | `SpriteRenderer` | Gravity ≠ None 时显示的白色圆心 |

`NodeMarker` 行为：按 `NodeAppearanceSet` 上色；按 `visible` 控制运行时显隐（编辑模式恒显示）；
X 坐标由 `beat` 经 BPM 自动计算（`UpdatePos`），Y 坐标自由摆放。

### 2.4 旧字段迁移规则（NodeData.MigrateLegacy）

| 旧 `NodeType` | 新 `action` | 新 `gravity` | 新 `auto` |
| --- | --- | --- | --- |
| Jump | Jump | None | 由 trigger 决定 |
| Respawn | Respawn | None | 由 trigger 决定 |
| Gravity | None | Reset | 由 trigger 决定 |
| FastFall | FastFall | None | 由 trigger 决定 |
| GravityFastFall | FastFall | Reset | 由 trigger 决定 |
| WeakGravity | None | Reset | 由 trigger 决定 |
| GravityJump | Jump | Reset | 由 trigger 决定 |

`auto = (trigger != Manual)`；`Auto/None` 均不参与判定。

---

## 3. 时间轴与时钟

### 3.1 BpmTimeline —— BPM / 速度时间轴

`BpmSegment`：

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `beat` | `float` | — | 该段起始拍，第一段必须为 0 |
| `bpm` | `float` | `120` | 该段 BPM |
| `speed` | `float` | `5` | 每拍沿 X 轴跨越的格数（每格 = 1 世界单位） |

`BpmTimeline` 持有 `List<BpmSegment> segments`，提供 `BeatToSeconds` / `SecondsToBeat` /
`DistanceAtBeat` / `DistanceBetweenBeats` 等换算。

> 新架构下 `BpmTimeline` 作为 `[Serializable]` 字段直接嵌入场景组件（`ChartPlayer`），不再放 ChartSO。

### 3.2 SongClock —— 播放时钟

- 唯一 DSP 时钟：`PlaybackTime = dspTime - scheduledDspTime`（调度等待期为负）。
- `SongTime = scheduledSongTime + PlaybackTime`（叠加全局偏移）。
- `Schedule(lead, offset)` 安排开始；`Seek(lead, targetSongTime)` 用于检查点回退。

### 3.3 ChartMotionTimeline —— 时间→位置

将歌曲时间映射到玩家位置；起点用游戏开始位置，后续点来自节点；查询只依赖绝对歌曲时间。

---

## 4. 全局设置 GameSettingsSO

路径：`Assets/Resources/GameSettings.asset`（`Resources` 下，`GameSettingsSO.LoadDefault()` 全局共享，不随场景变化）。

| 分组 | 字段 | 默认 | 说明 |
| --- | --- | --- | --- |
| Judgement | `perfectWindowSeconds` | `0.06` | Perfect 判定窗（秒） |
| Judgement | `greatWindowSeconds` | `0.14` | Great 判定窗 |
| Judgement | `badWindowSeconds` | `0.24` | Bad 判定窗（也是 Miss 越界） |
| Auto Play | `autoModeEnabled` | `false` | 自动模式开关 |
| Auto Play | `autoPlayOffsetMilliseconds` | `0` | 自动输入偏移（限制在 Great 窗内） |
| Playback | `scheduleLeadSeconds` | `0.15` | 音频预调度提前量 |
| Playback | `globalOffsetSeconds` | `0` | 全局音画偏移（秒），正值让谱面整体晚于音乐 |
| Playback | `timelineNodeClickVolume` | `0.8` | 时间轴预览卡点音量 |
| Death Rewind | `respawnDelaySeconds` | `0.35` | 死亡后等待回溯的延迟 |
| Death Rewind | `rewindDurationSeconds` | `0.65` | 回溯动画时长 |
| Death Rewind | `motionSamplesPerSecond` | `30` | 运动记录采样率 |
| Death Rewind | `motionHistorySeconds` | `30` | 运动历史保留时长 |
| Death Rewind | `rewindTint` | `(0.35,0.85,1,1)` | 回溯着色 |
| Death Rewind | `rewindPulseFrequency` | `18` | 回溯脉冲频率 |
| Physics | `jumpVelocity` | `8` | Jump 默认起跳速度 |
| Physics | `gravityScale` | `3` | 重力缩放 |
| Physics | `angularSpeed` | `220` | 空中旋转角速度（度/秒） |
| Physics | `fastFallImpulse` | `60` | FastFall 默认速度脉冲 |
| Physics | `fallDeathDistance` | `12` | 掉落死亡距离 |
| Physics | `groundLayers` | `~0` | 地面层掩码（全部） |
| Physics | `blockingLayers` | `1 << 6` | 阻挡层掩码（Layer 6） |
| Physics | `blockedCheckWindow` | `0.12` | 受阻判定窗口 |
| Physics | `minMoveDistance` | `0.001` | 最小移动距离 |
| Physics | `collisionSkin` | `0.002` | 碰撞皮肤厚度 |
| Physics | `groundProbeDistance` | `0.03` | 地面探测距离 |
| Judgement Feedback | `sizeMultiplier` | `1` | 反馈缩放 |
| Judgement Feedback | `worldUnitsPerReferencePixel` | `0.01` | 参考像素→世界单位 |
| Judgement Feedback | `perfectDiamondSize` | `42` | Perfect 菱形大小 |
| Judgement Feedback | `greatDiamondSize` | `36` | Great 菱形大小 |
| Judgement Feedback | `missDiamondSize` | `30` | Miss 菱形大小 |
| Judgement Feedback | `shardSize` | `13` | Perfect 碎片大小 |
| Judgement Feedback | `shardDistance` | `74` | Perfect 碎片飞出距离 |
| Judgement Feedback | `crossSize` | `(64,10)` | 失败叉号尺寸 |

---

## 5. 判定与玩法流程

### 5.1 判定

- 手动节点（`!auto`）参与判定；输入为键盘任意键 `wasPressedThisFrame` 或触摸按下。
- 按 `|输入时刻 - 节点时刻|` 落窗：`≤ perfect` → Perfect；`≤ great` → Great；`≤ bad` → Bad；超过 bad 窗 → Miss。
- Perfect / Great 为成功命中（`IsSuccessfulHit`）；Miss 触发死亡回溯。

### 5.2 轨道模拟（PerfectRoute）

- `PerfectRouteBuilder.Build()` 用固定步长物理模拟推进竖直运动（Jump/FastFall/重力），
  X 由 `RouteXTimeline`（beat→x）驱动，生成 `PerfectRouteSample[]`。
- 受阻或未完成则烘焙失败并报错。
- **新架构下该模拟只在「烘焙」时运行一次，运行时不重跑。**

### 5.3 死亡回溯

- 死亡后记录运动历史，等待 `respawnDelaySeconds`，再按 `rewindDurationSeconds` 播回溯动画，
  回到最近 `Respawn` 检查点（无检查点则回起点），并 `Seek` 音频与时钟。

---

## 6. 旧架构（迁移前基线）

> 本节仅用于迁移时读取旧数据；迁移完成后相关资产/逻辑退役。

### 6.1 旧资产

| 资产 | 内容 |
| --- | --- |
| `ChartSO` | `clip` + `nodes(List<NodeData>)` + `bpmTimeline` + `globalOffsetBeats` |
| `ChartTrackSO` | `samples(List<PerfectRouteSample>)` + `blocked` + `complete` + `routeVersion` |
| `StageDefinitionSO` | 只捆 `ChartSO` + `ChartTrackSO` |

### 6.2 旧工作流（将被移除）

- `ChartPlayer.LoadFromChart()`：删除重建场景 `NodeMarker`（从 `ChartSO.nodes`）。
- `ChartPlayer.Bake()`：场景 `NodeMarker` → `NodeData` → 模拟 → 写回 `ChartSO` / `ChartTrackSO`。
- `SampleScene` 单场景多关卡切换（`StageDefinitionSO` 切换 + 脏标记 + 弹窗）。

### 6.3 旧场景

`Assets/Scenes/SampleScene.unity`：一个 `Grid` + 一个子 Tilemap `Platform`（Layer 6，
挂 `Tilemap`/`TilemapRenderer`/`TilemapCollider2D`）；`ChartPlayer.surfaceTilemap` 指向它。
物理检测靠 `groundLayers`/`blockingLayers` 层掩码，不依赖 `surfaceTilemap` 字段。

---

## 7. 新架构：一场景一关卡

### 7.1 核心原则

1. **每个关卡一个独立 `.unity` 场景**，场景即关卡，不再有跨关卡切换。
2. **Node 与 Tilemap 场景化为唯一来源**，直接在场景里编辑、持久化，不反复重建。
3. **烘焙一次、运行只读**：物理模拟只发生在“烘焙”这个显式步骤，产物序列化进场景，运行时只反序列化。
4. **Player 与全局设置统一**：Player 用共享 Prefab，GameSettings 用 Resources 全局资产。
5. **退役 ChartSO / ChartTrackSO / StageDefinitionSO**，关卡数据全部收进场景组件。

### 7.2 存储映射（旧 → 新）

| 数据 | 旧位置 | 新位置 |
| --- | --- | --- |
| 音频 clip | `ChartSO.clip` | 场景组件字段（引用 AudioClip 资产） |
| BPM 时间轴 | `ChartSO.bpmTimeline` | 场景组件字段（`[Serializable]` 直接序列化） |
| 全局偏移 | `ChartSO.globalOffsetBeats` | `GameSettingsSO.globalOffsetSeconds`（全局秒） |
| 节点（编辑源） | `ChartSO.nodes` + 场景 `NodeMarker`（双份） | **仅场景 `NodeMarker`** |
| 节点（运行缓存） | 无（运行时读 `ChartSO.nodes`） | 场景组件序列化的 `NodeData[]`（烘焙生成） |
| 完美路径 | `ChartTrackSO.samples` | 场景组件序列化的 `PerfectRouteSample[]`（烘焙生成） |
| Player | 场景手搭组件堆 | **共享 Prefab** `Assets/Prefabs/Player.prefab` |
| GameSettings | `Resources/GameSettings.asset` | 不变（全局共享） |
| 关卡引用 | `StageDefinitionSO` | 无（场景即关卡） |

### 7.3 场景内组件结构

每个关卡场景包含：

```
<StageScene>                         (关卡根，可无单独对象)
├─ Grid
│   ├─ Background1 / Background2 …   (Tilemap + TilemapRenderer，纯视觉)
│   ├─ Platform1 / Platform2 …       (Tilemap + TilemapRenderer + TilemapCollider2D，Layer 6)
│   └─ Frontground1 / Frontground2 …(Tilemap + TilemapRenderer，纯视觉)
├─ <Nodes>                           (NodeMarker 实例，直接摆放)
│   ├─ Node_0 …
│   └─ …
├─ <ChartPlayer>  (ChartPlayer 组件)
│   ├─ clip / bpmTimeline                             ← 关卡元数据（收进场景）
│   ├─ bakedNodes: NodeData[]                        ← 节点缓存（烘焙生成）
│   ├─ bakedTrack: PerfectRouteSample[]              ← 完美路径缓存（烘焙生成）
│   └─ (扫描范围 scanRoot / 各 Tilemap 绑定)
└─ <Player>  (Player.prefab 实例)
```

---

## 8. 烘焙一次、运行只读

### 8.1 编辑（作者在场景里）

- 摆 `NodeMarker`（X 由 `beat` 经 BPM 自动算，Y 自由）。
- 画三层 Tilemap（Background / Platform / Frontground）。

### 8.2 烘焙（显式、一次性，作者点一下）

烘焙按钮/菜单执行：

1. 扫描 `scanRoot` 下所有 `NodeMarker` → 按 `beat/order` 排序 → 用 `BpmTimeline` 算每个节点 X。
2. 生成 `NodeData[]`（beat/order/action/gravity/auto/visible/strength/position/deltaX）。
3. 跑 `PerfectRouteBuilder.Build()` 生成 `PerfectRouteSample[]`。
4. 把 `NodeData[]` 与 `PerfectRouteSample[]` **序列化写回场景组件**（`.unity` 文件内），并标记为已烘焙。

### 8.3 运行（只读）

1. 直接反序列化 `bakedNodes` / `bakedTrack`。
2. `Player` / `BasicJudgement` / `PhysicsActionMode` 使用缓存数据。
3. `NodeMarker` 照常作为可视节点显示（受 `visible` 控制）。
4. **不重新扫描、不重新物理模拟。**

### 8.4 未烘焙 / 过期

- 编辑器：Inspector 提示“尚未烘焙 / 数据已过期，请烘焙”。
- 运行时：给出明确错误并停止（`enabled=false`），**绝不自动重烘焙**。

---

## 9. Tilemap 分层 + 动画演出

### 9.1 分层命名与渲染

- 三个分类（组）：`Background`（背景）、`Platform`（平台/地面）、`Frontground`（前景）。
- 每组下编号变体：`<组名><序号>`，例如 `Background1`、`Platform1`、`Frontground2`…
- 渲染顺序（`TilemapRenderer.sortingOrder` 分带）：`Background < Platform < Frontground`。
  建议：Background `0–9`，Platform `10–19`，Frontground `20+`；同组内序号越大越靠前。

### 9.2 碰撞规则

- **只有 `Platform` 组**的 Tilemap 挂 `TilemapCollider2D`，并保持 **Layer 6**
  （`blockingLayers = 1<<6`，且被 `groundLayers = ~0` 覆盖 → 地面 + 阻挡）。
- `Background` / `Frontground` **不得挂任何 `Collider2D`**（否则会被 `groundLayers = ~0` 误判为地面）。

### 9.3 动画演出：硬切换显隐（按拍点）

- 决策：按歌曲时间/拍点**硬切换层显隐**（瞬间，暂不做淡入淡出）；只有 Platform 参与碰撞；
  Tilemap 直接引用场景对象；同组变体**独立控制、可叠加**。
- 数据模型（纯数据，收进场景组件或 StageDefinitionSO 的等价物）：

```csharp
public enum StageLayerCategory { Background = 0, Platform = 1, Frontground = 2 }

[System.Serializable] public sealed class StageTilemapLayerDef
{
    public string id;              // 唯一标识，如 "Frontground2"
    public StageLayerCategory category;
    public int variantIndex;       // 组内序号 1/2/3…
    public bool hasCollision;      // 仅 Platform 组为 true
    public bool visibleByDefault;  // 首个事件前 / 未播放时
}

[System.Serializable] public sealed class StageLayerVisibilityEvent
{
    public string layerId;   // 目标层 id
    public float beat;       // 触发拍点（用 BpmTimeline 转秒）
    public bool visible;     // 目标显隐
}
```

- 运行时：取当前 `SongTime`，对每层找「`BeatToSeconds(beat) <= SongTime` 的最后一条事件」决定显隐，
  无事件则用 `visibleByDefault`；硬切换 `TilemapRenderer.enabled`。由 `SongTime` 驱动，死亡回溯自动回退。

---

## 10. 场景内组件结构与编辑器操作

> 按项目规范，场景物体/组件的搭建与 Prefab 提取需要你在 Unity 编辑器里手动完成；
> 我会提供清单与步骤，并写好对应的 C# 脚本。

### 10.1 Player 共享 Prefab（需在编辑器提取）

1. 打开现有 `SampleScene`，把 Player 根物体（含 VisualRoot、PhysicsActionMode、BasicJudgement、
   BasicJudgementFeedback、PlayerVisualFeedback、PlayerGroundTrail、Rigidbody2D、BoxCollider2D、AudioSource 等）
   整体拖到 `Assets/Prefabs/` 生成 `Player.prefab`。
2. 每个关卡场景实例化该 `Player.prefab`；起始位置按关卡摆放。
3. 后续对 Player 的组件/参数改动只改 Prefab，所有场景自动一致。

### 10.2 关卡模板场景（需在编辑器创建）

- 新建一个“关卡模板”场景：`Grid` + 空的三组 Tilemap（Background/Platform/Frontground）+ `ChartPlayer` + `Player.prefab` 实例。
- 新关卡 = 复制模板场景，改名，再摆节点 / 画 Tilemap / 填 clip 与 BPM。

### 10.3 已实现的脚本（代码）

- 修改 `ChartPlayer.cs`：
  - 新增字段 `clip` / `bpmTimeline` / `bakedNodes(NodeData[])` / `bakedTrack(PerfectRouteSample[])`。
  - 新增 `Bake()`（扫描 NodeMarker → 生成缓存 → 模拟轨道 → 序列化写回）。
  - 移除关卡切换、`LoadFromChart` 重建、Play 前自动重烘焙。
- 新增 `StageSceneController.cs`（含 `StageLayerCategory` / `StageTilemapLayerDef` / `StageLayerVisibilityEvent` / `StageLayerBinding`，Tilemap 绑定 + 按拍点硬切换显隐）。
- 修改 `Player.cs` / `BasicJudgement.cs` / `PhysicsActionMode.cs`：从 `ChartPlayer` 缓存读节点与轨道，而非 `ChartSO.nodes`。
- 修改 `ChartTimelineWindow.cs`：从场景 `NodeMarker` 读，而非 `ChartSO.nodes`。

---

## 11. 迁移路径

1. 提取 Player 共享 Prefab。
2. 建关卡模板场景。
3. 把现有 Glaciaxion 迁成独立场景（复用 SampleScene 里已有 NodeMarker 与 Platform Tilemap；
   旧 `ChartSO.nodes` / `ChartTrackSO.samples` 作为迁移数据源读入）。
4. 让 `ChartPlayer` 从场景读节点、烘焙写入场景，退役三个 SO。
5. 跑通一次烘焙 + 播放验证无回归，逐项 git commit。

---

## 12. 版本与后续

- 当前工程提交版本沿用 `1.0.0`（见 git 提交历史）。
- 本文档为设计基线；实现时字段/命名调整需同步更新本文档。
- 后续待办（视需求确认）：
  - 是否把 `StageSceneController` 并入 `ChartPlayer`（减少一个控制器）。
  - 是否支持“平台层也随演出开关碰撞”。
  - 是否需要淡入淡出过渡（当前明确为硬切换）。
