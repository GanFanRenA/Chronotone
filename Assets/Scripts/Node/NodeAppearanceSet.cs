using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public sealed class NodeTypeAppearance
{
    [FormerlySerializedAs("mode")]
    public NodeType type;

    [Tooltip("场景中 Node 使用的颜色。只修改 SpriteRenderer 的颜色，不修改精灵。")]
    [ColorUsage(false, false)]
    public Color nodeColor = Color.white;

    [Tooltip("Chart Timeline 中该类型使用的颜色。")]
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

    [SerializeField]
    private List<NodeTypeAppearance> appearances =
        new List<NodeTypeAppearance>();

    private static NodeAppearanceSet defaultAppearanceSet;

    public IReadOnlyList<NodeTypeAppearance> Appearances => appearances;

    public static NodeAppearanceSet LoadDefault()
    {
        if (defaultAppearanceSet == null)
        {
            defaultAppearanceSet =
                Resources.Load<NodeAppearanceSet>(DefaultResourcesPath);
        }

        return defaultAppearanceSet;
    }

    public bool TryGetAppearance(
        NodeType type,
        out NodeTypeAppearance appearance)
    {
        if (appearances != null)
        {
            foreach (NodeTypeAppearance candidate in appearances)
            {
                if (candidate == null || candidate.type != type)
                    continue;

                appearance = candidate;
                return true;
            }
        }

        appearance = null;
        return false;
    }

    public bool TryGetNodeColor(NodeType type, out Color color)
    {
        if (TryGetAppearance(type, out NodeTypeAppearance appearance))
        {
            color = appearance.nodeColor;
            return true;
        }

        color = Color.white;
        return false;
    }

    public bool TryGetTimelineColor(NodeType type, out Color color)
    {
        if (TryGetAppearance(type, out NodeTypeAppearance appearance))
        {
            color = appearance.timelineColor;
            return true;
        }

        color = Color.white;
        return false;
    }

#if UNITY_EDITOR
    public static event Action<NodeAppearanceSet> Changed;

    private bool refreshQueued;

    private void OnValidate()
    {
        EnsureEveryTypeHasAnEntry();

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

    private void EnsureEveryTypeHasAnEntry()
    {
        if (appearances == null)
            appearances = new List<NodeTypeAppearance>();

        foreach (NodeType type in Enum.GetValues(typeof(NodeType)))
        {
            if (TryGetAppearance(type, out _))
                continue;

            appearances.Add(new NodeTypeAppearance { type = type });
        }
    }
#endif
}
