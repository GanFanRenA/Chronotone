using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 场景「显示」动画组件（动画组件系列之一）。
/// 按 beat 定时对目标做淡入 / 淡出，只改透明度（Tilemap.color / SpriteRenderer.color），
/// 从头到尾不改变碰撞体，也不参与 StageSceneController 的物理在场调度。
/// Platform 的碰撞开关由 StageSceneController 的硬切换事件与飞入飞出动画负责。
///
/// 目标与时间源全部自动获取，不在 Inspector 暴露：
/// - 目标：优先本物体、其次子物体上的 Tilemap / SpriteRenderer。
/// - ChartPlayer：场景内首个 ChartPlayer；Player：由其 ResolvePlayer() 解析。
/// 时间用 Player.SongTime 驱动，因此死亡回溯会随 SongTime 自动回退到对应透明度。
///
/// 语义：
/// - 淡入：从「结束拍 - 时长」开始 alpha=0，经过 fadeInDurationBeats 拍淡入到基础透明度，在结束拍完全出现。
/// - 淡出：从「开始拍」自基础透明度出发，经过 fadeOutDurationBeats 拍淡出到 0（消失）。
/// - 两者可同时启用，按时间轴相乘叠加。
/// </summary>
[DisallowMultipleComponent]
public sealed class StageDisplayAnimation :
    MonoBehaviour,
    IStageAnimationEventSource,
    IStageAnimationSource
{
    private enum TargetKind
    {
        None = 0,
        Tilemap = 1,
        SpriteRenderer = 2
    }

    [Header("Fade In（淡入）")]
    [Tooltip("勾选后：从「结束拍 - 时长」开始淡入，经过时长在结束拍完全出现（恢复到基础透明度）。")]
    [SerializeField] private bool fadeInEnabled;
    [Tooltip("淡入结束拍点（完全出现的拍点）。")]
    [SerializeField] private float fadeInEndBeat;
    [Tooltip("淡入持续拍数，默认 1（即从「结束拍 - 1 拍」开始淡入）。")]
    [Min(0f)]
    [SerializeField] private float fadeInDurationBeats = 1f;

    [Header("Fade Out（淡出）")]
    [Tooltip("勾选后：从「开始拍」自基础透明度出发，经过时长淡出到完全透明（消失）。")]
    [SerializeField] private bool fadeOutEnabled;
    [Tooltip("淡出开始拍点（开始淡出的拍点）。")]
    [SerializeField] private float fadeOutStartBeat;
    [Tooltip("淡出持续拍数，默认 1。")]
    [Min(0f)]
    [SerializeField] private float fadeOutDurationBeats = 1f;

    private ChartPlayer chartPlayer;
    private Player resolvedPlayer;
    private TargetKind targetKind;
    private Tilemap tilemap;
    private SpriteRenderer spriteRenderer;
    private SpriteRenderer gravityRenderer;
    private Color baseColor = Color.white;
    private Color gravityBaseColor = Color.white;
    private StageAnimationDriver driver;
    private bool driverDriven;

    private void Awake()
    {
        ResolveTimeSource();
        ResolveTarget();
    }

    private void OnEnable()
    {
        driver = StageAnimationDriver.FindDriver();
        driverDriven = driver != null;
        if (!driverDriven)
        {
            StageAnimationDriver.WarnMissingDriver(this);
            return;
        }

        if (fadeInEnabled)
        {
            driver.RegisterWindow(
                this,
                fadeInEndBeat - fadeInDurationBeats,
                fadeInEndBeat);
        }

        if (fadeOutEnabled)
        {
            driver.RegisterWindow(
                this,
                fadeOutStartBeat,
                fadeOutStartBeat + fadeOutDurationBeats);
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
        // Node.prefab places its visible renderer on the nested Sprite/Apperance
        // object. NodeMarker may apply the action color during Awake, so capture
        // the base color after all Awake callbacks have completed.
        if (targetKind != TargetKind.None)
            baseColor = GetColor();
        if (gravityRenderer != null)
            gravityBaseColor = gravityRenderer.color;

        if (targetKind == TargetKind.None)
        {
            Debug.LogWarning(
                "StageDisplayAnimation 未找到可淡入淡出的目标" +
                "（Tilemap 或 SpriteRenderer）。",
                this);
        }

        ApplyAlpha(ComputeAlpha(double.NegativeInfinity));
    }

    private void Update()
    {
        if (driverDriven)
            return;

        double songTime = resolvedPlayer != null
            ? resolvedPlayer.SongTime
            : 0d;
        ApplyAlpha(ComputeAlpha(songTime));
    }

    public void ApplyAt(double songTime)
    {
        ApplyAlpha(ComputeAlpha(songTime));
    }

    public void CollectAnimationEvents(List<StageAnimationEvent> events)
    {
        if (events == null)
            return;

        if (fadeInEnabled && fadeOutEnabled && fadeOutStartBeat > fadeInEndBeat)
        {
            events.Add(new StageAnimationEvent(
                fadeInEndBeat,
                fadeOutStartBeat,
                StageAnimationEventKind.Fade,
                "淡入→淡出",
                gameObject));
            return;
        }

        if (fadeInEnabled)
            events.Add(new StageAnimationEvent(
                fadeInEndBeat, StageAnimationEventKind.Fade, "淡入", gameObject));

        if (fadeOutEnabled)
            events.Add(new StageAnimationEvent(
                fadeOutStartBeat, StageAnimationEventKind.Fade, "淡出", gameObject));
    }

    private void ResolveTimeSource()
    {
        if (chartPlayer == null)
        {
            ChartPlayer[] candidates =
                Object.FindObjectsByType<ChartPlayer>(FindObjectsSortMode.None);
            chartPlayer = candidates.Length > 0 ? candidates[0] : null;
        }

        resolvedPlayer = chartPlayer != null
            ? chartPlayer.ResolvePlayer()
            : null;
    }

    private void ResolveTarget()
    {
        if (TryGetComponent(out Tilemap ownTilemap))
        {
            tilemap = ownTilemap;
            targetKind = TargetKind.Tilemap;
            return;
        }

        if (TryGetComponent(out SpriteRenderer ownSprite))
        {
            spriteRenderer = ownSprite;
            targetKind = TargetKind.SpriteRenderer;
            return;
        }

        Tilemap childTilemap = GetComponentInChildren<Tilemap>(true);
        if (childTilemap != null)
        {
            tilemap = childTilemap;
            targetKind = TargetKind.Tilemap;
            return;
        }

        SpriteRenderer[] childSprites = GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer fallbackSprite = null;
        foreach (SpriteRenderer childSprite in childSprites)
        {
            if (childSprite != null && IsGravityIndicator(childSprite.transform))
            {
                gravityRenderer = childSprite;
                break;
            }
        }

        foreach (SpriteRenderer childSprite in childSprites)
        {
            if (childSprite == null || IsGravityIndicator(childSprite.transform))
                continue;

            if (childSprite.gameObject.name == "Apperance")
            {
                spriteRenderer = childSprite;
                targetKind = TargetKind.SpriteRenderer;
                return;
            }

            if (fallbackSprite == null)
                fallbackSprite = childSprite;
        }

        if (fallbackSprite != null)
        {
            spriteRenderer = fallbackSprite;
            targetKind = TargetKind.SpriteRenderer;
        }
    }

    private static bool IsGravityIndicator(Transform candidate)
    {
        for (Transform current = candidate; current != null; current = current.parent)
        {
            if (current.name == "GravityNode" || current.name == "GravityDot")
                return true;
        }

        return false;
    }

    private Color GetColor()
    {
        switch (targetKind)
        {
            case TargetKind.Tilemap:
                return tilemap != null ? tilemap.color : Color.white;
            case TargetKind.SpriteRenderer:
                return spriteRenderer != null ? spriteRenderer.color : Color.white;
            default:
                return Color.white;
        }
    }

    private void ApplyAlpha(float alpha)
    {
        Color color = baseColor;
        color.a = alpha;

        switch (targetKind)
        {
            case TargetKind.Tilemap:
                if (tilemap != null && tilemap.color != color)
                    tilemap.color = color;
                break;

            case TargetKind.SpriteRenderer:
                if (spriteRenderer != null && spriteRenderer.color != color)
                    spriteRenderer.color = color;
                break;
        }

        if (gravityRenderer != null)
        {
            Color gravityColor = gravityBaseColor;
            gravityColor.a *= alpha;
            if (gravityRenderer.color != gravityColor)
                gravityRenderer.color = gravityColor;
        }
    }

    private float ComputeAlpha(double songTime)
    {
        if (targetKind == TargetKind.None)
            return baseColor.a;

        float fadeIn = fadeInEnabled ? FadeInFactor(songTime) : 1f;
        float fadeOut = fadeOutEnabled ? FadeOutFactor(songTime) : 1f;
        return baseColor.a * fadeIn * fadeOut;
    }

    private float FadeInFactor(double songTime)
    {
        double start = BeatToSeconds(fadeInEndBeat - fadeInDurationBeats);
        double end = BeatToSeconds(fadeInEndBeat);
        return FadeProgress(songTime, start, end, 0f, 1f);
    }

    private float FadeOutFactor(double songTime)
    {
        double start = BeatToSeconds(fadeOutStartBeat);
        double end = BeatToSeconds(fadeOutStartBeat + fadeOutDurationBeats);
        return FadeProgress(songTime, start, end, 1f, 0f);
    }

    private static float FadeProgress(
        double songTime,
        double startSeconds,
        double endSeconds,
        float from,
        float to)
    {
        double duration = endSeconds - startSeconds;
        if (duration <= 0d)
            return songTime < startSeconds ? from : to;

        double progress = System.Math.Clamp(
            (songTime - startSeconds) / duration,
            0d,
            1d);
        return Mathf.Lerp(from, to, (float)progress);
    }

    private double BeatToSeconds(float beat)
    {
        if (chartPlayer != null && chartPlayer.BpmTimeline != null)
            return chartPlayer.BpmTimeline.BeatToSeconds(beat);

        return beat * 0.5d; // 兜底 120 BPM
    }

#if UNITY_EDITOR
    /// <summary>编辑器里淡入淡出参数变化后触发，让 Chart Timeline 立即失效预览并重绘。</summary>
    public static event System.Action EditorChanged;

    private void OnValidate() => EditorChanged?.Invoke();
#endif
}
