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
            scrollBeat = Mathf.Max(0f, playheadBeat - viewBeats * 0.2f);

        RequestRepaint();
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
            previewSource.time = Mathf.Clamp(audioSeconds, 0f, Mathf.Max(0f, clip.length - 0.001f));
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
        RequestRepaint();
    }

    private void ReplayPlayback()
    {
        if (!CanPlay()) return;

        playheadBeat = playStartBeat;
        StartPlayback();
    }

    private void StopPlayback()
    {
        playing = false;
        playbackUsesAudio = false;
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
