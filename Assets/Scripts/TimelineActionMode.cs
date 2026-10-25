using UnityEngine;

/// <summary>
/// 保留原有移动行为的兼容模式，同时也作为后续模式的实现参考。
/// </summary>
public sealed class TimelineActionMode : PlayerActionMode
{
    private ChartMotionTimeline timeline;

    public override string DisplayName => "时间轴移动";

    protected override void OnModeEnter()
    {
        if (Player.Chart == null)
        {
            timeline = null;
            Player.transform.position = Player.OriginalPosition;
            return;
        }

        timeline = new ChartMotionTimeline(
            Player.PlaybackStartPosition,
            Player.PlaybackStartSongTime,
            Player.Nodes,
            Player.Chart.bpmTimeline);

        Player.transform.position = timeline.Sample(Player.SongTime);
    }

    protected override void OnModeUpdate(double songTime)
    {
        if (timeline == null)
            return;

        Player.transform.position = timeline.Sample(songTime);
    }

    protected override void OnModeRespawn(
        Vector2 position,
        double checkpointSongTime,
        int checkpointNodeIndex)
    {
        if (Player.Chart == null)
            return;

        timeline = new ChartMotionTimeline(
            position,
            checkpointSongTime,
            Player.Nodes,
            Player.Chart.bpmTimeline);

        Player.transform.position = position;
    }

    protected override void OnModeExit()
    {
        timeline = null;
    }
}
