using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    menuName = "Rhythm/Chart Track",
    fileName = "NewChartTrack")]
[PreferBinarySerialization]
public sealed class ChartTrackSO : ScriptableObject
{
    [SerializeField] private List<PerfectRouteSample> samples =
        new List<PerfectRouteSample>();
    [SerializeField] private bool blocked;
    [SerializeField] private bool complete;

    public IReadOnlyList<PerfectRouteSample> Samples => samples;
    public bool Blocked => blocked;
    public bool IsComplete => complete;
    public bool HasSamples => samples != null && samples.Count > 0;

    internal void Set(PerfectRoute route)
    {
        if (samples == null)
            samples = new List<PerfectRouteSample>();

        samples.Clear();
        if (route == null)
        {
            blocked = false;
            complete = false;
            return;
        }

        samples.AddRange(route.Samples);
        blocked = route.Blocked;
        complete = route.IsComplete;
    }

    public PerfectRoute CreateRoute()
    {
        return HasSamples
            ? new PerfectRoute(samples.ToArray(), blocked, complete)
            : null;
    }
}
