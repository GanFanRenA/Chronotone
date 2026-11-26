using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 节奏段：把谱面划分成可命名的 [startBeat, endBeat) 区间，用于在 Chart Timeline
/// 里整体平移其中所有「按 beat 定位」的元素。纯制谱数据，运行时无效果。
/// </summary>
[Serializable]
public sealed class RhythmSegment
{
    [Tooltip("节奏段名称。")]
    public string name = "段落";

    [Tooltip("起始拍（含）。")]
    public float startBeat;

    [Tooltip("结束拍（不含）。移动时主拍点在 [startBeat, endBeat) 内的元素一起平移。")]
    public float endBeat;
}

/// <summary>
/// 一个可被节奏段整体平移的「拍点组」。Beat 是主拍点（用于判断是否落在段内），
/// Offset 把该组内所有拍点整体平移 delta 拍（时长类字段保持不变）。
/// </summary>
public readonly struct BeatAnchor
{
    public BeatAnchor(float beat, Action<float> offset)
    {
        Beat = beat;
        Offset = offset;
    }

    public float Beat { get; }
    public Action<float> Offset { get; }
}

/// <summary>
/// 有「按 beat 定位」元素、可被节奏段整体平移的组件。
/// 实现 CollectBeatAnchors 把自身所有主拍点（及同组的从属拍点）暴露出来，
/// 新事件类型实现该接口后即可自动被节奏段纳入，无需改动编辑器代码。
/// </summary>
public interface IBeatMappable
{
    void CollectBeatAnchors(List<BeatAnchor> anchors);
}
