using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    menuName = "Rhythm/Chart Track",
    fileName = "NewChartTrack")]
[PreferBinarySerialization]
public sealed class ChartTrackSO : ScriptableObject
{
    private const int CurrentRouteVersion = 2;

    [SerializeField] private List<PerfectRouteSample> samples =
        new List<PerfectRouteSample>();
    [SerializeField] private bool blocked;
    [SerializeField] private bool complete;
    [SerializeField, HideInInspector] private int routeVersion;

    public IReadOnlyList<PerfectRouteSample> Samples => samples;
    public bool Blocked => blocked;
    public bool IsComplete => complete;
    public bool HasSamples => samples != null && samples.Count > 0;
    public bool HasCurrentRouteVersion =>
        routeVersion >= CurrentRouteVersion;

    internal void Set(PerfectRoute route)
    {
        if (samples == null)
            samples = new List<PerfectRouteSample>();

        samples.Clear();
        if (route == null)
        {
            blocked = false;
            complete = false;
            routeVersion = 0;
            return;
        }

        samples.AddRange(route.Samples);
        blocked = route.Blocked;
        complete = route.IsComplete;
        routeVersion = CurrentRouteVersion;
    }

    public PerfectRoute CreateRoute()
    {
        return HasSamples
            ? new PerfectRoute(samples.ToArray(), blocked, complete)
            : null;
    }
}
