using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rhythm/Chart", fileName = "NewChart")]
public class ChartSO : ScriptableObject
{
    public AudioClip clip;
    public List<NodeData> nodes = new List<NodeData>();
    public BpmTimeline bpmTimeline = new BpmTimeline();

    [Tooltip("全局偏移，单位为拍。正值让谱面整体晚于音乐。")]
    public float globalOffsetBeats;

    public float GlobalOffsetSeconds =>
        bpmTimeline != null ? bpmTimeline.BeatToSeconds(globalOffsetBeats) : 0f;

    public void UpdatePos(float baseX)
    {
        if (nodes == null || nodes.Count == 0)
            return;

        nodes.Sort((a, b) =>
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            int byBeat = a.beat.CompareTo(b.beat);
            return byBeat != 0 ? byBeat : a.order.CompareTo(b.order);
        });

        bpmTimeline?.RebuildDistanceCache();

        float previousBeat = 0f;
        float previousX = baseX;
        foreach (NodeData node in nodes)
        {
            if (node == null) continue;

            float beat = Mathf.Max(0f, node.beat);
            float deltaX = bpmTimeline != null
                ? bpmTimeline.DistanceBetweenBeats(previousBeat, beat)
                : (beat - previousBeat) * 60f / 120f * 5f;

            previousX += deltaX;
            node.deltaX = deltaX;
            node.position = new Vector2(previousX, node.position.y);
            previousBeat = beat;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
            ChartPlayer.RequestPositionUpdates(this);
    }
#endif
}
