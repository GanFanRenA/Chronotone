using System;
using UnityEngine;

/// <summary>
/// 节点动作。None 只做重力/标记，Respawn 用于保存重生检查点。
/// </summary>
public enum NodeAction
{
    None = 0,
    Jump = 1,
    FastFall = 2,
    Respawn = 3
}

/// <summary>
/// 节点对重力的作用。None 不改变，Normal 强制正常，Reset 反转并把垂直速度清零。
/// </summary>
public enum NodeGravity
{
    None = 0,
    Normal = 1,
    Reset = 2
}

// 旧版节点类型，仅用于把已有谱面/场景数据迁移到新字段。
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

// 旧版触发方式，仅用于迁移。
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

    [Tooltip("动作类型：None 无动作 / Jump 跳跃 / FastFall 快速下落 / Respawn 重生点。")]
    public NodeAction action = NodeAction.Jump;

    [Tooltip("重力：None 不改变 / Normal 强制正常 / Reset 反转并把垂直速度清零。")]
    public NodeGravity gravity = NodeGravity.None;

    [Tooltip("自动：不判定、不计分、不显示判定提示，始终跟随轨道；Respawn 自动时经过即保存检查点。")]
    public bool auto;

    [Tooltip("显示：关闭后只在运行时隐藏节点，编辑模式仍然可见。")]
    public bool visible = true;

    [Tooltip("动作力度：Jump 为起跳速度，FastFall 为向下速度脉冲（越大落得越快），Respawn 无效。0 表示使用 GameSettings 默认值。")]
    [Min(0f)]
    public float strength;

    public Vector2 position;
    [HideInInspector] public float deltaX;

    [HideInInspector] public NodeType type;
    [HideInInspector] public NodeTrigger trigger;
    [HideInInspector, SerializeField] private bool migrated;

    public bool IsManuallyTriggered => !auto;

    public bool HasRouteEffect =>
        action == NodeAction.Jump ||
        action == NodeAction.FastFall ||
        gravity != NodeGravity.None;

    /// <summary>
    /// 0 或负数表示沿用 GameSettings 的默认力度。
    /// </summary>
    public static float ResolveStrength(
        float strength,
        float defaultStrength) =>
        strength > 0f ? strength : defaultStrength;

    /// <summary>
    /// 动作在 GameSettings 中的默认力度：Jump 用 JumpForce，FastFall 用默认速度脉冲，Respawn 没有力度。
    /// </summary>
    public static float GetDefaultStrength(
        NodeAction action,
        GameSettingsSO settings)
    {
        switch (action)
        {
            case NodeAction.Jump:
                return settings != null ? settings.JumpVelocity : 8f;

            case NodeAction.FastFall:
                return settings != null
                    ? settings.FastFallImpulse
                    : 60f;

            default:
                return 0f;
        }
    }

    public void MarkMigrated()
    {
        migrated = true;
    }

    public void EnsureMigrated()
    {
        if (migrated)
            return;

        // 旧 Jump + Manual 映射后正好等于新字段默认值；
        // 跳过可以避免覆盖已经在用新字段的新数据。
        if (type == NodeType.Jump && trigger == NodeTrigger.Manual)
        {
            migrated = true;
            return;
        }

        MigrateLegacy(
            type,
            trigger,
            out NodeAction migratedAction,
            out NodeGravity migratedGravity,
            out bool migratedAuto);
        action = migratedAction;
        gravity = migratedGravity;
        auto = migratedAuto;
        visible = true;
        migrated = true;
    }

    public static void MigrateLegacy(
        NodeType legacyType,
        NodeTrigger legacyTrigger,
        out NodeAction action,
        out NodeGravity gravity,
        out bool auto)
    {
        action = NodeAction.None;
        gravity = NodeGravity.None;
        switch (legacyType)
        {
            case NodeType.Jump:
                action = NodeAction.Jump;
                break;

            case NodeType.Respawn:
                action = NodeAction.Respawn;
                break;

            case NodeType.Gravity:
                gravity = NodeGravity.Reset;
                break;

            case NodeType.FastFall:
                action = NodeAction.FastFall;
                break;

            case NodeType.GravityFastFall:
                action = NodeAction.FastFall;
                gravity = NodeGravity.Reset;
                break;

            case NodeType.WeakGravity:
                gravity = NodeGravity.Reset;
                break;

            case NodeType.GravityJump:
                action = NodeAction.Jump;
                gravity = NodeGravity.Reset;
                break;
        }

        // 旧版 Auto / None 都不参与判定。
        auto = legacyTrigger != NodeTrigger.Manual;
    }
}
