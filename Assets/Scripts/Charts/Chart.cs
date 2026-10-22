using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Chart : MonoBehaviour
{
    [SerializeField] private List<Node> nodes = new List<Node>();

    public IReadOnlyList<Node> Nodes => nodes;

    // 全量扫描自己的子树，排序，写回 index
    public void Build()
    {
        nodes.Clear();
        GetComponentsInChildren(false, nodes);   // false = 只收启用的节点
        nodes.Sort((a, b) =>
        {
            int byBeat = a.beat.CompareTo(b.beat);
            return byBeat != 0 ? byBeat : a.order.CompareTo(b.order);
        });
        for (int i = 0; i < nodes.Count; i++)
            nodes[i].index = i;
    }

    [ContextMenu("Refresh Chart")]
    private void RefreshFromMenu() => Build();

#if UNITY_EDITOR
    private void OnEnable() => QueueRefresh();

    public void QueueRefresh()
    {
        EditorApplication.delayCall -= Refresh;
        EditorApplication.delayCall += Refresh;
    }

    private void Refresh()
    {
        if (this == null) return;
        if (Application.isPlaying) return;
        Build();
    }
#endif
}