# Chronotone 游戏设定文档

> 本文档记录当前项目的游戏设定（数据模型、玩法规则、场景结构），并给出下一阶段
> **Tilemap 分层 + 动画演出系统** 的设计方案。
>
> 状态：**现状记录 + 新系统设计（待实现）**
> 本文档中的“拟新增”代码/组件仅为设计约定，尚未落地到工程。

---

## 目录

1. [项目概述](#1-项目概述)
2. [数据资产与脚本结构（现状）](#2-数据资产与脚本结构现状)
3. [Node（节点）定义](#3-node节点定义)
4. [全局设置 GameSettingsSO](#4-全局设置-gamesettingsso)
5. [场景对象与组件（现状）](#5-场景对象与组件现状)
6. [玩法流程（现状）](#6-玩法流程现状)
7. [当前场景 Tilemap 现状](#7-当前场景-tilemap-现状)
8. [新设计：Tilemap 分层 + 动画演出系统](#8-新设计tilemap-分层--动画演出系统)
9. [需要添加的物体与组件清单](#9-需要添加的物体与组件清单)
10. [版本与后续](#10-版本与后续)

---

## 1. 项目概述

- 类型：2D 横版节奏跳跃游戏（Unity，URP）。
- 核心玩法：玩家角色沿一条“完美轨道”前进，在拍点按任意键（键盘任意键 / 触摸）完成判定；
  判定成功后继续跟轨，Miss / 脱轨受阻则触发死亡回溯。
- 数据流：
  `ChartSO`（谱面数据）→ `ChartTrackSO`（烘焙出的完美轨道）→ `StageDefinitionSO`（关卡 = 谱面 + 轨道）→
  `ChartPlayer`（场景关卡驱动器）→ `Player`（角色，含物理与判定）。

---

## 2. 数据资产与脚本结构（现状）

### 2.1 ChartSO —— 谱面（ScriptableObject 资产）

路径示例：`Assets/GameData/Charts/<歌名>/<歌名>.asset`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `clip` | `AudioClip` | 歌曲音频 |
| `nodes` | `List<NodeData>` | 节点列表（见 §3） |
| `bpmTimeline` | `BpmTimeline` | BPM / 速度时间轴（见 §2.5） |
| `globalOffsetBeats` | `float` | 全局偏移（单位：拍），正值让谱面整体晚于音乐 |

- 派生属性：`GlobalOffsetSeconds`（用 `bpmTimeline.BeatToSeconds(globalOffsetBeats)` 换算）。
- 关键方法：
  - `UpdatePos(float baseX)`：按 beat 排序节点，累计计算每个节点的世界 X 坐标 `position` 与 `deltaX`。
  - `EnsureMigrated()`：迁移旧 `NodeType/NodeTrigger` 字段到新字段（见 §3）。

### 2.2 ChartTrackSO —— 完美轨道（ScriptableObject 资产，二进制序列化）

路径示例：`Assets/GameData/Charts/<歌名>/<歌名>Track.asset`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `samples` | `List<PerfectRouteSample>` | 轨道采样点序列（时间 / 位置 / 竖直速度 / 角度 / 是否着地 / 重力是否反转 / 重力加速度） |
| `blocked` | `bool` | 轨道是否受阻 |
| `complete` | `bool` | 轨道是否完整 |
| `routeVersion` | `int` | 轨道规则版本（当前 `4`） |

- 由 `ChartPlayer.Bake()` 通过物理模拟烘焙生成；旧版本轨道会在进入 Play 时自动重烘焙。

### 2.3 StageDefinitionSO —— 关卡（ScriptableObject 资产）

路径示例：`Assets/GameData/Charts/<歌名>/<歌名>Stage.asset`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `chart` | `ChartSO` | 谱面 |
| `track` | `ChartTrackSO` | 完美轨道 |

- 派生属性：`IsComplete`（chart 与 track 均非空）。
- **当前只捆 Chart + Track，不包含任何场景/Tilemap 信息。**

### 2.4 脚本目录结构

```
Assets/Scripts/
├─ Charts/        ChartSO, ChartTrackSO, StageDefinitionSO, ChartPlayer, ChartPlayerEditor, Chart
├─ Node/          NodeData, NodeMarker, Node, NodeAppearanceSet
├─ Timing/        BpmTimeline, SongClock, ChartMotionTimeline
├─ Player/        Player, PlayerActionMode, PhysicsActionMode, PerfectRoute,
│                 PlayerVisualFeedback, PlayerGroundTrail
├─ Judgement/     BasicJudgement, JudgementResult, BasicJudgementFeedback
├─ Camera/        CameraController
└─ Settings/      GameSettingsSO
```

### 2.5 BpmTimeline —— BPM / 速度时间轴

`BpmSegment`：

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `beat` | `float` | — | 该段起始拍，第一段必须为 0 |
| `bpm` | `float` | `120` | 该段 BPM |
| `speed` | `float` | `5` | 每拍沿 X 轴跨越的格数（每格 = 1 世界单位） |

`BpmTimeline` 持有 `List<BpmSegment> segments`（默认含一段 `beat=0, bpm=120, speed=5`）。
提供 `BeatToSeconds` / `SecondsToBeat` / `DistanceAtBeat` / `DistanceBetweenBeats` 等换算。

---

## 3. Node（节点）定义

节点是谱面的最小单位，**逻辑数据**用 `NodeData`（序列化类），**场景表现**用 `NodeMarker`
（挂在 `Assets/Prefabs/Node.prefab` 上的 MonoBehaviour）。

### 3.1 动作 / 重力 / 旧字段枚举

```csharp
public enum NodeAction { None = 0, Jump = 1, FastFall = 2, Respawn = 3 }
public enum NodeGravity { None = 0, Normal = 1, Reset = 2 }

// 以下两个枚举仅用于旧谱面迁移，不再参与运行逻辑
public enum NodeType    { Jump = 0, Respawn = 1, Gravity = 2, FastFall = 3,
                          GravityFastFall = 4, WeakGravity = 5, GravityJump = 6 }
public enum NodeTrigger { Manual = 0, Auto = 1, None = 2 }
```

### 3.2 NodeData 字段（逻辑数据）

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `beat` | `float` | — | 拍点（逻辑时间） |
| `order` | `int` | — | 同一 beat 内的排序号 |
| `action` | `NodeAction` | `Jump` | 动作：None / Jump（起跳）/ FastFall（快速下落）/ Respawn（重生检查点） |
| `gravity` | `NodeGravity` | `None` | 重力：None / Normal（反转方向并保留竖直速度）/ Reset（反转方向并清零竖直速度）。**重力转换永远先于同节点动作执行** |
| `auto` | `bool` | `false` | 自动节点：不判定、不计分、不显示判定提示，始终跟随轨道；Respawn 自动时经过即保存检查点 |
| `visible` | `bool` | `true` | 关闭后运行时隐藏节点，编辑模式仍可见 |
| `strength` | `float` | `0` | 动作力度：Jump = 起跳速度，FastFall = 沿当前重力方向的速度脉冲；`0` 表示使用 `GameSettings` 默认值 |
| `position` | `Vector2` | — | 世界坐标（由 `UpdatePos` 派生） |
| `deltaX` | `float` | — | 与上一节点的 X 距离（派生，`[HideInInspector]`） |
| `type` | `NodeType` | — | 旧字段，仅迁移用（`[HideInInspector]`） |
| `trigger` | `NodeTrigger` | — | 旧字段，仅迁移用（`[HideInInspector]`） |
| `migrated` | `bool` | — | 迁移标记（`[HideInInspector, SerializeField]`） |

派生语义：

- `IsManuallyTriggered` → `!auto`（手动节点才参与判定）。
- `HasRouteEffect` → `action` 为 Jump / FastFall，或 `gravity != None`（影响轨道形状）。
- `ResolveStrength(strength, default)`：`strength > 0` 用之，否则用默认。
- `GetDefaultStrength(action, settings)`：Jump → `settings.JumpVelocity`；FastFall → `settings.FastFallImpulse`；其余 → 0。

### 3.3 NodeMarker 字段（场景表现，Node.prefab）

逻辑字段与 `NodeData` 一致（`beat/order/action/gravity/auto/visible/strength`），另有：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | `int` | 派生结果，由 `ChartPlayer` 写入（`[HideInInspector]`） |
| `deltaX` | `float` | 派生（`[NonSerialized]`） |
| `gravityDot` | `SpriteRenderer` | Gravity ≠ None 时显示的白色圆心（Node 下白色 Circle 子物体的 SpriteRenderer） |
| `type` / `trigger` | 旧枚举 | 旧数据迁移用（`[HideInInspector]`） |

`NodeMarker` 行为：按 `NodeAppearanceSet` 上色；按 `visible` 控制运行时显隐（编辑模式恒显示）；
`OnValidate` 变化时通知 `ChartPlayer` 刷新时间轴预览与轨道。

### 3.4 旧字段迁移规则（NodeData.MigrateLegacy）

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

## 4. 全局设置 GameSettingsSO

路径：`Assets/Resources/GameSettings.asset`（`Resources` 下，`GameSettingsSO.LoadDefault()` 加载）。

| 分组 | 字段 | 默认 | 说明 |
| --- | --- | --- | --- |
| Judgement | `perfectWindowSeconds` | `0.06` | Perfect 判定窗（秒） |
| Judgement | `greatWindowSeconds` | `0.14` | Great 判定窗 |
| Judgement | `badWindowSeconds` | `0.24` | Bad 判定窗（也是 Miss 越界） |
| Auto Play | `autoModeEnabled` | `false` | 自动模式开关 |
| Auto Play | `autoPlayOffsetMilliseconds` | `0` | 自动输入偏移（限制在 Great 窗内） |
| Playback | `scheduleLeadSeconds` | `0.15` | 音频预调度提前量 |
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

> 所有字段都有 `Validate()` 做范围钳制，运行时与编辑器都会调用。

---

## 5. 场景对象与组件（现状）

| 对象 / 组件 | 职责 |
| --- | --- |
| `ChartPlayer` | 场景关卡驱动器：绑定 `StageDefinitionSO`、`surfaceTilemap`、`scanRoot`、`nodePrefab`、`nodeAppearanceSet`；提供 `Bake()`（烘焙轨道）、`LoadFromChart()`（从 ChartSO 重建场景节点）、`UpdatePos()`；编辑期自动刷新与 Play 前自动烘焙 |
| `Player` | 角色根：持有 `SongClock`；驱动 `PlayerActionMode`、`BasicJudgement`；处理死亡回溯与检查点重生；`SongTime`（public）为全局玩法时间 |
| `PlayerActionMode`（抽象） | 行动模式基类：`Enter/Exit/FixedTick/RespawnTo/SetControlLocked/SetRewindPose` 生命周期，由 `Player` 统一驱动 |
| `PhysicsActionMode` | 物理移动：有轨道权限时严格跟轨（`PerfectRoute` 采样），失去权限后自由物理（重力/跳跃/落地/受阻判定）；碰撞检测用 `groundLayers`/`blockingLayers` 掩码的 BoxCast |
| `BasicJudgement` | 判定：捕获键盘任意键/触摸输入，按判定窗结算 Perfect/Great/Bad/Miss，触发 `JudgementResolved` |
| `BasicJudgementFeedback` | 判定视觉反馈：菱形/碎片/叉号，早晚回声（ghost） |
| `PlayerVisualFeedback` | 角色表现：输入脉冲、Perfect 锁、早晚倾斜/挤压等（只动 `VisualRoot`，不动物理） |
| `PlayerGroundTrail` | 着地移动时的方块拖尾 |
| `CameraController` | 摄像机跟随：`transform.position = (target.x, target.y, -10)` |
| `Chart` / `Node` | 旧版谱面/节点场景组件（被 `NodeMarker` + `ChartPlayer` 取代，保留用于兼容） |

---

## 6. 玩法流程（现状）

### 6.1 时钟 SongClock

- 唯一 DSP 时钟：`PlaybackTime = dspTime - scheduledDspTime`（调度等待期为负）。
- `SongTime = scheduledSongTime + PlaybackTime`（叠加谱面全局偏移）。
- `Schedule(lead, chartOffsetSeconds)` 安排开始；`Seek(lead, targetSongTime)` 用于检查点回退。

### 6.2 判定

- 手动节点（`!auto`）参与判定；输入为键盘任意键 `wasPressedThisFrame` 或触摸按下。
- 按 `|输入时刻 - 节点时刻|` 落窗：`≤ perfect` → Perfect；`≤ great` → Great；`≤ bad` → Bad；超过 bad 窗 → Miss。
- Perfect / Great 为成功命中（`IsSuccessfulHit`）；Miss 触发死亡回溯。

### 6.3 轨道烘焙（PerfectRoute）

- `ChartPlayer.Bake()` 收集场景 `NodeMarker` → 转 `NodeData` → `PerfectRouteBuilder.Build()` 做固定步长物理模拟，
  生成 `PerfectRouteSample[]` 写入 `ChartTrackSO`。
- 模拟按节点动作（Jump/FastFall/重力）推进竖直运动，X 由 `RouteXTimeline`（beat→x）驱动；
  受阻或未完成则烘焙失败并报错。

### 6.4 死亡回溯

- 死亡后记录运动历史，按 `respawnDelaySeconds` 等待，再按 `rewindDurationSeconds` 播回溯动画，
  回到最近 `Respawn` 检查点（无检查点则回起点），并 `Seek` 音频与时钟。

---

## 7. 当前场景 Tilemap 现状

场景 `Assets/Scenes/SampleScene.unity` 中：

- 只有一个 `Grid`（cell size 1×1），其下**只有一个子 Tilemap**，名为 `Platform`：
  - Layer 6（= `blockingLayers` 默认值，同时被 `groundLayers = ~0` 覆盖 → 既是地面也是阻挡）。
  - 挂 `Tilemap` + `TilemapRenderer` + `TilemapCollider2D`。
- `ChartPlayer.surfaceTilemap` 指向这个 `Platform` 的 `Tilemap` 组件。

> **关键事实**：`surfaceTilemap` 字段在代码里只做空值校验与编辑器提示；**物理的地面/阻挡检测完全靠
> `PhysicsActionMode` 里的 `groundLayers` / `blockingLayers` 层掩码 BoxCast**，不依赖 `surfaceTilemap`。
> 因此新增多层 Tilemap 不会破坏物理，前提是层掩码与碰撞体配置正确（见 §8.3）。

---

## 8. 新设计：Tilemap 分层 + 动画演出系统

> 本阶段目标：让每个谱子/关卡由**多个小 Tilemap 层**组成（Background / Platform / Frontground 三组，
> 每组有编号变体 `1/2/…`），并可**按歌曲时间/拍点硬切换层的显隐**来配合演出。

### 8.1 设计决策（已与需求确认）

1. **动画**：按歌曲时间/拍点**硬切换层显隐**（瞬间显示/隐藏，暂不做淡入淡出）。
2. **碰撞**：**只有 Platform 组参与碰撞**；Background / Frontground 纯视觉、不挂碰撞体。
3. **存储/引用**：**直接引用场景对象**（Tilemap 组件），与现有 `surfaceTilemap` 方式一致。
4. **变体关系**：同一组内变体（如 `Frontground1` / `Frontground2`）**独立控制显隐，可叠加显示**，不做互斥。

### 8.2 分层命名与渲染

- 三个分类（组）：`Background`（背景）、`Platform`（平台/地面）、`Frontground`（前景）。
- 每组下编号变体：`<组名><序号>`，例如 `Background1`、`Background2`、`Platform1`、`Platform2`、`Frontground1`、`Frontground2`…
- 渲染顺序（`TilemapRenderer.sortingOrder` 分带）：`Background < Platform < Frontground`。
  建议：Background 用 `0–9`，Platform 用 `10–19`，Frontground 用 `20+`；同一组内序号越大越靠前。
  （也可改用 Unity Sorting Layer，但初期用 sortingOrder 分带更简单直观。）

### 8.3 碰撞规则

- **只有 `Platform` 组**的 Tilemap 挂 `TilemapCollider2D`，并保持 **Layer 6**（`blockingLayers = 1<<6`，
  且被 `groundLayers = ~0` 覆盖 → 地面 + 阻挡）。
- `Background` / `Frontground` **不得挂任何 `Collider2D`**（否则会被 `groundLayers = ~0` 误判为地面）。
- 平台碰撞合并（可选）：多个 `Platform` 变体如需要连续碰撞，可各自加 `CompositeCollider2D` + 静态 `Rigidbody2D` 合并，
  或沿用逐格 TilemapCollider2D（现状即后者）。

### 8.4 动画演出：硬切换显隐（按拍点）

- 数据：一条“显隐事件” = `{ 层 id, 触发拍点 beat, 目标显隐 visible }`。
- 语义：到某拍点，把指定层硬切换为 `visible`（`TilemapRenderer.enabled` 或 `GameObject.SetActive`），无过渡。
- 变体独立：每条事件只影响一个 `layerId`；多事件可叠加，同一时刻可同时显示多个 Frontground 变体。
- 层在**当前时刻的显隐** = “触发拍点 ≤ 当前时刻的最后一条事件”决定；若尚未到任何事件，则用 `visibleByDefault`。
- 因显隐由 `SongTime` 驱动，**死亡回溯时显隐会随 Seek 自动回退**，无需额外处理。

### 8.5 数据模型

#### 8.5.1 层描述与事件（纯数据，放 StageDefinitionSO）

```csharp
// 拟新增 —— 层分类
public enum StageLayerCategory
{
    Background = 0,   // 背景（纯视觉）
    Platform  = 1,    // 平台/地面（唯一碰撞）
    Frontground = 2   // 前景（纯视觉）
}

// 拟新增 —— 单个 Tilemap 层的静态描述（数据）
[System.Serializable]
public sealed class StageTilemapLayerDef
{
    public string id;                // 唯一标识，如 "Frontground2"
    public StageLayerCategory category;
    public int variantIndex;         // 组内序号：1/2/3…
    public bool hasCollision;        // 仅 Platform 组为 true
    public bool visibleByDefault;    // 首个事件之前 / 未播放时的显隐
}

// 拟新增 —— 一条显隐演出事件（按拍点硬切换）
[System.Serializable]
public sealed class StageLayerVisibilityEvent
{
    public string layerId;   // 目标层 id
    public float beat;       // 触发拍点（用 Chart.bpmTimeline 转秒）
    public bool visible;     // 目标显隐状态
}
```

扩展 `StageDefinitionSO`：

```csharp
public sealed class StageDefinitionSO : ScriptableObject
{
    [SerializeField] private ChartSO chart;
    [SerializeField] private ChartTrackSO track;

    // 拟新增 —— Tilemap 分层与演出数据
    [SerializeField] private List<StageTilemapLayerDef> layers;
    [SerializeField] private List<StageLayerVisibilityEvent> visibilityEvents;

    public ChartSO Chart => chart;
    public ChartTrackSO Track => track;
    // 新增：暴露 layers / visibilityEvents
}
```

#### 8.5.2 场景层绑定（场景对象引用，放 MonoBehaviour）

> **Unity 序列化约束（重要）**：`ScriptableObject` 资产（如 `StageDefinitionSO`）**无法持久化引用场景对象**
> （场景里的 Tilemap）；场景对象引用只能由场景里的 MonoBehaviour 持有。因此分层做两层：
> - **数据**（层描述 + 事件）放 `StageDefinitionSO`（资产）。
> - **场景绑定**（实际 Tilemap 组件引用）放场景侧组件，按 `id` 与数据匹配。

```csharp
// 拟新增 —— 场景层绑定（持有场景对象引用）
[System.Serializable]
public sealed class StageLayerBinding
{
    public string id;                  // 与 StageTilemapLayerDef.id 对应
    public Tilemap tilemap;            // 场景引用
    public TilemapRenderer renderer;   // 场景引用
    public TilemapCollider2D collider; // 仅 Platform 层非空
}

// 拟新增 —— 场景侧控制器
public sealed class StageSceneController : MonoBehaviour
{
    [SerializeField] private StageDefinitionSO stage;
    [SerializeField] private List<StageLayerBinding> bindings;
    [SerializeField] private Player player;   // 用于取 SongTime（public）
    // 也可改为共享 SongClock，初期先用 Player.SongTime 即可
}
```

### 8.6 运行时驱动流程

1. `Awake/Start`：以 `id` 为键构建 `Dictionary<string, StageLayerBinding>`；
   把所有层设为各自的 `visibleByDefault`（`renderer.enabled`）。
2. `Update`（或 `LateUpdate`）：
   - 取当前 `SongTime`（`player.SongTime`）。
   - 对每个层：遍历 `visibilityEvents` 中 `layerId` 匹配且 `BeatToSeconds(beat) <= SongTime` 的最后一条事件，
     取它的 `visible`；若无则用 `visibleByDefault`。
   - 目标状态与当前不一致时执行硬切换（`renderer.enabled = visible`；若用 `SetActive` 则注意与碰撞体同步，
     推荐只切 `renderer.enabled`，保持碰撞体不受显隐影响——除非平台层也需要按演出开关碰撞，此时再切整个 GameObject）。
3. 未开始播放 / `SongTime <= 0`：全部使用 `visibleByDefault`。

### 8.7 编辑期预览（可选，后续）

- 在 `ChartTimelineWindow`（`Assets/Editor/ChartTimelineWindow.cs`）中按拖动时间轴预览显隐状态，
  复用同一份 `visibilityEvents` 采样逻辑。

---

## 9. 需要添加的物体与组件清单

> 按项目规范，本阶段**不自动创建场景物体/组件**，由你按下列清单手动搭建；代码部分在确认设计后再实现。

### 9.1 场景搭建

1. 在现有 `Grid` 下新增子 Tilemap（每个：`GameObject + Tilemap + TilemapRenderer`）：
   - 背景组：`Background1`、`Background2`…（sortingOrder 0–9，无碰撞体）
   - 平台组：`Platform1`、`Platform2`…（每个挂 `TilemapCollider2D`，Layer 6，sortingOrder 10–19；
     可选 `CompositeCollider2D + Rigidbody2D(Static)` 合并碰撞）
   - 前景组：`Frontground1`、`Frontground2`…（sortingOrder 20+，无碰撞体）
2. 现有 `Platform` Tilemap：改名 `Platform1`（或保留，作为平台组第一个变体），
   并同步更新 `ChartPlayer.surfaceTilemap` 引用（或后续移除该字段依赖）。
3. 新增一个空物体（建议命名 `StageScene`），挂 `StageSceneController` 脚本（拟新增），
   绑定 `stage`、`bindings`（各层 id→Tilemap/Renderer/Collider）、`player`。

### 9.2 代码（拟新增，确认设计后实现）

- `Assets/Scripts/Charts/StageLayerCategory.cs`、`StageTilemapLayerDef.cs`、`StageLayerVisibilityEvent.cs`
  （或合并进一个文件）。
- `Assets/Scripts/Charts/StageSceneController.cs`（含 `StageLayerBinding`）。
- 修改 `StageDefinitionSO.cs`：新增 `layers`、`visibilityEvents` 两个列表与访问器。

---

## 10. 版本与后续

- 当前工程提交版本沿用 `1.0.0`（见 git 提交历史）。
- 本文档为设计基线；实现时若字段/命名调整，需同步更新本文档。
- 后续待办（视需求确认）：
  - 是否把 `StageSceneController` 与 `ChartPlayer` 合并（避免多一个控制器）。
  - 是否支持“平台层也随演出开关碰撞”。
  - 是否需要淡入淡出过渡（当前明确为硬切换）。
