using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 场景「显示」动画组件（动画组件系列之一）。
/// 按 beat 定时对目标做淡入 / 淡出，只改透明度（Tilemap.color / SpriteRenderer.color），
/// 不碰碰撞体。Platform 的碰撞开关仍由 StageSceneController 的硬切换事件负责。
///
/// 目标与时间源全部自动获取，不在 Inspector 暴露：
/// - 目标：优先本物体、其次子物体上的 Tilemap / SpriteRenderer。
/// - ChartPlayer：场景内首个 ChartPlayer；Player：由其 ResolvePlayer() 解析。
/// 时间用 Player.SongTime 驱动，因此死亡回溯会随 SongTime 自动回退到对应透明度。
///
/// 语义：
/// - 淡入：起始拍之前 alpha=0，经过 fadeInDurationBeats 拍淡入到基础透明度。
/// - 淡出：起始拍之前 alpha=基础透明度，经过 fadeOutDurationBeats 拍淡出到 0。
/// - 两者可同时启用，按时间轴相乘叠加。
/// </summary>
[DisallowMultipleComponent]
public sealed class StageDisplayAnimation : MonoBehaviour
{
    private enum TargetKind
    {
        None = 0,
        Tilemap = 1,
        SpriteRenderer = 2
    }

    [Header("Fade In（淡入）")]
    [Tooltip("勾选后：在淡入起始拍之前完全透明，经过淡入时长后恢复到基础透明度。")]
    [SerializeField] private bool fadeInEnabled;
    [Tooltip("淡入起始拍点（相对谱面）。")]
    [SerializeField] private float fadeInStartBeat;
    [Tooltip("淡入持续拍数，默认 1（即从起始拍经过 1 拍淡入完成）。")]
    [Min(0f)]
    [SerializeField] private float fadeInDurationBeats = 1f;

    [Header("Fade Out（淡出）")]
    [Tooltip("勾选后：在淡出起始拍之前保持基础透明度，经过淡出时长后完全透明。")]
    [SerializeField] private bool fadeOutEnabled;
    [Tooltip("淡出起始拍点（相对谱面）。")]
    [SerializeField] private float fadeOutStartBeat;
    [Tooltip("淡出持续拍数，默认 1。")]
    [Min(0f)]
    [SerializeField] private float fadeOutDurationBeats = 1f;

    private ChartPlayer chartPlayer;
    private Player resolvedPlayer;
    private TargetKind targetKind;
    private Tilemap tilemap;
    private SpriteRenderer spriteRenderer;
    private Color baseColor = Color.white;

    private void Awake()
    {
        ResolveTimeSource();
        ResolveTarget();
        baseColor = GetColor();
    }

    private void Start()
    {
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
        double songTime = resolvedPlayer != null
            ? resolvedPlayer.SongTime
            : 0d;
        ApplyAlpha(ComputeAlpha(songTime));
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

        SpriteRenderer childSprite = GetComponentInChildren<SpriteRenderer>(true);
        if (childSprite != null)
        {
            spriteRenderer = childSprite;
            targetKind = TargetKind.SpriteRenderer;
        }
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
        double start = BeatToSeconds(fadeInStartBeat);
        double end = BeatToSeconds(fadeInStartBeat + fadeInDurationBeats);
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
}
