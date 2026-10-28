using UnityEngine;

public partial class ChartTimelineWindow
{
    private void UpdatePlayheadIfPlaying()
    {
        if (!playing) return;
        if (targetPlayer == null || targetPlayer.chart == null ||
            targetPlayer.chart.bpmTimeline == null)
        {
            StopPlayback();
            return;
        }

        if (playbackUsesAudio)
        {
            if (previewSource == null)
            {
                StopPlayback();
                RequestRepaint();
                return;
            }

            if (AudioSettings.dspTime < playStartDspTime)
            {
                RequestRepaint();
                return;
            }

            if (!previewSource.isPlaying)
            {
                StopPlayback();
                RequestRepaint();
                return;
            }

            previewSource.pitch = playbackSpeed;
            float chartSeconds = previewSource.time - targetPlayer.chart.GlobalOffsetSeconds;
            playheadBeat = targetPlayer.chart.bpmTimeline.SecondsToBeat(
                Mathf.Max(0f, chartSeconds));
        }
        else
        {
            float startSec =
                targetPlayer.chart.bpmTimeline.BeatToSeconds(playbackAnchorBeat);
            float elapsed = (float)(AudioSettings.dspTime - playStartDspTime);
            if (elapsed < 0f) elapsed = 0f;
            playheadBeat = targetPlayer.chart.bpmTimeline.SecondsToBeat(
                startSec + elapsed * playbackSpeed);
        }

        float viewBeats = Mathf.Max(1f, position.width) / pixelsPerBeat;
        if (playheadBeat > scrollBeat + viewBeats * 0.85f)
            scrollBeat = Mathf.Max(
                MinScrollBeat,
                playheadBeat - viewBeats * 0.2f);

        RequestRepaint();
    }

    private float GetCurrentPlaybackBeat(float speedForFallback)
    {
        if (!playing ||
            targetPlayer == null ||
            targetPlayer.chart == null ||
            targetPlayer.chart.bpmTimeline == null)
        {
            return playheadBeat;
        }

        BpmTimeline timeline = targetPlayer.chart.bpmTimeline;
        if (playbackUsesAudio)
        {
            if (previewSource == null)
                return playheadBeat;

            float chartSeconds =
                previewSource.time -
                targetPlayer.chart.GlobalOffsetSeconds;
            return timeline.SecondsToBeat(
                Mathf.Max(0f, chartSeconds));
        }

        float startSeconds =
            timeline.BeatToSeconds(playbackAnchorBeat);
        float elapsed =
            (float)(AudioSettings.dspTime - playStartDspTime);
        return timeline.SecondsToBeat(
            startSeconds +
            Mathf.Max(0f, elapsed) *
            Mathf.Max(0.1f, speedForFallback));
    }

    private void TogglePlayback()
    {
        if (playing) StopPlayback();
        else if (CanPlay()) StartPlayback();
    }

    private void StartPlayback()
    {
        if (!CanPlay()) return;

        playbackAnchorBeat = playheadBeat;

        AudioClip clip = targetPlayer.chart.clip;
        playbackUsesAudio = clip != null;
        if (playbackUsesAudio)
        {
            EnsurePreviewSource();
            previewSource.Stop();
            previewSource.clip = clip;
            previewSource.playOnAwake = false;
            previewSource.pitch = playbackSpeed;

            float audioSeconds =
                targetPlayer.chart.bpmTimeline.BeatToSeconds(playbackAnchorBeat) +
                targetPlayer.chart.GlobalOffsetSeconds;
            float clampedAudioSeconds = Mathf.Clamp(
                audioSeconds,
                0f,
                Mathf.Max(0f, clip.length - 0.001f));
            previewSource.time = clampedAudioSeconds;
            playbackAnchorBeat =
                targetPlayer.chart.bpmTimeline.SecondsToBeat(
                    Mathf.Max(
                        0f,
                        clampedAudioSeconds -
                        targetPlayer.chart.GlobalOffsetSeconds));
            playStartDspTime = AudioSettings.dspTime + PlaybackLeadTime;
            previewSource.PlayScheduled(playStartDspTime);
        }
        else
        {
            if (previewSource != null)
            {
                previewSource.Stop();
                previewSource.clip = null;
            }
            playStartDspTime = AudioSettings.dspTime;
        }

        playing = true;
        BeginNodeClickSchedule(
            playStartDspTime,
            playbackAnchorBeat);
        RequestRepaint();
    }

    private void ReplayPlayback()
    {
        if (!CanPlay()) return;

        FocusPlayStartBeat();
        StartPlayback();
    }

    private void ReturnToPlayStart()
    {
        StopPlayback();
        FocusPlayStartBeat();
    }

    private void FocusPlayStartBeat()
    {
        playheadBeat = playStartBeat;
        scrollBeat = Mathf.Max(
            MinScrollBeat,
            playStartBeat - RedAxisLeftInsetPixels / pixelsPerBeat);
        RequestRepaint();
    }

    private void StopPlayback()
    {
        playing = false;
        playbackUsesAudio = false;
        StopNodeClickAudio();
        if (previewSource != null)
            previewSource.Stop();
    }

    private bool CanPlay() =>
        targetPlayer != null &&
        targetPlayer.chart != null &&
        targetPlayer.chart.bpmTimeline != null;

    private void EnsurePreviewSource()
    {
        if (previewSource != null) return;

        var go = new GameObject("~ChartPreviewAudio") { hideFlags = HideFlags.HideAndDontSave };
        previewSource = go.AddComponent<AudioSource>();
        previewSource.playOnAwake = false;
        previewSource.spatialBlend = 0f;
    }

    private void PreparePreviewClip()
    {
        PrepareNodeClickAudio();
        if (previewSource == null) return;

        AudioClip clip = CanPlay() ? targetPlayer.chart.clip : null;
        if (playing)
        {
            if (previewSource.clip != clip) StopPlayback();
            return;
        }

        if (previewSource.clip != clip)
            previewSource.clip = clip;
    }
}
