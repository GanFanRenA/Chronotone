using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rhythm/Chart", fileName = "NewChart")]
public class ChartSO : ScriptableObject
{
    public AudioClip clip;
    public List<NodeData> nodes = new List<NodeData>();
    public BpmTimeline bpmTimeline = new BpmTimeline();
}