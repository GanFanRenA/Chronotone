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
/// 关卡场景控制器：按歌曲时间硬切换各 Tilemap 层的显隐，并按外观动画（飞入飞出）
/// 的入场/出场窗口门控 Platform 的物理碰撞（入场完成前 / 出场开始后关闭碰撞体）。
/// 淡入淡出只改透明度、不参与碰撞门控；初始为关闭的碰撞体从头到尾不改变。
/// 层描述、显隐事件、场景绑定都直接序列化在本组件里（一场景一关卡，不产生额外资产）。
/// </summary>
public sealed class StageSceneController :
    MonoBehaviour, IStageAnimationEventSource, IStageAnimationSource, IBeatMappable
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
    private readonly Dictionary<StageLayerBinding, IStageAppearanceAnimation[]> appearanceCache =
        new Dictionary<StageLayerBinding, IStageAppearanceAnimation[]>();
    private readonly Dictionary<StageLayerBinding, IStageVisibilityAnimation[]> visibilityCache =
        new Dictionary<StageLayerBinding, IStageVisibilityAnimation[]>();
    private Player resolvedPlayer;
    private StageAnimationDriver driver;
    private bool driverDriven;
    private readonly HashSet<StageLayerBinding> lockedColliders =
        new HashSet<StageLayerBinding>();

    private const float BeatEpsilon = 0.0001f;

    private void OnEnable()
    {
        driver = StageAnimationDriver.FindDriver();
        driverDriven = driver != null;
        if (!driverDriven)
        {
            StageAnimationDriver.WarnMissingDriver(this);
            return;
        }

        RegisterStageBeats(driver);
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
        if (chartPlayer == null)
            chartPlayer = Object.FindFirstObjectByType<ChartPlayer>();

        resolvedPlayer = player != null
            ? player
            : (chartPlayer != null ? chartPlayer.ResolvePlayer() : null);
        CaptureInitialColliderState();
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

    /// <summary>节奏段移动时把层显隐事件拍点作为「主拍点」暴露给编辑器统一平移。</summary>
    public void CollectBeatAnchors(List<BeatAnchor> anchors)
    {
        if (anchors == null || visibilityEvents == null)
            return;

        foreach (StageLayerVisibilityEvent e in visibilityEvents)
        {
            if (e == null)
                continue;

            anchors.Add(new BeatAnchor(e.beat, d => e.beat += d));
        }
    }

    private void RegisterStageBeats(StageAnimationDriver targetDriver)
    {
        if (visibilityEvents != null)
        {
            foreach (StageLayerVisibilityEvent e in visibilityEvents)
            {
                if (e == null || string.IsNullOrEmpty(e.layerId))
                    continue;

                targetDriver.RegisterDiscrete(this, e.beat);
            }
        }

        if (bindings == null)
            return;

        foreach (StageLayerBinding binding in bindings)
        {
            foreach (IStageAppearanceAnimation appearance in
                     GetAppearanceComponents(binding))
            {
                if (appearance.CollisionEnterBeat > float.NegativeInfinity)
                {
                    targetDriver.RegisterDiscrete(
                        this,
                        appearance.CollisionEnterBeat);
                }

                if (appearance.CollisionExitBeat < float.PositiveInfinity)
                {
                    targetDriver.RegisterDiscrete(
                        this,
                        appearance.CollisionExitBeat);
                }
            }

            foreach (IStageVisibilityAnimation visibility in
                     GetVisibilityComponents(binding))
            {
                if (visibility.VisibilityEnterBeat > float.NegativeInfinity)
                {
                    targetDriver.RegisterDiscrete(
                        this,
                        visibility.VisibilityEnterBeat);
                }

                if (visibility.VisibilityExitBeat < float.PositiveInfinity)
                {
                    targetDriver.RegisterDiscrete(
                        this,
                        visibility.VisibilityExitBeat);
                }
            }
        }
    }

    private IStageAppearanceAnimation[] GetAppearanceComponents(
        StageLayerBinding binding)
    {
        if (appearanceCache.TryGetValue(binding, out IStageAppearanceAnimation[] cached))
            return cached;

        var result = new List<IStageAppearanceAnimation>();
        foreach (MonoBehaviour behaviour in GetBindingBehaviours(binding))
        {
            if (behaviour is IStageAppearanceAnimation appearance)
                result.Add(appearance);
        }

        IStageAppearanceAnimation[] array = result.ToArray();
        appearanceCache[binding] = array;
        return array;
    }

    private IStageVisibilityAnimation[] GetVisibilityComponents(
        StageLayerBinding binding)
    {
        if (visibilityCache.TryGetValue(binding, out IStageVisibilityAnimation[] cached))
            return cached;

        var result = new List<IStageVisibilityAnimation>();
        foreach (MonoBehaviour behaviour in GetBindingBehaviours(binding))
        {
            if (behaviour is IStageVisibilityAnimation visibility)
                result.Add(visibility);
        }

        IStageVisibilityAnimation[] array = result.ToArray();
        visibilityCache[binding] = array;
        return array;
    }

    private static MonoBehaviour[] GetBindingBehaviours(StageLayerBinding binding)
    {
        Component anchor = GetBindingAnchor(binding);
        return anchor != null
            ? anchor.GetComponentsInParent<MonoBehaviour>(true)
            : System.Array.Empty<MonoBehaviour>();
    }

    private static Component GetBindingAnchor(StageLayerBinding binding)
    {
        if (binding == null)
            return null;
        if (binding.tilemap != null)
            return binding.tilemap;
        if (binding.renderer != null)
            return binding.renderer;
        return binding.collider;
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

    private void CaptureInitialColliderState()
    {
        lockedColliders.Clear();
        if (bindings == null)
            return;

        foreach (StageLayerBinding binding in bindings)
        {
            if (binding == null || binding.collider == null)
                continue;

            if (!binding.collider.enabled)
                lockedColliders.Add(binding);
        }
    }

    private void ApplyState(double songTime)
    {
        if (bindings == null)
            return;

        float currentBeat = SecondsToBeat(songTime);

        foreach (StageLayerBinding binding in bindings)
        {
            if (binding == null || string.IsNullOrEmpty(binding.id))
                continue;

            bool visible = ResolveVisible(binding.id, currentBeat);
            bool physicallyPresent = ResolvePhysicallyPresent(binding, currentBeat);
            bool visibilityVisible = ResolveVisibility(binding, currentBeat);
            bool colliderLocked = lockedColliders.Contains(binding);
            ApplyVisible(
                binding,
                visible,
                physicallyPresent,
                visibilityVisible,
                colliderLocked);
        }
    }

    private bool ResolveVisible(string layerId, float currentBeat)
    {
        bool visible = GetDefaultVisible(layerId);

        if (visibilityEvents == null)
            return visible;

        foreach (StageLayerVisibilityEvent e in visibilityEvents)
        {
            if (e == null || e.layerId != layerId)
                continue;

            if (e.beat <= currentBeat + BeatEpsilon)
                visible = e.visible;
        }

        return visible;
    }

    private bool ResolvePhysicallyPresent(
        StageLayerBinding binding,
        float currentBeat)
    {
        float enterBeat = float.NegativeInfinity;
        float exitBeat = float.PositiveInfinity;

        foreach (IStageAppearanceAnimation appearance in
                 GetAppearanceComponents(binding))
        {
            enterBeat = Mathf.Max(enterBeat, appearance.CollisionEnterBeat);
            exitBeat = Mathf.Min(exitBeat, appearance.CollisionExitBeat);
        }

        return currentBeat >= enterBeat - BeatEpsilon &&
               currentBeat < exitBeat - BeatEpsilon;
    }

    private bool ResolveVisibility(
        StageLayerBinding binding,
        float currentBeat)
    {
        float enterBeat = float.NegativeInfinity;
        float exitBeat = float.PositiveInfinity;

        foreach (IStageVisibilityAnimation visibility in
                 GetVisibilityComponents(binding))
        {
            enterBeat = Mathf.Max(enterBeat, visibility.VisibilityEnterBeat);
            exitBeat = Mathf.Min(exitBeat, visibility.VisibilityExitBeat);
        }

        return currentBeat >= enterBeat - BeatEpsilon &&
               currentBeat < exitBeat - BeatEpsilon;
    }

    private bool GetDefaultVisible(string layerId)
    {
        if (defById.TryGetValue(layerId, out StageTilemapLayerDef def))
            return def.visibleByDefault;

        return true;
    }

    private float SecondsToBeat(double songTime)
    {
        if (chartPlayer != null && chartPlayer.BpmTimeline != null)
            return chartPlayer.BpmTimeline.SecondsToBeat((float)songTime);

        return (float)songTime * 2f; // 兜底 120 BPM
    }

    private static void ApplyVisible(
        StageLayerBinding binding,
        bool visible,
        bool physicallyPresent,
        bool visibilityVisible,
        bool colliderLocked)
    {
        bool rendererEnabled = visible && visibilityVisible;
        if (binding.renderer != null &&
            binding.renderer.enabled != rendererEnabled)
        {
            binding.renderer.enabled = rendererEnabled;
        }

        // 初始为关闭的碰撞体从头到尾不改变，交回场景手工控制。
        if (colliderLocked || binding.collider == null)
            return;

        bool colliderEnabled = visible && physicallyPresent;
        if (binding.collider.enabled != colliderEnabled)
        {
            binding.collider.enabled = colliderEnabled;
        }
    }
}
