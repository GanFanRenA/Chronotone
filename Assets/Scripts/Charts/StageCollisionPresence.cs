using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 外观型舞台动画（飞入飞出 / 淡入淡出）对外暴露的「物理在场」窗口。
/// 入场完成拍之前、出场开始拍之后，平台都尚未/不再物理在场。
/// </summary>
public interface IStageAppearanceAnimation
{
    /// <summary>入场完成的拍点；无入场动画时返回 float.NegativeInfinity（自始就在场）。</summary>
    float CollisionEnterBeat { get; }

    /// <summary>出场开始的拍点；无出场动画时返回 float.PositiveInfinity（永不退场）。</summary>
    float CollisionExitBeat { get; }
}

/// <summary>
/// 需要按时间窗口隐藏/显示渲染器的外观动画（当前只有飞入飞出）。
/// 在 [VisibilityEnterBeat, VisibilityExitBeat) 之外，渲染器应被隐藏。
/// </summary>
public interface IStageVisibilityAnimation
{
    /// <summary>开始可见的拍点；无飞入时返回 float.NegativeInfinity（自始可见）。</summary>
    float VisibilityEnterBeat { get; }

    /// <summary>变回隐藏的拍点；无飞出时返回 float.PositiveInfinity（永不因该动画隐藏）。</summary>
    float VisibilityExitBeat { get; }
}

/// <summary>
/// 场景里所有外观型平台碰撞体的「在场时间表」。
/// 供 PerfectRoute 模拟（烘焙 + 编辑器轨迹预览）按当前歌曲时间忽略
/// 「尚未入场 / 已退场」的碰撞体命中。
/// </summary>
public sealed class StageCollisionPresence
{
    private readonly struct Interval
    {
        public readonly double EnterSeconds;
        public readonly double ExitSeconds;

        public Interval(double enterSeconds, double exitSeconds)
        {
            EnterSeconds = enterSeconds;
            ExitSeconds = exitSeconds;
        }
    }

    private readonly Dictionary<Collider2D, Interval> intervals =
        new Dictionary<Collider2D, Interval>();

    /// <summary>某碰撞体在 songTime 是否物理在场。不在时间表里 = 没有外观约束，始终在场。</summary>
    public bool IsActive(Collider2D collider, double songTime)
    {
        if (collider == null)
            return true;

        if (!intervals.TryGetValue(collider, out Interval interval))
            return true;

        return songTime >= interval.EnterSeconds &&
               songTime < interval.ExitSeconds;
    }

    public static StageCollisionPresence Build(
        BpmTimeline bpmTimeline,
        GameObject[] roots)
    {
        var presence = new StageCollisionPresence();
        if (roots == null || roots.Length == 0)
            return presence;

        foreach (GameObject root in roots)
        {
            if (root == null)
                continue;

            MonoBehaviour[] behaviours =
                root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                IStageAppearanceAnimation appearance =
                    behaviour as IStageAppearanceAnimation;
                if (appearance == null)
                    continue;

                double enterSeconds = BeatToSeconds(
                    bpmTimeline,
                    appearance.CollisionEnterBeat,
                    double.NegativeInfinity);
                double exitSeconds = BeatToSeconds(
                    bpmTimeline,
                    appearance.CollisionExitBeat,
                    double.PositiveInfinity);

                Collider2D[] colliders =
                    behaviour.GetComponentsInChildren<Collider2D>(true);
                foreach (Collider2D collider in colliders)
                {
                    if (collider == null || collider.isTrigger)
                        continue;

                    presence.Merge(collider, enterSeconds, exitSeconds);
                }
            }
        }

        return presence;
    }

    private static double BeatToSeconds(
        BpmTimeline bpmTimeline,
        float beat,
        double infiniteFallback)
    {
        if (float.IsNegativeInfinity(beat))
            return double.NegativeInfinity;
        if (float.IsPositiveInfinity(beat))
            return double.PositiveInfinity;

        if (bpmTimeline != null)
            return bpmTimeline.BeatToSeconds(beat);

        return beat * 0.5d; // 兜底 120 BPM
    }

    private void Merge(
        Collider2D collider,
        double enterSeconds,
        double exitSeconds)
    {
        if (intervals.TryGetValue(collider, out Interval existing))
        {
            // 同一碰撞体被多个外观动画约束时：都入场才算在场，任一退场即离场。
            intervals[collider] = new Interval(
                System.Math.Max(existing.EnterSeconds, enterSeconds),
                System.Math.Min(existing.ExitSeconds, exitSeconds));
        }
        else
        {
            intervals[collider] = new Interval(enterSeconds, exitSeconds);
        }
    }
}
