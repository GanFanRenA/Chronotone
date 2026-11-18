using System;
using System.Collections.Generic;
using UnityEngine;

// 旧版按 NodeType 的配色，仅用于一次性迁移。
[Serializable]
public sealed class NodeTypeAppearance
{
    public NodeType type;
    [ColorUsage(false, false)] public Color nodeColor = Color.white;
    [ColorUsage(false, false)] public Color timelineColor = Color.white;
}

[Serializable]
public sealed class NodeActionAppearance
{
    [Tooltip("动作类型。")]
    public NodeAction action;

    [Tooltip("场景中 Node 使用的颜色。只修改 SpriteRenderer 的颜色，不修改精灵。")]
    [ColorUsage(false, false)]
    public Color nodeColor = Color.white;

    [Tooltip("Chart Timeline 中该动作使用的颜色。")]
    [ColorUsage(false, false)]
    public Color timelineColor = Color.white;
}

[CreateAssetMenu(
    menuName = "Rhythm/Node Appearance Set",
    fileName = "NodeAppearanceSet")]
public sealed class NodeAppearanceSet : ScriptableObject
{
    public const string DefaultResourcesPath =
        "NodeAppearance/NodeAppearanceSet";

    private static NodeAppearanceSet defaultAppearanceSet;

    [SerializeField]
    private List<NodeActionAppearance> actionAppearances =
        new List<NodeActionAppearance>();

    [SerializeField, HideInInspector]
    private List<NodeTypeAppearance> appearances =
        new List<NodeTypeAppearance>();

    [SerializeField, HideInInspector]
    private bool migrated;

    public static NodeAppearanceSet LoadDefault()
    {
        if (defaultAppearanceSet == null)
        {
            defaultAppearanceSet =
                Resources.Load<NodeAppearanceSet>(DefaultResourcesPath);
        }

        return defaultAppearanceSet;
    }

    public void EnsureMigrated()
    {
        if (migrated)
            return;

        MigrateLegacyAppearances();
        migrated = true;
    }

    public bool TryGetNodeColor(NodeAction action, out Color color)
    {
        EnsureMigrated();
        EnsureEveryActionHasAnEntry();
        if (TryGetAppearance(action, out NodeActionAppearance appearance))
        {
            color = appearance.nodeColor;
            return true;
        }

        color = Color.white;
        return false;
    }

    public bool TryGetTimelineColor(NodeAction action, out Color color)
    {
        EnsureMigrated();
        EnsureEveryActionHasAnEntry();
        if (TryGetAppearance(action, out NodeActionAppearance appearance))
        {
            color = appearance.timelineColor;
            return true;
        }

        color = Color.white;
        return false;
    }

    private bool TryGetAppearance(
        NodeAction action,
        out NodeActionAppearance appearance)
    {
        if (actionAppearances != null)
        {
            foreach (NodeActionAppearance candidate in actionAppearances)
            {
                if (candidate == null || candidate.action != action)
                    continue;

                appearance = candidate;
                return true;
            }
        }

        appearance = null;
        return false;
    }

    private void MigrateLegacyAppearances()
    {
        if (actionAppearances == null)
            actionAppearances = new List<NodeActionAppearance>();

        if (actionAppearances.Count > 0)
            return;

        NodeTypeAppearance jump =
            FindLegacy(NodeType.Jump) ?? FindLegacy(NodeType.GravityJump);
        NodeTypeAppearance fastFall =
            FindLegacy(NodeType.FastFall) ?? FindLegacy(NodeType.GravityFastFall);
        NodeTypeAppearance none =
            FindLegacy(NodeType.Gravity) ?? FindLegacy(NodeType.WeakGravity);
        NodeTypeAppearance respawn = FindLegacy(NodeType.Respawn);

        AddMigrated(NodeAction.None, none);
        AddMigrated(NodeAction.Jump, jump);
        AddMigrated(NodeAction.FastFall, fastFall);
        AddMigrated(NodeAction.Respawn, respawn);
    }

    private NodeTypeAppearance FindLegacy(NodeType type)
    {
        if (appearances == null)
            return null;

        foreach (NodeTypeAppearance candidate in appearances)
        {
            if (candidate != null && candidate.type == type)
                return candidate;
        }

        return null;
    }

    private void AddMigrated(
        NodeAction action,
        NodeTypeAppearance legacy)
    {
        actionAppearances.Add(new NodeActionAppearance
        {
            action = action,
            nodeColor = legacy != null
                ? legacy.nodeColor
                : DefaultNodeColor(action),
            timelineColor = legacy != null
                ? legacy.timelineColor
                : DefaultTimelineColor(action)
        });
    }

    private void EnsureEveryActionHasAnEntry()
    {
        if (actionAppearances == null)
            actionAppearances = new List<NodeActionAppearance>();

        foreach (NodeAction action in Enum.GetValues(typeof(NodeAction)))
        {
            if (TryGetAppearance(action, out _))
                continue;

            actionAppearances.Add(new NodeActionAppearance
            {
                action = action,
                nodeColor = DefaultNodeColor(action),
                timelineColor = DefaultTimelineColor(action)
            });
        }
    }

    private static Color DefaultNodeColor(NodeAction action)
    {
        Color color = DefaultTimelineColor(action);
        color.a = 0.7f;
        return color;
    }

    private static Color DefaultTimelineColor(NodeAction action)
    {
        switch (action)
        {
            case NodeAction.None:
                return new Color(0.2f, 0.65f, 1f, 1f);
            case NodeAction.FastFall:
                return new Color(1f, 0.28f, 0.18f, 1f);
            case NodeAction.Respawn:
                return new Color(0.15f, 1f, 0f, 1f);
            default:
                return new Color(1f, 0.75f, 0f, 1f);
        }
    }

#if UNITY_EDITOR
    public static event Action<NodeAppearanceSet> Changed;

    private bool refreshQueued;

    private void OnEnable()
    {
        EnsureMigrated();
        EnsureEveryActionHasAnEntry();
    }

    private void OnValidate()
    {
        EnsureMigrated();
        EnsureEveryActionHasAnEntry();

        if (refreshQueued)
            return;

        refreshQueued = true;
        UnityEditor.EditorApplication.delayCall += NotifyChanged;
    }

    private void NotifyChanged()
    {
        refreshQueued = false;
        if (this == null)
            return;

        foreach (NodeMarker marker in UnityEngine.Object.FindObjectsByType<NodeMarker>(
                     FindObjectsSortMode.None))
        {
            ChartPlayer owner = marker.GetComponentInParent<ChartPlayer>();
            NodeAppearanceSet effectiveSet = owner != null
                ? owner.ResolveNodeAppearanceSet()
                : LoadDefault();

            if (effectiveSet == this)
                marker.ApplyAppearance(this);
        }

        Changed?.Invoke(this);
        UnityEditor.SceneView.RepaintAll();
    }
#else
    private void OnEnable()
    {
        EnsureMigrated();
        EnsureEveryActionHasAnEntry();
    }
#endif
}
