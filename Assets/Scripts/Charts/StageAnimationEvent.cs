using System.Collections.Generic;
using UnityEngine;

/// <summary>动画事件大类，用于 Chart Timeline 上色与图例。</summary>
public enum StageAnimationEventKind
{
    LayerVisibility = 0,
    Fade = 1,
    Fly = 2
}

/// <summary>
/// 场景动画事件的一个时间点或一段区间，供 Chart Timeline 显示。
/// beat 是主锚点；当 endBeat 大于 beat 时表示一段区间（例如配套的「淡入 → 淡出」）。
/// </summary>
public readonly struct StageAnimationEvent
{
    public readonly float beat;
    public readonly float endBeat;
    public readonly StageAnimationEventKind kind;
    public readonly string label;
    public readonly Object owner;

    public StageAnimationEvent(
        float beat,
        StageAnimationEventKind kind,
        string label,
        Object owner)
        : this(beat, 0f, kind, label, owner)
    {
    }

    public StageAnimationEvent(
        float beat,
        float endBeat,
        StageAnimationEventKind kind,
        string label,
        Object owner)
    {
        this.beat = Mathf.Max(0f, beat);
        this.endBeat = Mathf.Max(0f, endBeat);
        this.kind = kind;
        this.label = label;
        this.owner = owner;
    }

    public bool IsSpan => endBeat > beat;
}

/// <summary>
/// 实现该接口的组件会把自己的动画事件提供给 Chart Timeline。
/// 以后新增动画组件时实现同一接口即可自动纳入时间轴，无需改动窗口代码。
/// </summary>
public interface IStageAnimationEventSource
{
    void CollectAnimationEvents(List<StageAnimationEvent> events);
}
