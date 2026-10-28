using UnityEditor;
using UnityEngine;

public partial class ChartTimelineWindow
{
    private const float MinPlaybackSpeed = 0.1f;
    private const float MaxPlaybackSpeed = 2f;

    private void DrawPlayhead(Rect rulerRect, Rect trackRect)
    {
        DrawPlayheadLine(
            rulerRect,
            trackRect,
            playStartBeat,
            new Color(1f, 0.2f, 0.2f));

        if (!playing && Mathf.Approximately(playStartBeat, playheadBeat)) return;
        DrawPlayheadLine(
            rulerRect,
            trackRect,
            playheadBeat,
            new Color(1f, 0.82f, 0.12f));
    }

    private void DrawPlayheadLine(Rect rulerRect, Rect trackRect, float beat, Color color)
    {
        float x = rulerRect.x + (beat - scrollBeat) * pixelsPerBeat;
        if (x < -2 || x > position.width + 2) return;

        EditorGUI.DrawRect(new Rect(x - 1, rulerRect.y, 2, rulerRect.height), color);
        EditorGUI.DrawRect(new Rect(x - 1, trackRect.y, 2, trackRect.height), color);
        EditorGUI.DrawRect(new Rect(x - 5, rulerRect.yMax - 10, 10, 10), color);
    }

    private bool DoEdgeScroll(Rect trackRect)
    {
        if (!draggingNodes && !boxSelecting) return false;

        Vector2 mouse = Event.current.mousePosition;
        if (mouse.x < 0 || mouse.x > position.width) return false;

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastEdgeScrollTime);
        lastEdgeScrollTime = now;
        dt = Mathf.Clamp(dt, 0.01f, 0.05f);

        float delta = 0f;
        if (mouse.x < EdgeScrollZone)
            delta = -EdgeScrollSpeed * dt;
        else if (mouse.x > position.width - EdgeScrollZone)
            delta = EdgeScrollSpeed * dt;

        if (Mathf.Approximately(delta, 0f)) return false;

        float newScroll = Mathf.Max(
            MinScrollBeat,
            scrollBeat + delta / pixelsPerBeat);
        if (Mathf.Approximately(newScroll, scrollBeat)) return false;

