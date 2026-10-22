using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class ChartPlayer : MonoBehaviour
{
    public ChartSO chart;
    public Transform scanRoot;
    public bool autoBake = false;   // 想要旧行为就勾上

#if UNITY_EDITOR
    [System.NonSerialized] public bool dirty;

    [InitializeOnLoadMethod]
    private static void HookPlayMode()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;
        foreach (var p in Object.FindObjectsByType<ChartPlayer>(FindObjectsSortMode.None))
            if (p.dirty) p.Bake();
    }

    // 由 Marker 调用：只标记，不写盘
    public void MarkDirty()
    {
        if (dirty) return;
        dirty = true;
        EditorUtility.SetDirty(this);       // 刷新 Inspector 状态
        if (autoBake) QueueBake();
    }

    // 由 autoBake 触发：合并同帧多次调用
    private void QueueBake()
    {
        EditorApplication.delayCall -= Bake;
        EditorApplication.delayCall += Bake;
    }

    [ContextMenu("Bake From Scene")]
    public void Bake()
    {
        if (this == null || chart == null || Application.isPlaying) return;

        Transform root = scanRoot != null ? scanRoot : transform;
        NodeMarker[] markers = root.GetComponentsInChildren<NodeMarker>(false);

        System.Array.Sort(markers, (a, b) =>
        {
            int byBeat = a.beat.CompareTo(b.beat);
            return byBeat != 0 ? byBeat : a.order.CompareTo(b.order);
        });

        var result = new List<NodeData>(markers.Length);
        for (int i = 0; i < markers.Length; i++)
        {
            NodeMarker m = markers[i];
            m.index = i;

            string newName = $"Node_{i}:{m.beat:0.##}_{m.order}";
            if (m.name != newName) m.name = newName;

            m.transform.SetSiblingIndex(i);

            result.Add(new NodeData
            {
                beat = m.beat,
                order = m.order,
                position = m.transform.position
            });
        }

        chart.nodes = result;
        EditorUtility.SetDirty(chart);
        AssetDatabase.SaveAssets();

        dirty = false;
        EditorUtility.SetDirty(this);
    }
#endif
}