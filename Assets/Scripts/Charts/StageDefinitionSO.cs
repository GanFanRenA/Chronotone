using UnityEngine;

[CreateAssetMenu(
    menuName = "Rhythm/Stage Definition",
    fileName = "NewStage")]
public sealed class StageDefinitionSO : ScriptableObject
{
    [SerializeField] private ChartSO chart;
    [SerializeField] private ChartTrackSO track;

    public ChartSO Chart => chart;
    public ChartTrackSO Track => track;
    public bool IsComplete =>
        chart != null &&
        track != null;
}