        scrollBeat = newScroll;
        if (boxSelecting)
            boxCurrentBeat = ScreenXToBeat(mouse.x, trackRect);
        return true;
    }

    private void DrawToolbar()
    {
        GUILayout.BeginArea(new Rect(4, 3, position.width - 8, ToolbarHeight - 3));
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        ChartPlayer previousTarget = targetPlayer;
        targetPlayer = (ChartPlayer)EditorGUILayout.ObjectField(
            targetPlayer, typeof(ChartPlayer), true, GUILayout.Width(150));
        if (targetPlayer != previousTarget)
        {
            StopPlayback();
            ClearSelection();
            SyncSelectionToUnity();
            InvalidateMarkerCache();
            SceneView.RepaintAll();
        }
        PreparePreviewClip();

        GUILayout.Label("吸附", GUILayout.Width(28));
        int current = Mathf.Max(0, System.Array.IndexOf(SnapValues, snap));
        int next = EditorGUILayout.Popup(current, SnapLabels, GUILayout.Width(48));
        if (next != current) snap = SnapValues[next];

        GUILayout.Space(8);
        EditorGUI.BeginDisabledGroup(!playing && !CanPlay());
        if (GUILayout.Button(playing ? "■ 停" : "▶ 播", EditorStyles.toolbarButton, GUILayout.Width(50)))
            TogglePlayback();
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(!CanPlay());
        if (GUILayout.Button(
                new GUIContent("↺ 重放", "从红轴起始时间重新播放 (R)"),
                EditorStyles.toolbarButton,
                GUILayout.Width(62)))
        {
            ReplayPlayback();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(
            targetPlayer == null ||
            targetPlayer.chart == null ||
            EditorApplication.isPlayingOrWillChangePlaymode);
        if (GUILayout.Button(
                new GUIContent("导入CSV", "导入每行一个 beat 数字的 CSV，并追加到当前谱面"),
                EditorStyles.toolbarButton,
                GUILayout.Width(64)))
        {
            ImportBeatsFromCsv();
        }
        EditorGUI.EndDisabledGroup();

        GUILayout.Label(
            $"红:{playStartBeat:0.##}  黄:{playheadBeat:0.##}",
            EditorStyles.miniLabel,
            GUILayout.Width(122));

        GUILayout.FlexibleSpace();
        GUILayout.Label($"{pixelsPerBeat:0.#} px/beat", EditorStyles.miniLabel);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginDisabledGroup(targetPlayer == null || targetPlayer.chart == null);

        GUILayout.Label(OffsetLabel, EditorStyles.miniLabel, GUILayout.Width(40));
        float currentOffset = targetPlayer != null && targetPlayer.chart != null
            ? targetPlayer.chart.globalOffsetBeats
            : 0f;

        EditorGUI.BeginChangeCheck();
        float newOffset = EditorGUILayout.DelayedFloatField(currentOffset, GUILayout.Width(62));
        if (EditorGUI.EndChangeCheck() && targetPlayer != null && targetPlayer.chart != null)
        {
            Undo.RecordObject(targetPlayer.chart, "Change Chart Global Offset");
            targetPlayer.chart.globalOffsetBeats = Mathf.Clamp(newOffset, -64f, 64f);
            EditorUtility.SetDirty(targetPlayer.chart);
        }

        float offsetSeconds = targetPlayer != null && targetPlayer.chart != null
            ? targetPlayer.chart.GlobalOffsetSeconds
            : 0f;
        GUILayout.Label(
            $"{offsetSeconds * 1000f:+0.0;-0.0;0} ms",
            EditorStyles.miniLabel,
            GUILayout.Width(54));

        GUILayout.Space(4);
        GUILayout.Label(PlaybackSpeedLabel, EditorStyles.miniLabel, GUILayout.Width(28));
        float previousPlaybackSpeed = playbackSpeed;
        playbackSpeed = GUILayout.HorizontalSlider(
            NormalizePlaybackSpeed(playbackSpeed),
            MinPlaybackSpeed,
            MaxPlaybackSpeed,
            GUILayout.Width(100));
        playbackSpeed = NormalizePlaybackSpeed(playbackSpeed);
        if (!Mathf.Approximately(previousPlaybackSpeed, playbackSpeed))
        {
            float currentBeat =
                GetCurrentPlaybackBeat(previousPlaybackSpeed);
            if (previewSource != null)
                previewSource.pitch = playbackSpeed;
            if (playing)
            {
                playheadBeat = currentBeat;
                BeginNodeClickSchedule(
                    AudioSettings.dspTime,
                    currentBeat);
            }
        }
        GUILayout.Label($"{playbackSpeed:0.0}x", EditorStyles.miniLabel, GUILayout.Width(36));

        GUILayout.Space(8);
        bool newPreviewEnabled = GUILayout.Toggle(
            previewEnabled,
            new GUIContent(
                "启用预览",
                "仅单选时显示物理预测线：绿色最早、洋红最晚，青色/橙色为节点拍点；多选暂停计算。"),
            EditorStyles.toolbarButton,
            GUILayout.Width(72));
        if (newPreviewEnabled != previewEnabled)
        {
            previewEnabled = newPreviewEnabled;
            SceneView.RepaintAll();
        }

        GUILayout.Space(4);
        EditorGUI.BeginDisabledGroup(!CanUseNodeClickAudio);
        bool newNodeClickEnabled = GUILayout.Toggle(
            nodeClickEnabled,
            new GUIContent(
                "卡点音",
                "播放经过节点时按 DSP 时间预调度固定音效。需要在 ChartPlayer 配置音效和 AudioSource 池。"),
            EditorStyles.toolbarButton,
            GUILayout.Width(56));
        if (newNodeClickEnabled != nodeClickEnabled)
            SetNodeClickEnabled(newNodeClickEnabled);
        EditorGUI.EndDisabledGroup();

        EditorGUI.EndDisabledGroup();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private static float NormalizePlaybackSpeed(float value)
    {
        value = Mathf.Clamp(value, MinPlaybackSpeed, MaxPlaybackSpeed);
        return Mathf.Floor(value * 10f + 0.5f) / 10f;
    }

    private void DrawRuler(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.22f, 0.22f, 0.22f));

        float startBeat = scrollBeat;
        float endBeat = scrollBeat + rect.width / pixelsPerBeat;

        float majorStep = 1f;
        while (majorStep * pixelsPerBeat < 40f) majorStep *= 2f;

        float firstMajor = Mathf.Floor(startBeat / majorStep) * majorStep;
        if (firstMajor < 0f) firstMajor = 0f;

        for (float b = firstMajor; b <= endBeat; b += majorStep)
        {
            if (b < 0f) continue;
            float x = (b - scrollBeat) * pixelsPerBeat;
            if (x < -4 || x > rect.width + 4) continue;
            EditorGUI.DrawRect(new Rect(x, rect.y + rect.height * 0.4f, 1, rect.height * 0.6f),
                new Color(0.75f, 0.75f, 0.75f));
            GUI.Label(new Rect(x + 3, rect.y + 2, 80, 16), b.ToString("0.##"), EditorStyles.miniLabel);
        }

        float minorStep = majorStep / 4f;
        if (minorStep * pixelsPerBeat >= 6f)
        {
            float firstMinor = Mathf.Floor(startBeat / minorStep) * minorStep;
            if (firstMinor < 0f) firstMinor = 0f;
            for (float b = firstMinor; b <= endBeat; b += minorStep)
            {
                if (b < 0f) continue;
                if (Mathf.Approximately(b / majorStep, Mathf.Round(b / majorStep))) continue;
                float x = (b - scrollBeat) * pixelsPerBeat;
                if (x < -4 || x > rect.width + 4) continue;
                EditorGUI.DrawRect(new Rect(x, rect.y + rect.height * 0.7f, 1, rect.height * 0.3f),
                    new Color(0.5f, 0.5f, 0.5f));
            }
        }

        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), new Color(0.4f, 0.4f, 0.4f));
    }

    private void DrawTrack(Rect rect, NodeMarker[] markers)
    {
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));

        NodeAppearanceSet appearanceSet = targetPlayer != null
            ? targetPlayer.ResolveNodeAppearanceSet()
            : NodeAppearanceSet.LoadDefault();

        foreach (NodeMarker m in markers)
        {
            float x = (m.beat - scrollBeat) * pixelsPerBeat;
            if (x < -10 || x > rect.width + 10) continue;

            Color color = appearanceSet != null &&
                          appearanceSet.TryGetTimelineColor(m.mode, out Color modeColor)
                ? modeColor
                : Color.white;
            Rect markerRect = new Rect(x - 3, rect.y + 4, 6, rect.height - 8);
            EditorGUI.DrawRect(markerRect, color);

            if (IsSelected(m))
                DrawSelectionOutline(markerRect);
        }
    }

    private static void DrawSelectionOutline(Rect rect)
    {
        Color outline = new Color(1f, 1f, 1f, 0.95f);
        EditorGUI.DrawRect(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, 1), outline);
        EditorGUI.DrawRect(new Rect(rect.x - 1, rect.yMax, rect.width + 2, 1), outline);
        EditorGUI.DrawRect(new Rect(rect.x - 1, rect.y - 1, 1, rect.height + 2), outline);
        EditorGUI.DrawRect(new Rect(rect.xMax, rect.y - 1, 1, rect.height + 2), outline);
    }

    private void DrawScrollbar(Rect rect, NodeMarker[] markers)
    {
        float viewBeats = Mathf.Max(1f, position.width) / pixelsPerBeat;
        float maxBeat = markers.Length > 0 ? markers[markers.Length - 1].beat : 0f;
        float furthestBeat = Mathf.Max(Mathf.Max(maxBeat, playStartBeat), playheadBeat);
        float totalBeats = Mathf.Max(
            Mathf.Max(viewBeats, furthestBeat + 4f),
            scrollBeat + viewBeats);
        float minScroll = MinScrollBeat;
        float maxScroll = Mathf.Max(minScroll, totalBeats - viewBeats);

        float newScroll = GUI.HorizontalScrollbar(
            rect,
            scrollBeat,
            viewBeats,
            minScroll,
            totalBeats);
        newScroll = Mathf.Clamp(newScroll, minScroll, maxScroll);
        if (!Mathf.Approximately(newScroll, scrollBeat))
        {
            scrollBeat = newScroll;
            RequestRepaint();
        }
    }

    private void DrawBoxSelection()
    {
        if (!boxSelecting) return;
        float x1 = Mathf.Min(pendingStart.x, boxCurrent.x);
        float y1 = Mathf.Min(pendingStart.y, boxCurrent.y);
        float x2 = Mathf.Max(pendingStart.x, boxCurrent.x);
        float y2 = Mathf.Max(pendingStart.y, boxCurrent.y);
        Rect r = new Rect(x1, y1, x2 - x1, y2 - y1);
        EditorGUI.DrawRect(r, new Color(0.3f, 0.6f, 1f, 0.2f));
        Color border = new Color(0.3f, 0.6f, 1f, 0.7f);
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), border);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), border);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), border);
        EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), border);
    }
}
