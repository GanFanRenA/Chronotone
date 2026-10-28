using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class ChartTimelineWindow
{
    private void HandleInput(Rect rulerRect, Rect trackRect, Rect scrollRect, NodeMarker[] markers)
    {
        Event e = Event.current;

        if (scrollRect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDown && e.button == 0 && rulerRect.Contains(e.mousePosition))
        {
            float newBeat = scrollBeat + (e.mousePosition.x - rulerRect.x) / pixelsPerBeat;
            if (snap > 0f) newBeat = Mathf.Round(newBeat / snap) * snap;
            playStartBeat = Mathf.Max(0f, newBeat);
            playheadBeat = playStartBeat;
            if (playing) StartPlayback();
            e.Use(); RequestRepaint(); return;
        }

        if (e.type == EventType.ScrollWheel && trackRect.Contains(e.mousePosition))
        {
            bool ctrl = e.control || e.command;
            if (ctrl)
            {
                float mouseBeat = scrollBeat + (e.mousePosition.x - trackRect.x) / pixelsPerBeat;
                pixelsPerBeat = Mathf.Clamp(
                    pixelsPerBeat * Mathf.Pow(1.1f, -e.delta.y),
                    MinPixelsPerBeat, MaxPixelsPerBeat);
                scrollBeat = Mathf.Max(
                    MinScrollBeat,
                    mouseBeat - (e.mousePosition.x - trackRect.x) / pixelsPerBeat);
            }
            else
            {
                float scrollDelta = (Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y))
                    ? e.delta.x * 100f / pixelsPerBeat
                    : e.delta.y * 100f / pixelsPerBeat;
                scrollBeat = Mathf.Max(MinScrollBeat, scrollBeat + scrollDelta);
            }
            e.Use(); RequestRepaint(); return;
        }

        if (e.type == EventType.MouseDrag && e.button == 2 &&
            (rulerRect.Contains(e.mousePosition) || trackRect.Contains(e.mousePosition)))
        {
            scrollBeat = Mathf.Max(
                MinScrollBeat,
                scrollBeat - e.delta.x / pixelsPerBeat);
            e.Use(); RequestRepaint(); return;
        }

        if (e.type == EventType.MouseDown && e.button == 0 && trackRect.Contains(e.mousePosition))
        {
            bool ctrl = e.control || e.command;
            bool shift = e.shift;

            NodeMarker hit = FindMarkerAt(markers, e.mousePosition.x, trackRect);
            if (hit != null)
            {
                boxSelectionBase.Clear();
                if (ctrl)
                {
                    if (IsSelected(hit)) RemoveFromSelection(hit);
                    else
                    {
                        AddToSelection(hit);
                        if (rangeSelectionAnchor == null)
                            rangeSelectionAnchor = hit;
                    }
                }
                else if (shift)
                {
                    SelectRangeTo(hit, markers);
                }
                else
                {
                    ClearSelection();
                    AddToSelection(hit);
                    rangeSelectionAnchor = hit;
                    BeginDrag(e, trackRect);
                }
                SyncSelectionToUnity();
                e.Use(); RequestRepaint(); return;
            }
            else
            {
                if (!ctrl && !shift)
                {
                    ClearSelection();
                    SyncSelectionToUnity();
                }
                pendingClick = true;
                pendingStart = e.mousePosition;
                boxCurrent = e.mousePosition;
                pendingStartBeat = ScreenXToBeat(e.mousePosition.x, trackRect);
                boxCurrentBeat = pendingStartBeat;
                boxSelecting = false;
                boxSelectionBase.Clear();
                boxSelectionBase.AddRange(selection);
                lastEdgeScrollTime = EditorApplication.timeSinceStartup;
                e.Use(); RequestRepaint(); return;
            }
        }

        if (e.type == EventType.MouseDrag && e.button == 0)
        {
            if (draggingNodes)
            {
                UpdateDrag(e, trackRect);
                e.Use(); RequestRepaint(); return;
            }
            else if (pendingClick)
            {
                if ((e.mousePosition - pendingStart).sqrMagnitude > 16f)
                {
                    if (!boxSelecting) lastEdgeScrollTime = EditorApplication.timeSinceStartup;
                    boxSelecting = true;
                }
                if (boxSelecting)
                {
                    boxCurrent = e.mousePosition;
                    boxCurrentBeat = ScreenXToBeat(e.mousePosition.x, trackRect);
                    ApplyBoxSelection(markers);
                    e.Use(); RequestRepaint(); return;
                }
            }
        }

        if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (draggingNodes)
            {
                EndDrag();
                e.Use(); RequestRepaint(); return;
            }
            if (pendingClick)
            {
                bool movedBeyondThreshold =
                    (e.mousePosition - pendingStart).sqrMagnitude > 16f;
                if (boxSelecting || movedBeyondThreshold)
                {
                    boxSelecting = true;
                    boxCurrent = e.mousePosition;
                    boxCurrentBeat = ScreenXToBeat(e.mousePosition.x, trackRect);
                    CompleteBoxSelection(markers);
                }
                else MovePlayheadFromClick(e, trackRect);

                pendingClick = false;
                boxSelecting = false;
                boxSelectionBase.Clear();
                e.Use(); RequestRepaint(); return;
            }
        }
    }

    private bool HandleKeyboardShortcuts(Event e)
    {
        if (e.type != EventType.KeyDown || focusedWindow != this)
            return false;
        if (EditorGUIUtility.editingTextField)
            return false;

        switch (e.keyCode)
        {
            case KeyCode.Delete:
                DeleteSelection();
                break;
            case KeyCode.A:
                if (!e.control && !e.command) return false;
                SelectAllNodes();
                break;
            case KeyCode.Space:
                TogglePlayback();
                RequestRepaint();
                break;
            case KeyCode.R:
                if (e.control || e.command || e.alt) return false;
                ReplayPlayback();
                RequestRepaint();
                break;
            case KeyCode.F:
                if (e.control || e.command || e.alt) return false;
                ReturnToPlayStart();
                break;
            case KeyCode.J:
                if (e.control || e.command || e.alt) return false;
                CreateNodeAtPlayhead();
                RequestRepaint();
                break;
            case KeyCode.Escape:
                ClearSelection();
                SyncSelectionToUnity();
                RequestRepaint();
                break;
            case KeyCode.LeftBracket:
                AdjustOrder(-1);
                break;
            case KeyCode.RightBracket:
                AdjustOrder(+1);
                break;
            default:
                return false;
        }

        e.Use();
        return true;
    }

    private NodeMarker FindMarkerAt(NodeMarker[] markers, float mouseX, Rect trackRect)
    {
        const float threshold = 6f;
        NodeMarker best = null;
        float bestDist = threshold;
        foreach (NodeMarker m in markers)
        {
            float x = trackRect.x + (m.beat - scrollBeat) * pixelsPerBeat;
            float d = Mathf.Abs(x - mouseX);
            if (d < bestDist) { bestDist = d; best = m; }
        }
        return best;
    }

    private float ScreenXToBeat(float screenX, Rect trackRect) =>
        scrollBeat + (screenX - trackRect.x) / pixelsPerBeat;

    private void MovePlayheadFromClick(Event e, Rect trackRect)
    {
        float beat = ScreenXToBeat(e.mousePosition.x, trackRect);
        if (snap > 0f)
            beat = Mathf.Round(beat / snap) * snap;

        playheadBeat = Mathf.Max(0f, beat);
        if (playing)
            StartPlayback();

        RequestRepaint();
    }

    private void BeginDrag(Event e, Rect trackRect)
    {
        targetPlayer.BeginPositionUpdateBatch();
        draggingNodes = true;
        dragMouseStartBeat = scrollBeat + (e.mousePosition.x - trackRect.x) / pixelsPerBeat;
        dragStartBeats = new Dictionary<NodeMarker, float>();
        dragMinBeat = float.MaxValue;
        foreach (var m in selection)
        {
            dragStartBeats[m] = m.beat;
            dragMinBeat = Mathf.Min(dragMinBeat, m.beat);
        }
        lastEdgeScrollTime = EditorApplication.timeSinceStartup;
        Object[] undoObjects = selection
            .Where(m => m != null)
            .SelectMany(m => new Object[] { m, m.transform })
            .ToArray();
        Undo.RegisterCompleteObjectUndo(undoObjects, "Drag Nodes");
    }

    private void UpdateDrag(Event e, Rect trackRect)
    {
        float mouseBeat = scrollBeat + (e.mousePosition.x - trackRect.x) / pixelsPerBeat;
        float delta = mouseBeat - dragMouseStartBeat;

        if (!(e.control || e.command) && snap > 0f)
            delta = Mathf.Round(delta / snap) * snap;

        delta = Mathf.Max(delta, -dragMinBeat);

        foreach (var kv in dragStartBeats)
        {
            if (kv.Key == null) continue;
            float newBeat = kv.Value + delta;
            if (!Mathf.Approximately(kv.Key.beat, newBeat))
            {
                kv.Key.beat = newBeat;
                EditorUtility.SetDirty(kv.Key);
            }
        }
        targetPlayer.MarkDirty();
    }

    private void EndDrag()
    {
        float fromBeat = GetEarliestSelectedBeat();
        draggingNodes = false;
        dragStartBeats = null;
        targetPlayer.EndPositionUpdateBatch(fromBeat);
        InvalidateMarkerCache();
    }

    private float GetEarliestSelectedBeat()
    {
        if (selection.Count == 0) return 0f;

        float earliestBeat = float.MaxValue;
        foreach (NodeMarker marker in selection)
        {
            if (marker != null)
                earliestBeat = Mathf.Min(earliestBeat, marker.beat);
        }
        return earliestBeat == float.MaxValue ? 0f : earliestBeat;
    }

    private void CompleteBoxSelection(NodeMarker[] markers)
    {
        ApplyBoxSelection(markers);
        SyncSelectionToUnity();
    }

    private void ApplyBoxSelection(NodeMarker[] markers)
    {
        ReplaceSelection(boxSelectionBase);

        float firstBeat = Mathf.Min(pendingStartBeat, boxCurrentBeat);
        float lastBeat = Mathf.Max(pendingStartBeat, boxCurrentBeat);

        foreach (var m in markers)
        {
            if (m.beat >= firstBeat && m.beat <= lastBeat)
                AddToSelection(m);
        }
    }
}
