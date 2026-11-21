using System.Collections.Generic;
using UnityEngine;

public partial class ChartTimelineWindow
{
    private void UpdatePlayheadIfPlaying()
    {
        if (!playing) return;
        if (targetPlayer == null ||
            targetPlayer.BpmTimeline == null)
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
            float chartSeconds = previewSource.time - targetPlayer.GlobalOffsetSeconds;
            playheadBeat = targetPlayer.BpmTimeline.SecondsToBeat(
                Mathf.Max(0f, chartSeconds));
        }
        else
        {
            float startSec =
                targetPlayer.BpmTimeline.BeatToSeconds(playbackAnchorBeat);
            float elapsed = (float)(AudioSettings.dspTime - playStartDspTime);
            if (elapsed < 0f) elapsed = 0f;
            playheadBeat = targetPlayer.BpmTimeline.SecondsToBeat(
                startSec + elapsed * playbackSpeed);
        }

        if (loopEnabled && loopEndBeat > playStartBeat && playheadBeat >= loopEndBeat)
        {
            LoopBackToStart();
            return;
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
            targetPlayer.BpmTimeline == null)
        {
            return playheadBeat;
        }

        BpmTimeline timeline = targetPlayer.BpmTimeline;
        if (playbackUsesAudio)
        {
            if (previewSource == null)
                return playheadBeat;

            float chartSeconds =
                previewSource.time -
                targetPlayer.GlobalOffsetSeconds;
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

        AudioClip clip = targetPlayer.Clip;
        playbackUsesAudio = clip != null;
        if (playbackUsesAudio)
        {
            EnsurePreviewSource();
            previewSource.Stop();
            previewSource.clip = clip;
            previewSource.playOnAwake = false;
            previewSource.pitch = playbackSpeed;

            float audioSeconds =
                targetPlayer.BpmTimeline.BeatToSeconds(playbackAnchorBeat) +
                targetPlayer.GlobalOffsetSeconds;
            float clampedAudioSeconds = Mathf.Clamp(
                audioSeconds,
                0f,
                Mathf.Max(0f, clip.length - 0.001f));
            previewSource.time = clampedAudioSeconds;
            playbackAnchorBeat =
                targetPlayer.BpmTimeline.SecondsToBeat(
                    Mathf.Max(
                        0f,
                        clampedAudioSeconds -
                        targetPlayer.GlobalOffsetSeconds));
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

    private void LoopBackToStart()
    {
        playheadBeat = playStartBeat;
        StartPlayback();
        scrollBeat = Mathf.Max(
            MinScrollBeat,
            playStartBeat - RedAxisLeftInsetPixels / pixelsPerBeat);
        IncrementBeatMarkLoops();
        RequestRepaint();
    }

    private void IncrementBeatMarkLoops()
    {
        if (beatMarks == null)
            return;

        if (beatMarkLoopCounts == null)
            beatMarkLoopCounts = new List<int>();

        while (beatMarkLoopCounts.Count < beatMarks.Count)
            beatMarkLoopCounts.Add(0);

        for (int i = beatMarks.Count - 1; i >= 0; i--)
        {
            int count = beatMarkLoopCounts[i] + 1;
            if (count >= BeatMarkMaxLoops)
            {
                beatMarks.RemoveAt(i);
                beatMarkLoopCounts.RemoveAt(i);
            }
            else
            {
                beatMarkLoopCounts[i] = count;
            }
        }
    }

    private void StopPlayback()
    {
        playing = false;
        playbackUsesAudio = false;
        if (previewSource != null)
            previewSource.Stop();
        ResetBeatMarkLoops();
    }

    private void ResetBeatMarkLoops()
    {
        if (beatMarkLoopCounts == null)
            return;

        for (int i = 0; i < beatMarkLoopCounts.Count; i++)
            beatMarkLoopCounts[i] = 0;
    }

    private bool CanPlay() =>
        targetPlayer != null &&
        targetPlayer.BpmTimeline != null;

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

        AudioClip clip = CanPlay() ? targetPlayer.Clip : null;
        if (playing)
        {
            if (previewSource.clip != clip) StopPlayback();
            return;
        }

        if (previewSource.clip != clip)
            previewSource.clip = clip;
    }
}
