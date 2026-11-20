using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class ChartTimelineWindow
{
    private const float VfxLaneHeight = 56f;
    private const float VfxLaneHeaderHeight = 16f;
    private const float VfxDotSize = 8f;
    private const float VfxDetailPanelHeight = 64f;
    private const float VfxHitThreshold = 6f;

    private static readonly Color ScreenShakeColor =
        new Color(0.85f, 0.5f, 1f);

    private int selectedVfxIndex = -1;
    private bool vfxDragging;
    private int vfxDragIndex = -1;
    private float vfxDragStartBeat;
    private float vfxDragMouseStartBeat;
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

            float x = rect.x + (e.beat - scrollBeat) * pixelsPerBeat;
            if (x < -10 || x > rect.width + 10)
                continue;

            Color color = ColorForVfxKind(e.kind);
            Rect dotRect = new Rect(
                x - VfxDotSize * 0.5f,
                centerY - VfxDotSize * 0.5f,
                VfxDotSize,
                VfxDotSize);
            DrawDot(dotRect, color);

            GUI.Label(dotRect, new GUIContent(string.Empty, VfxTooltip(e)));

            if (i == selectedVfxIndex)
                DrawSelectionOutline(InflateRect(dotRect, 2f));
        }
    }

    private void DrawVfxLegend(Rect header)
    {
        DrawLegendItem(4f, header.y + 2f, ScreenShakeColor, "屏幕震动", 72f);
        GUI.Label(
            new Rect(84f, header.y + 2f, header.width - 88f, 14f),
            "右键添加 · 拖动改拍点 · Delete 删除",
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
            new Rect(x, y, fieldWidth, 18f), "时长s", e.duration);
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
            e.duration = Mathf.Max(0f, newDuration);
            e.strength = Mathf.Max(0f, newStrength);
            e.frequency = Mathf.Max(0f, newFrequency);
            CommitVfxEdit();
        }
    }

    private static Rect InflateRect(Rect rect, float amount)
    {
        return new Rect(
            rect.x - amount,
            rect.y - amount,
            rect.width + amount * 2f,
            rect.height + amount * 2f);
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
            return $"屏幕震动 · beat {e.beat:0.###} · " +
                   $"{e.duration:0.##}s · 强度 {e.strength:0.##} · 频率 {e.frequency:0.##}";
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
            int hit = FindVfxEventAt(events, e.mousePosition.x, rect);
            if (hit >= 0)
            {
                selectedVfxIndex = hit;
                BeginVfxDrag(e, rect, events);
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

        int hit = FindVfxEventAt(events, mouse.x, rect);

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

    private int FindVfxEventAt(
        List<StageVfxEvent> events,
        float mouseX,
        Rect rect)
    {
        if (events == null)
            return -1;

        int best = -1;
        float bestDistance = VfxHitThreshold;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i] == null)
                continue;

            float x = rect.x + (events[i].beat - scrollBeat) * pixelsPerBeat;
            float distance = Mathf.Abs(x - mouseX);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private float ScreenXToBeatVfx(float screenX, Rect rect) =>
        scrollBeat + (screenX - rect.x) / pixelsPerBeat;

    private void BeginVfxDrag(Event e, Rect rect, List<StageVfxEvent> events)
    {
        if (selectedVfxIndex < 0 ||
            events == null ||
            selectedVfxIndex >= events.Count)
        {
            return;
        }

        vfxDragging = true;
        vfxDragChanged = false;
        vfxDragRecorded = false;
        vfxDragIndex = selectedVfxIndex;
        vfxDragStartBeat = events[selectedVfxIndex].beat;
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

        float mouseBeat = ScreenXToBeatVfx(e.mousePosition.x, rect);
        float delta = mouseBeat - vfxDragMouseStartBeat;
        if (!(e.control || e.command) && snap > 0f)
            delta = Mathf.Round(delta / snap) * snap;

        float newBeat = Mathf.Max(0f, vfxDragStartBeat + delta);
        if (Mathf.Approximately(events[vfxDragIndex].beat, newBeat))
            return;

        if (!vfxDragRecorded)
        {
            RecordVfxUndo("Move VFX Event");
            vfxDragRecorded = true;
        }

        events[vfxDragIndex].beat = newBeat;
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
