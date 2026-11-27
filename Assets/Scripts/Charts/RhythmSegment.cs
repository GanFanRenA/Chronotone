using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 节奏段实例：某模板在某起始拍处的一次放置。内容由模板提供，元素由模板展开生成。
/// 实例只记「哪个模板」+「放在哪拍」，因此移动实例 = 移动它展开出的元素。
/// </summary>
[Serializable]
public sealed class RhythmSegment
{
    [Tooltip("所属模板 id；为空表示无共享内容的独立段（兼容旧数据）。")]
    public string templateId;

    [Tooltip("实例唯一 id，用于标识其展开生成的元素。")]
    public string instanceId;

    [Tooltip("起始拍（该模板内容的绝对偏移）。")]
    public float startBeat;
}

/// <summary>
/// 节奏段模板：可复用的「乐句/段落」。内容以相对拍存储，实例 = 模板 + startBeat。
/// 更改模板内容会同步到所有实例。
/// </summary>
[Serializable]
public sealed class SegmentTemplate
{
    [Tooltip("模板唯一 id。")]
    public string id;

    [Tooltip("模板名称（所有实例共用，改名即同步）。")]
    public string name = "段落";

    [Tooltip("段长度（拍）。")]
    public float durationBeats;

    public List<SegmentTemplateNode> nodes = new List<SegmentTemplateNode>();
    public List<SegmentTemplateVfx> vfx = new List<SegmentTemplateVfx>();
}

/// <summary>模板里的一个节点（相对拍 + 节点属性）。</summary>
[Serializable]
public sealed class SegmentTemplateNode
{
    public float relativeBeat;
    public NodeAction action = NodeAction.Jump;
    public NodeGravity gravity = NodeGravity.None;
    public bool auto;
    public bool visible = true;
    public float strength;
    public float positionY;
}

/// <summary>模板里的一个屏幕震动（相对拍 + 震动属性）。</summary>
[Serializable]
public sealed class SegmentTemplateVfx
{
    public float relativeBeat;
    public float durationBeats = 1f;
    public float strength = 0.25f;
    public float frequency = 1f;
}

/// <summary>
/// 一个可被整体平移的「拍点组」。Beat 是主拍点，Offset 把该组所有拍点平移 delta 拍。
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
/// 有「按 beat 定位」元素、可被整体平移的组件。新事件类型实现该接口即可被纳入。
/// </summary>
public interface IBeatMappable
{
    void CollectBeatAnchors(List<BeatAnchor> anchors);
}
