using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 场景「飞入 / 飞出」动画组件（动画组件系列之一）。
/// 按 beat 定时移动物体自身 Transform，只改 localPosition，不碰碰撞体开关。
///
/// 目标（自身 Transform）与时间源全部自动获取，不在 Inspector 暴露：
/// - ChartPlayer：场景内首个 ChartPlayer；Player：由其 ResolvePlayer() 解析。
/// 时间用 Player.SongTime 驱动，因此死亡回溯会随 SongTime 自动回退到对应位置。
///
/// 语义（原始位置 = 物体在场景里摆好的位置；偏移点 = 原始位置 + 角度方向 × Distance）：
/// - 飞入：从「结束拍 - Duration」开始在偏移点，经过 Duration 飞到原始位置，在结束拍抵达。
/// - 飞出：从「开始拍」自原始位置出发，经过 Duration 飞到偏移点。
/// - 两者可同时启用，偏移按向量相加（正常使用两者不重叠）。
/// </summary>
[DisallowMultipleComponent]
public sealed class StageFlyAnimation : MonoBehaviour, IStageAnimationEventSource
{
    [Header("Fly In（飞入）")]
    [Tooltip("勾选后：从「结束拍 - 时长」开始在偏移点，经过时长飞到原始位置，在结束拍抵达。")]
    [SerializeField] private bool flyInEnabled;
    [Tooltip("偏移距离（世界单位），默认 10。")]
    [SerializeField] private float flyInDistance = 10f;
    [Tooltip("偏移方向角度（度；0°=右 +X，逆时针为正）。正下方 = -90°。")]
    [SerializeField] private float flyInAngle = -90f;
    [Tooltip("飞行时长（拍），默认 1。")]
    [Min(0f)]
    [SerializeField] private float flyInDurationBeats = 1f;
    [Tooltip("飞入结束拍点（抵达原始位置的拍点）。")]
    [SerializeField] private float flyInEndBeat;

    [Header("Fly Out（飞出）")]
    [Tooltip("勾选后：从「开始拍」自原始位置出发，经过时长飞到偏移点。")]
    [SerializeField] private bool flyOutEnabled;
    [Tooltip("偏移距离（世界单位），默认 10。")]
    [SerializeField] private float flyOutDistance = 10f;
    [Tooltip("偏移方向角度（度；0°=右 +X，逆时针为正）。正下方 = -90°。")]
    [SerializeField] private float flyOutAngle = -90f;
    [Tooltip("飞行时长（拍），默认 1。")]
    [Min(0f)]
    [SerializeField] private float flyOutDurationBeats = 1f;
    [Tooltip("飞出开始拍点（自原始位置出发的拍点）。")]
    [SerializeField] private float flyOutStartBeat;

    private ChartPlayer chartPlayer;
    private Player resolvedPlayer;
    private Vector3 originalLocalPosition;

    private void Awake()
    {
        ResolveTimeSource();
        originalLocalPosition = transform.localPosition;
    }

    private void Start()
    {
        ApplyOffset(ComputeOffset(double.NegativeInfinity));
    }

    private void Update()
    {
        double songTime = resolvedPlayer != null
            ? resolvedPlayer.SongTime
            : 0d;
        ApplyOffset(ComputeOffset(songTime));
    }

    public void CollectAnimationEvents(List<StageAnimationEvent> events)
    {
        if (events == null)
            return;

        if (flyInEnabled && flyOutEnabled && flyOutStartBeat > flyInEndBeat)
        {
            events.Add(new StageAnimationEvent(
                flyInEndBeat,
                flyOutStartBeat,
                StageAnimationEventKind.Fly,
                "飞入→飞出",
                gameObject));
            return;
        }

        if (flyInEnabled)
            events.Add(new StageAnimationEvent(
                flyInEndBeat, StageAnimationEventKind.Fly, "飞入", gameObject));

        if (flyOutEnabled)
            events.Add(new StageAnimationEvent(
                flyOutStartBeat, StageAnimationEventKind.Fly, "飞出", gameObject));
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

    private Vector3 ComputeOffset(double songTime)
    {
        Vector3 offset = Vector3.zero;

        if (flyInEnabled)
            offset += FlyInOffset(songTime);

        if (flyOutEnabled)
            offset += FlyOutOffset(songTime);

        return offset;
    }

    private Vector3 FlyInOffset(double songTime)
    {
        double start = BeatToSeconds(flyInEndBeat - flyInDurationBeats);
        double end = BeatToSeconds(flyInEndBeat);
        float remaining = 1f - Progress01(songTime, start, end);
        return Direction(flyInAngle) * (flyInDistance * remaining);
    }

    private Vector3 FlyOutOffset(double songTime)
    {
        double start = BeatToSeconds(flyOutStartBeat);
        double end = BeatToSeconds(flyOutStartBeat + flyOutDurationBeats);
        float progress = Progress01(songTime, start, end);
        return Direction(flyOutAngle) * (flyOutDistance * progress);
    }

    private static float Progress01(
        double songTime,
        double startSeconds,
        double endSeconds)
    {
        double duration = endSeconds - startSeconds;
        if (duration <= 0d)
            return songTime < startSeconds ? 0f : 1f;

        double progress = System.Math.Clamp(
            (songTime - startSeconds) / duration,
            0d,
            1d);
        return (float)progress;
    }

    private static Vector3 Direction(float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
    }

    private void ApplyOffset(Vector3 offset)
    {
        Vector3 target = originalLocalPosition + offset;
        if ((transform.localPosition - target).sqrMagnitude > 1e-12f)
            transform.localPosition = target;
    }

    private double BeatToSeconds(float beat)
    {
        if (chartPlayer != null && chartPlayer.BpmTimeline != null)
            return chartPlayer.BpmTimeline.BeatToSeconds(beat);

        return beat * 0.5d; // 兜底 120 BPM
    }
}
