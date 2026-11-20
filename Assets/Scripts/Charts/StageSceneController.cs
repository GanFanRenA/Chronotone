using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>Tilemap 层的分类。Background / Platform / Frontground。</summary>
public enum StageLayerCategory
{
    Background = 0,
    Platform = 1,
    Frontground = 2
}

/// <summary>单个 Tilemap 层的静态描述（纯数据，序列化在场景组件里）。</summary>
[System.Serializable]
public sealed class StageTilemapLayerDef
{
    [Tooltip("唯一标识，如 Frontground2。")]
    public string id;

    [Tooltip("层分类。只有 Platform 参与碰撞。")]
    public StageLayerCategory category = StageLayerCategory.Background;

    [Tooltip("组内序号 1/2/3…")]
    public int variantIndex = 1;

    [Tooltip("是否参与碰撞（仅 Platform 组为 true）。")]
    public bool hasCollision;

    [Tooltip("首个显隐事件之前 / 未播放时的默认显隐。")]
    public bool visibleByDefault = true;
}

/// <summary>一条显隐演出事件：到某拍点把指定层硬切换为 visible。</summary>
[System.Serializable]
public sealed class StageLayerVisibilityEvent
{
    [Tooltip("目标层 id，需与 StageTilemapLayerDef.id / StageLayerBinding.id 一致。")]
    public string layerId;

    [Tooltip("触发拍点（相对谱面，运行时用 ChartPlayer.BpmTimeline 转秒）。")]
    public float beat;

    [Tooltip("目标显隐状态。")]
    public bool visible = true;
}

/// <summary>场景层绑定：持有场景里的 Tilemap 对象引用，按 id 与层描述匹配。</summary>
[System.Serializable]
public sealed class StageLayerBinding
{
    public string id;
    public Tilemap tilemap;
    public TilemapRenderer renderer;

    [Tooltip("Platform 层的碰撞体（TilemapCollider2D 或 CompositeCollider2D），随显隐一起开关；纯视觉层留空。")]
    public Collider2D collider;
}

/// <summary>
/// 关卡场景控制器：按歌曲时间硬切换各 Tilemap 层的显隐。
/// 层描述、显隐事件、场景绑定都直接序列化在本组件里（一场景一关卡，不产生额外资产）。
/// </summary>
public sealed class StageSceneController :
    MonoBehaviour, IStageAnimationEventSource, IStageAnimationSource
{
    [Header("Layers（层描述）")]
    [SerializeField] private List<StageTilemapLayerDef> layers =
        new List<StageTilemapLayerDef>();

    [Header("Visibility Events（按拍点硬切换显隐）")]
    [SerializeField] private List<StageLayerVisibilityEvent> visibilityEvents =
        new List<StageLayerVisibilityEvent>();

    [Header("Bindings（场景 Tilemap 引用，按 id 匹配）")]
    [SerializeField] private List<StageLayerBinding> bindings =
        new List<StageLayerBinding>();

    [Header("Time Source")]
    [Tooltip("用于 beat→秒 换算的 BPM 时间轴来源。")]
    [SerializeField] private ChartPlayer chartPlayer;
    [Tooltip("用于取 SongTime 的 Player；留空时自动从 chartPlayer 解析。")]
    [SerializeField] private Player player;

    private readonly Dictionary<string, StageTilemapLayerDef> defById =
        new Dictionary<string, StageTilemapLayerDef>();
    private Player resolvedPlayer;
    private StageAnimationDriver driver;
    private bool driverDriven;

    private void OnEnable()
    {
        driver = StageAnimationDriver.FindDriver();
        driverDriven = driver != null;
        if (!driverDriven)
        {
            StageAnimationDriver.WarnMissingDriver(this);
            return;
        }

        if (visibilityEvents == null)
            return;

        foreach (StageLayerVisibilityEvent e in visibilityEvents)
        {
            if (e == null || string.IsNullOrEmpty(e.layerId))
                continue;

            driver.RegisterDiscrete(this, e.beat);
        }
    }

    private void OnDisable()
    {
        if (driverDriven && driver != null)
            driver.Unregister(this);

        driverDriven = false;
        driver = null;
    }

    private void Start()
    {
        resolvedPlayer = player != null
            ? player
            : (chartPlayer != null ? chartPlayer.ResolvePlayer() : null);
        RebuildIndex();
        ApplyState(double.NegativeInfinity);
    }

    private void Update()
    {
        if (driverDriven)
            return;

        double songTime = resolvedPlayer != null
            ? resolvedPlayer.SongTime
            : 0d;
        ApplyState(songTime);
    }

    public void ApplyAt(double songTime)
    {
        ApplyState(songTime);
    }

    public void CollectAnimationEvents(List<StageAnimationEvent> events)
    {
        if (events == null || visibilityEvents == null)
            return;

        foreach (StageLayerVisibilityEvent e in visibilityEvents)
        {
            if (e == null || string.IsNullOrEmpty(e.layerId))
                continue;

            events.Add(new StageAnimationEvent(
                e.beat,
                StageAnimationEventKind.LayerVisibility,
                $"显隐 {e.layerId}→{(e.visible ? "显示" : "隐藏")}",
                gameObject));
        }
    }

    private void RebuildIndex()
    {
        defById.Clear();
        if (layers == null)
            return;

        foreach (StageTilemapLayerDef def in layers)
        {
            if (def == null ||
                string.IsNullOrEmpty(def.id) ||
                defById.ContainsKey(def.id))
            {
                continue;
            }

            defById.Add(def.id, def);
        }
    }

    private void ApplyState(double songTime)
    {
        if (bindings == null)
            return;

        foreach (StageLayerBinding binding in bindings)
        {
            if (binding == null || string.IsNullOrEmpty(binding.id))
                continue;

            bool visible = ResolveVisible(binding.id, songTime);
            ApplyVisible(binding, visible);
        }
    }

    private bool ResolveVisible(string layerId, double songTime)
    {
        bool visible = GetDefaultVisible(layerId);

        if (visibilityEvents == null)
            return visible;

        foreach (StageLayerVisibilityEvent e in visibilityEvents)
        {
            if (e == null || e.layerId != layerId)
                continue;

            double eventTime = BeatToSeconds(e.beat);
            if (eventTime <= songTime)
                visible = e.visible;
        }

        return visible;
    }

    private bool GetDefaultVisible(string layerId)
    {
        if (defById.TryGetValue(layerId, out StageTilemapLayerDef def))
            return def.visibleByDefault;

        return true;
    }

    private double BeatToSeconds(float beat)
    {
        if (chartPlayer != null && chartPlayer.BpmTimeline != null)
            return chartPlayer.BpmTimeline.BeatToSeconds(beat);

        return beat * 0.5d; // 兜底 120 BPM
    }

    private static void ApplyVisible(StageLayerBinding binding, bool visible)
    {
        if (binding.renderer != null &&
            binding.renderer.enabled != visible)
        {
            binding.renderer.enabled = visible;
        }

        if (binding.collider != null &&
            binding.collider.enabled != visible)
        {
            binding.collider.enabled = visible;
        }
    }
}
