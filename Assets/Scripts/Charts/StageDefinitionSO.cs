using System.IO;
using UnityEngine;

[CreateAssetMenu(
    menuName = "Rhythm/Stage Definition",
    fileName = "NewStage")]
public sealed class StageDefinitionSO : ScriptableObject
{
    [SerializeField] private string scenePath;
    [SerializeField] private ChartSO chart;
    [SerializeField] private ChartTrackSO track;

    public string ScenePath => scenePath;
    public string SceneName =>
        string.IsNullOrEmpty(scenePath)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(scenePath);
    public ChartSO Chart => chart;
    public ChartTrackSO Track => track;
    public bool IsComplete =>
        !string.IsNullOrEmpty(scenePath) &&
        chart != null &&
        track != null;
}
