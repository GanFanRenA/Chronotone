using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class ChartTimelineWindow
{
    private const double NodeClickScheduleHorizon = 1.0;
    private const double NodeClickLateTolerance = 0.02;

    private readonly List<double> pendingNodeClickDspTimes =
        new List<double>();

    private AudioSource[] scheduledNodeClickSources =
        System.Array.Empty<AudioSource>();
    private double[] nodeClickSourceBusyUntil =
        System.Array.Empty<double>();

    private int nextNodeClickIndex;
    private bool nodeClickScheduleActive;
    private bool warnedNodeClickScheduling;

    private bool CanUseNodeClickAudio
    {
        get
        {
            if (targetPlayer == null ||
                targetPlayer.chart == null ||
                targetPlayer.timelineNodeClickClip == null)
            {
                return false;
            }

            AudioSource[] configuredSources =
                targetPlayer.timelineNodeClickSources;
            if (configuredSources != null)
            {
                foreach (AudioSource source in configuredSources)
                {
                    if (source != null && source.isActiveAndEnabled)
                        return true;
                }
            }

            AudioSource fallbackSource =
                targetPlayer.GetComponent<AudioSource>();
            return fallbackSource != null &&
                   fallbackSource.isActiveAndEnabled;
        }
    }

    private void PrepareNodeClickAudio()
    {
        AudioClip clip = targetPlayer != null
            ? targetPlayer.timelineNodeClickClip
            : null;
        if (clip == null ||
            clip.loadState != AudioDataLoadState.Unloaded)
        {
            return;
        }

        clip.LoadAudioData();
    }

    private void SetNodeClickEnabled(bool enabled)
    {
        nodeClickEnabled = enabled;
        if (!enabled)
        {
            StopNodeClickAudio();
            return;
        }

        if (playing)
        {
            BeginNodeClickSchedule(
                AudioSettings.dspTime,
                playheadBeat);
        }
    }

    private void BeginNodeClickSchedule(
        double startDspTime,
        float anchorBeat)
    {
        StopNodeClickAudio();

        if (!nodeClickEnabled ||
            targetPlayer == null ||
            targetPlayer.chart == null ||
            targetPlayer.chart.bpmTimeline == null ||
            targetPlayer.timelineNodeClickClip == null)
        {
            return;
        }

        PrepareNodeClickAudio();

        AudioClip clip = targetPlayer.timelineNodeClickClip;
        scheduledNodeClickSources = ResolveNodeClickSources();
        if (clip == null ||
            scheduledNodeClickSources.Length == 0)
        {
            return;
        }

        nodeClickSourceBusyUntil =
            new double[scheduledNodeClickSources.Length];
        for (int i = 0; i < nodeClickSourceBusyUntil.Length; i++)
            nodeClickSourceBusyUntil[i] = double.NegativeInfinity;

        BpmTimeline timeline = targetPlayer.chart.bpmTimeline;
        float safeAnchorBeat = Mathf.Max(0f, anchorBeat);
        double anchorSeconds = timeline.BeatToSeconds(safeAnchorBeat);
        float speed = Mathf.Max(0.1f, playbackSpeed);

        pendingNodeClickDspTimes.Clear();
        float previousBeat = float.NegativeInfinity;
        foreach (NodeMarker marker in GetMarkers())
        {
            if (marker == null)
                continue;

            float beat = Mathf.Max(0f, marker.beat);
            if (beat + 0.00001f < safeAnchorBeat)
                continue;
            if (!float.IsNegativeInfinity(previousBeat) &&
                Mathf.Abs(previousBeat - beat) < 0.00001f)
            {
                continue;
            }

            previousBeat = beat;
            double nodeSeconds = timeline.BeatToSeconds(beat);
            double eventDspTime =
                startDspTime +
                (nodeSeconds - anchorSeconds) / speed;
            pendingNodeClickDspTimes.Add(eventDspTime);
        }

        nextNodeClickIndex = 0;
        nodeClickScheduleActive =
            pendingNodeClickDspTimes.Count > 0;
        warnedNodeClickScheduling = false;

        ScheduleUpcomingNodeClicks();
    }

    private AudioSource[] ResolveNodeClickSources()
    {
        var result = new List<AudioSource>();
        var seen = new HashSet<AudioSource>();
        AudioSource[] configured =
            targetPlayer.timelineNodeClickSources;
        if (configured != null)
        {
            foreach (AudioSource source in configured)
            {
                if (source != null &&
                    source.isActiveAndEnabled &&
                    seen.Add(source))
                {
                    result.Add(source);
                }
            }
        }

        if (result.Count == 0)
        {
            foreach (AudioSource source in
                     targetPlayer.GetComponents<AudioSource>())
            {
                if (source != null &&
                    source.isActiveAndEnabled &&
                    seen.Add(source))
                {
                    result.Add(source);
                }
            }
        }

        foreach (AudioSource source in result)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.mute = false;
            source.spatialBlend = 0f;
        }

        return result.ToArray();
    }

    private void UpdateNodeClickSchedule()
    {
        if (!nodeClickScheduleActive)
            return;

        if (!playing ||
            !nodeClickEnabled ||
            targetPlayer == null ||
            targetPlayer.timelineNodeClickClip == null)
        {
            StopNodeClickAudio();
            return;
        }

        ScheduleUpcomingNodeClicks();
    }

    private void ScheduleUpcomingNodeClicks()
    {
        if (!nodeClickScheduleActive ||
            nextNodeClickIndex >= pendingNodeClickDspTimes.Count)
        {
            return;
        }

        AudioClip clip = targetPlayer.timelineNodeClickClip;
        double now = AudioSettings.dspTime;
        double scheduleUntil = now + NodeClickScheduleHorizon;

        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            PrepareNodeClickAudio();
            if (clip.loadState == AudioDataLoadState.Failed)
            {
                WarnNodeClickScheduling(
                    "Chart Timeline 卡点音无法加载，请检查 AudioClip 导入设置。");
            }
            return;
        }

        while (nextNodeClickIndex < pendingNodeClickDspTimes.Count)
        {
            double eventDspTime =
                pendingNodeClickDspTimes[nextNodeClickIndex];
            if (eventDspTime > scheduleUntil)
                break;

            if (eventDspTime < now - NodeClickLateTolerance)
            {
                nextNodeClickIndex++;
                WarnNodeClickScheduling(
                    "Chart Timeline 卡点音调度晚于节点时间，请检查编辑器卡顿或 AudioSource 池大小。");
                continue;
            }

            int sourceIndex = FindAvailableNodeClickSource(now);
            if (sourceIndex < 0)
            {
                WarnNodeClickScheduling(
                    "Chart Timeline 卡点音 AudioSource 池不足；请给已引用的音源对象增加 AudioSource，并加入 timelineNodeClickSources。");
                return;
            }

            AudioSource source = scheduledNodeClickSources[sourceIndex];
            source.Stop();
            source.clip = clip;
            source.timeSamples = 0;
            source.volume = targetPlayer.timelineNodeClickVolume;
            source.pitch = 1f;
            source.priority = 0;
            source.PlayScheduled(eventDspTime);

            nodeClickSourceBusyUntil[sourceIndex] =
                eventDspTime + clip.length + 0.01;
            nextNodeClickIndex++;
        }
    }

    private int FindAvailableNodeClickSource(double now)
    {
        for (int i = 0; i < scheduledNodeClickSources.Length; i++)
        {
            // A source can only hold one scheduled playback at a time. It becomes
            // reusable only after the previously scheduled clip has finished.
            if (nodeClickSourceBusyUntil[i] <= now)
                return i;
        }

        return -1;
    }

    private void WarnNodeClickScheduling(string message)
    {
        if (warnedNodeClickScheduling)
            return;

        warnedNodeClickScheduling = true;
        Debug.LogWarning(message, this);
    }

    private void StopNodeClickAudio()
    {
        nodeClickScheduleActive = false;
        nextNodeClickIndex = 0;
        pendingNodeClickDspTimes.Clear();

        foreach (AudioSource source in scheduledNodeClickSources)
        {
            if (source != null)
                source.Stop();
        }
    }
}
