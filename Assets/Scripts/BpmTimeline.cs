using System;
using System.Collections.Generic;

[Serializable]
public class BpmSegment
{
    public float beat;      // 该段从第几拍开始，第一段必须是 0
    public float bpm = 120f;
}

[Serializable]
public class BpmTimeline
{
    public List<BpmSegment> segments = new List<BpmSegment>
    {
        new BpmSegment { beat = 0f, bpm = 120f }
    };

    // 逻辑拍 → 音频时间（秒）
    public float BeatToSeconds(float beat)
    {
        if (segments == null || segments.Count == 0)
            return beat * 60f / 120f;

        float seconds = 0f;
        float prevBeat = segments[0].beat;
        float prevBpm = segments[0].bpm;

        for (int i = 1; i < segments.Count; i++)
        {
            BpmSegment seg = segments[i];
            if (beat <= seg.beat) break;

            seconds += (seg.beat - prevBeat) * 60f / prevBpm;
            prevBeat = seg.beat;
            prevBpm = seg.bpm;
        }

        seconds += (beat - prevBeat) * 60f / prevBpm;
        return seconds;
    }
}