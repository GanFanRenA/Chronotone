using System;
using UnityEngine;
using UnityEngine.Serialization;

public enum NodeType
{
    Jump = 0,
    Respawn = 1,
    Gravity = 2,
    FastFall = 3,
    GravityFastFall = 4,
    WeakGravity = 5,
    GravityJump = 6
}

public enum NodeTrigger
{
    Manual = 0,
    Auto = 1,
    None = 2
}

[Serializable]
public class NodeData
{
    public float beat;
    public int order;
    [Tooltip("节点的运动类型。")]
    [FormerlySerializedAs("mode")]
    public NodeType type = NodeType.Jump;
    [Tooltip("Manual 需要点击并判定；Auto 自动触发且不判定；None 不触发。Respawn 固定为 None。")]
    public NodeTrigger trigger = NodeTrigger.Manual;
    public Vector2 position;
    [HideInInspector] public float deltaX;

    public NodeTrigger EffectiveTrigger =>
        NormalizeTrigger(type, trigger);

    public bool IsTriggered =>
        EffectiveTrigger != NodeTrigger.None;

    public bool IsManuallyTriggered =>
        EffectiveTrigger == NodeTrigger.Manual;

    public static NodeTrigger NormalizeTrigger(
        NodeType type,
        NodeTrigger trigger)
    {
        return type == NodeType.Respawn
            ? NodeTrigger.None
            : trigger;
    }
}
