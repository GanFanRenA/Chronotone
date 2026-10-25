using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class NodeModeAppearance
{
    public NodeMode mode;

    [Tooltip("场景中 Node 使用的颜色。只修改 SpriteRenderer 的颜色，不修改精灵。")]
    [ColorUsage(false, false)]
    public Color nodeColor = Color.white;

    [Tooltip("Chart Timeline 中该模式使用的颜色。")]
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
    private List<NodeModeAppearance> appearances =
        new List<NodeModeAppearance>();

    private static NodeAppearanceSet defaultAppearanceSet;

    public IReadOnlyList<NodeModeAppearance> Appearances => appearances;

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
        NodeMode mode,
        out NodeModeAppearance appearance)
    {
        if (appearances != null)
        {
            foreach (NodeModeAppearance candidate in appearances)
            {
                if (candidate == null || candidate.mode != mode)
                    continue;

                appearance = candidate;
                return true;
            }
        }

        appearance = null;
        return false;
    }

    public bool TryGetNodeColor(NodeMode mode, out Color color)
    {
        if (TryGetAppearance(mode, out NodeModeAppearance appearance))
        {
            color = appearance.nodeColor;
            return true;
        }

        color = Color.white;
        return false;
    }

    public bool TryGetTimelineColor(NodeMode mode, out Color color)
    {
        if (TryGetAppearance(mode, out NodeModeAppearance appearance))
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
        EnsureEveryModeHasAnEntry();

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

    private void EnsureEveryModeHasAnEntry()
    {
        if (appearances == null)
            appearances = new List<NodeModeAppearance>();

        foreach (NodeMode mode in Enum.GetValues(typeof(NodeMode)))
        {
            if (TryGetAppearance(mode, out _))
                continue;

            appearances.Add(new NodeModeAppearance { mode = mode });
        }
    }
#endif
}
