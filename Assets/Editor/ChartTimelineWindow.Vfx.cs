using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class ChartTimelineWindow
{
    private const float VfxLaneHeaderHeight = 16f;
    private const float VfxDotSize = 8f;
    private const float VfxEndDotSize = 6f;
    private const float VfxBarThickness = 4f;
    private const float VfxDetailPanelHeight = 64f;
    private const float VfxHitThreshold = 6f;

    private static readonly Color ScreenShakeColor =
        new Color(0.85f, 0.5f, 1f);

    private enum VfxHitPart
    {
        None = 0,
        Start = 1,   // 起点圆点 / 条身：改拍点
        End = 2      // 末端圆点：改时长
    }

    private int selectedVfxIndex = -1;
    private bool vfxDragging;
    private int vfxDragIndex = -1;
    private float vfxDragStartBeat;
    private float vfxDragStartDurationBeats;
    private float vfxDragMouseStartBeat;
    private bool vfxDragResize;
    private bool vfxDragChanged;
    private bool vfxDragRecorded;

    // ---- 数据 ----

    private List<StageVfxEvent> GetVfxEvents()
    {
        return targetPlayer != null ? targetPlayer.VfxEventsEditable : null;
    }

    private void ValidateVfxSelection()
    {
        List<StageVfxEvent> events = GetVfxEvents();
        if (selectedVfxIndex < 0)
            return;

        if (events == null || selectedVfxIndex >= events.Count)
            selectedVfxIndex = -1;
    }

    private void RecordVfxUndo(string name)
    {
        if (targetPlayer != null)
            Undo.RecordObject(targetPlayer, name);
    }

    private void CommitVfxEdit()
    {
        if (targetPlayer == null)
            return;

        EditorUtility.SetDirty(targetPlayer);
        ValidateVfxSelection();
        RequestRepaint();
    }

    // ---- 绘制 ----

    private void DrawVfxLane(Rect rect, List<StageVfxEvent> events)
    {
        EditorGUI.DrawRect(rect, new Color(0.13f, 0.12f, 0.16f));
        DrawVfxLegend(new Rect(rect.x, rect.y, rect.width, VfxLaneHeaderHeight));
        EditorGUI.DrawRect(
            new Rect(rect.x, rect.y + VfxLaneHeaderHeight - 1, rect.width, 1),
            new Color(0.3f, 0.3f, 0.3f));

        if (events == null)
            return;

        Rect content = new Rect(
            rect.x,
            rect.y + VfxLaneHeaderHeight,
            rect.width,
            rect.height - VfxLaneHeaderHeight);
        float centerY = content.y + content.height * 0.5f;

        for (int i = 0; i < events.Count; i++)
        {
            StageVfxEvent e = events[i];
            if (e == null)
                continue;

            float startX = rect.x + (e.beat - scrollBeat) * pixelsPerBeat;
            float endX = rect.x +
                (e.beat + e.durationBeats - scrollBeat) * pixelsPerBeat;
            float left = Mathf.Min(startX, endX);
            float right = Mathf.Max(startX, endX);
            if (right < -10 || left > rect.width + 10)
                continue;

            Color color = ColorForVfxKind(e.kind);
            string tooltip = VfxTooltip(e);

            // 时长线段。
            if (right - left > 0.5f)
            {
                Color bar = new Color(color.r, color.g, color.b, 0.35f);
                EditorGUI.DrawRect(
                    new Rect(
                        left,
                        centerY - VfxBarThickness * 0.5f,
                        right - left,
                        VfxBarThickness),
                    bar);
                GUI.Label(
                    new Rect(left, centerY - VfxDotSize, right - left, VfxDotSize * 2f),
                    new GUIContent(string.Empty, tooltip));
            }

            // 起点圆点（拖它改拍点）。
            Rect startDotRect = new Rect(
                startX - VfxDotSize * 0.5f,
                centerY - VfxDotSize * 0.5f,
                VfxDotSize,
                VfxDotSize);
            DrawDot(startDotRect, color);
            GUI.Label(startDotRect, new GUIContent(string.Empty, tooltip));

            // 末端圆点（拖它改时长）。
            if (e.durationBeats > 0f)
            {
                Rect endDotRect = new Rect(
                    endX - VfxEndDotSize * 0.5f,
                    centerY - VfxEndDotSize * 0.5f,
                    VfxEndDotSize,
                    VfxEndDotSize);
                DrawDot(endDotRect, color);
                GUI.Label(endDotRect, new GUIContent(string.Empty, tooltip));
            }

            if (i == selectedVfxIndex)
            {
                DrawSelectionOutline(new Rect(
                    left - 2f,
                    centerY - VfxDotSize - 2f,
                    Mathf.Max(VfxDotSize, right - left) + 4f,
                    VfxDotSize * 2f + 4f));
            }
        }
    }

    private void DrawVfxLegend(Rect header)
    {
        DrawLegendItem(4f, header.y + 2f, ScreenShakeColor, "屏幕震动", 72f);
        GUI.Label(
            new Rect(84f, header.y + 2f, header.width - 88f, 14f),
            "右键添加 · 拖起点改拍点 · 拖末端改时长 · Delete 删除",
            EditorStyles.miniLabel);
    }

    private void DrawVfxDetailPanel(Rect rect)
    {
        if (rect.height <= 0f)
            return;

        EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.19f));
        EditorGUI.DrawRect(
            new Rect(rect.x, rect.y, rect.width, 1),
            new Color(0.35f, 0.35f, 0.35f));

        List<StageVfxEvent> events = GetVfxEvents();
        if (events == null ||
            selectedVfxIndex < 0 ||
            selectedVfxIndex >= events.Count)
        {
            return;
        }

        StageVfxEvent e = events[selectedVfxIndex];
        if (e == null)
            return;

        const float fieldWidth = 90f;
        const float gap = 8f;
        float x = rect.x + 8f;
        float y = rect.y + 6f;

        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 42f;

        EditorGUI.BeginChangeCheck();
        float newBeat = EditorGUI.FloatField(
            new Rect(x, y, fieldWidth, 18f), "拍点", e.beat);
        x += fieldWidth + gap;
        float newDuration = EditorGUI.FloatField(
            new Rect(x, y, fieldWidth, 18f), "时长拍", e.durationBeats);
        x += fieldWidth + gap;
        float newStrength = EditorGUI.FloatField(
            new Rect(x, y, fieldWidth, 18f), "强度", e.strength);
        x += fieldWidth + gap;
        float newFrequency = EditorGUI.FloatField(
            new Rect(x, y, fieldWidth, 18f), "频率", e.frequency);

        EditorGUIUtility.labelWidth = previousLabelWidth;

        if (EditorGUI.EndChangeCheck())
        {
            RecordVfxUndo("Edit VFX Event");
            e.beat = Mathf.Max(0f, newBeat);
            e.durationBeats = Mathf.Max(0f, newDuration);
            e.strength = Mathf.Max(0f, newStrength);
            e.frequency = Mathf.Max(0f, newFrequency);
            CommitVfxEdit();
        }
    }

    private static Color ColorForVfxKind(StageVfxEventKind kind)
    {
        switch (kind)
        {
            case StageVfxEventKind.ScreenShake:
                return ScreenShakeColor;
            default:
                return Color.white;
        }
    }

    private static string VfxTooltip(StageVfxEvent e)
    {
        if (e.kind == StageVfxEventKind.ScreenShake)
        {
            return $"屏幕震动 · beat {e.beat:0.###} → {e.beat + e.durationBeats:0.###} · " +
                   $"强度 {e.strength:0.##} · 频率 {e.frequency:0.##}";
        }

        return $"视效 · beat {e.beat:0.###}";
    }

    // ---- 交互 ----

    private void HandleVfxInput(Rect rect)
    {
        Event e = Event.current;
        List<StageVfxEvent> events = GetVfxEvents();

        bool rightClick =
            e.type == EventType.ContextClick ||
            (e.type == EventType.MouseDown && e.button == 1);

        if (rightClick && rect.Contains(e.mousePosition))
        {
            ShowVfxContextMenu(rect, events, e.mousePosition);
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown &&
            e.button == 0 &&
            rect.Contains(e.mousePosition))
        {
            TryHitVfxEvent(
                events,
                e.mousePosition.x,
                rect,
                out int hit,
                out VfxHitPart part);
            if (hit >= 0)
            {
                selectedVfxIndex = hit;
                BeginVfxDrag(e, rect, events, part);
            }
            else
            {
                selectedVfxIndex = -1;
            }

            e.Use();
            RequestRepaint();
            return;
        }

        if (e.type == EventType.MouseDrag && e.button == 0 && vfxDragging)
        {
            UpdateVfxDrag(e, rect, events);
            e.Use();
            RequestRepaint();
            return;
        }

        if (e.type == EventType.MouseUp && e.button == 0 && vfxDragging)
        {
            EndVfxDrag();
            e.Use();
            RequestRepaint();
            return;
        }
    }

    private void ShowVfxContextMenu(
        Rect rect,
        List<StageVfxEvent> events,
        Vector2 mouse)
    {
        float beat = ScreenXToBeatVfx(mouse.x, rect);
        if (snap > 0f)
            beat = Mathf.Round(beat / snap) * snap;
        beat = Mathf.Max(0f, beat);

        TryHitVfxEvent(events, mouse.x, rect, out int hit, out VfxHitPart _);

        var menu = new GenericMenu();
        if (hit >= 0)
        {
            menu.AddItem(
                new GUIContent($"删除屏幕震动事件 (beat {events[hit].beat:0.###})"),
                false,
                () => DeleteVfxEventAt(hit));
            menu.AddSeparator(string.Empty);
        }

        menu.AddItem(
            new GUIContent($"添加屏幕震动事件 (beat {beat:0.###})"),
            false,
            () => AddVfxEventAt(beat));
        menu.ShowAsContext();
    }

    private void AddVfxEventAt(float beat)
    {
        if (targetPlayer == null)
            return;

        RecordVfxUndo("Add VFX Event");
        var e = new StageVfxEvent
        {
            beat = beat,
            kind = StageVfxEventKind.ScreenShake
        };
        targetPlayer.VfxEventsEditable.Add(e);
        selectedVfxIndex = targetPlayer.VfxEventsEditable.Count - 1;
        CommitVfxEdit();
    }

    private void DeleteVfxEventAt(int index)
    {
        List<StageVfxEvent> events = GetVfxEvents();
        if (events == null || index < 0 || index >= events.Count)
            return;

        RecordVfxUndo("Delete VFX Event");
        events.RemoveAt(index);
        if (selectedVfxIndex == index)
            selectedVfxIndex = -1;
        else if (selectedVfxIndex > index)
            selectedVfxIndex--;

        CommitVfxEdit();
    }

    private void DeleteSelectedVfxEvent()
    {
        if (selectedVfxIndex >= 0)
            DeleteVfxEventAt(selectedVfxIndex);
    }

    private void TryHitVfxEvent(
        List<StageVfxEvent> events,
        float mouseX,
        Rect rect,
        out int index,
        out VfxHitPart part)
    {
        index = -1;
        part = VfxHitPart.None;
        if (events == null)
            return;

        // 先找圆点（末端优先用于改时长，起点用于改拍点）。
        float bestDistance = VfxHitThreshold;
        for (int i = 0; i < events.Count; i++)
        {
            StageVfxEvent e = events[i];
            if (e == null)
                continue;

            float startX = rect.x + (e.beat - scrollBeat) * pixelsPerBeat;
            float endX = rect.x +
                (e.beat + e.durationBeats - scrollBeat) * pixelsPerBeat;

            if (e.durationBeats > 0f)
            {
                float endDistance = Mathf.Abs(endX - mouseX);
                if (endDistance < bestDistance)
                {
                    bestDistance = endDistance;
                    index = i;
                    part = VfxHitPart.End;
                }
            }

            float startDistance = Mathf.Abs(startX - mouseX);
            if (startDistance <= bestDistance)
            {
                bestDistance = startDistance;
                index = i;
                part = VfxHitPart.Start;
            }
        }

        if (index >= 0)
            return;

        // 没点到圆点，再看是否落在某段条身内。
        for (int i = 0; i < events.Count; i++)
        {
            StageVfxEvent e = events[i];
            if (e == null)
                continue;

            float startX = rect.x + (e.beat - scrollBeat) * pixelsPerBeat;
            float endX = rect.x +
                (e.beat + e.durationBeats - scrollBeat) * pixelsPerBeat;
            float left = Mathf.Min(startX, endX);
            float right = Mathf.Max(startX, endX);

            if (mouseX >= left && mouseX <= right)
            {
                index = i;
                part = VfxHitPart.Start; // 拖条身 = 改拍点
                return;
            }
        }
    }

    private float ScreenXToBeatVfx(float screenX, Rect rect) =>
        scrollBeat + (screenX - rect.x) / pixelsPerBeat;

    private void BeginVfxDrag(
        Event e,
        Rect rect,
        List<StageVfxEvent> events,
        VfxHitPart part)
    {
        if (selectedVfxIndex < 0 ||
            events == null ||
            selectedVfxIndex >= events.Count)
        {
            return;
        }

        StageVfxEvent evt = events[selectedVfxIndex];
        vfxDragging = true;
        vfxDragChanged = false;
        vfxDragRecorded = false;
        vfxDragIndex = selectedVfxIndex;
        vfxDragResize = part == VfxHitPart.End;
        vfxDragStartBeat = evt.beat;
        vfxDragStartDurationBeats = evt.durationBeats;
        vfxDragMouseStartBeat = ScreenXToBeatVfx(e.mousePosition.x, rect);
    }

    private void UpdateVfxDrag(Event e, Rect rect, List<StageVfxEvent> events)
    {
        if (!vfxDragging ||
            events == null ||
            vfxDragIndex < 0 ||
            vfxDragIndex >= events.Count)
        {
            return;
        }

        StageVfxEvent evt = events[vfxDragIndex];
        float mouseBeat = ScreenXToBeatVfx(e.mousePosition.x, rect);
        float delta = mouseBeat - vfxDragMouseStartBeat;
        if (!(e.control || e.command) && snap > 0f)
            delta = Mathf.Round(delta / snap) * snap;

        float newBeat = evt.beat;
        float newDuration = evt.durationBeats;
        if (vfxDragResize)
            newDuration = Mathf.Max(0f, vfxDragStartDurationBeats + delta);
        else
            newBeat = Mathf.Max(0f, vfxDragStartBeat + delta);

        if (Mathf.Approximately(evt.beat, newBeat) &&
            Mathf.Approximately(evt.durationBeats, newDuration))
        {
            return;
        }

        if (!vfxDragRecorded)
        {
            RecordVfxUndo(vfxDragResize ? "Resize VFX Event" : "Move VFX Event");
            vfxDragRecorded = true;
        }

        evt.beat = newBeat;
        evt.durationBeats = newDuration;
        vfxDragChanged = true;
    }

    private void EndVfxDrag()
    {
        vfxDragging = false;
        vfxDragIndex = -1;
        if (vfxDragChanged)
            CommitVfxEdit();
        else
            RequestRepaint();
    }
}
