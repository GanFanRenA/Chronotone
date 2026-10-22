using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class ChartTimelineWindow : EditorWindow
{
    [SerializeField] private ChartPlayer targetPlayer;
    [SerializeField] private float pixelsPerBeat = 60f;
    [SerializeField] private float scrollBeat;
    [SerializeField] private float snap = 0.25f;
    [SerializeField] private float playheadBeat;

    private const float ToolbarHeight = 26f;
    private const float RulerHeight = 32f;
    private const float TrackHeight = 80f;
    private const float ScrollbarHeight = 16f;
    private const float MinPixelsPerBeat = 4f;
    private const float MaxPixelsPerBeat = 400f;
    private const float EdgeScrollZone = 40f;
    private const float EdgeScrollSpeed = 600f;
    private const string NodePrefabPath = "Assets/Prefabs/Node.prefab";

    private readonly List<NodeMarker> selection = new List<NodeMarker>();

    private Dictionary<NodeMarker, float> dragStartBeats;
    private float dragMouseStartBeat;
    private float dragMinBeat;
    private bool draggingNodes;

    private bool pendingClick;
    private Vector2 pendingStart;
    private Vector2 boxCurrent;
    private bool boxSelecting;

    private double lastEdgeScrollTime;

    private bool playing;
    private double playStartDspTime;
    private float playStartBeat;
    private AudioSource previewSource;

    [MenuItem("Window/Rhythm/Chart Timeline")]
    public static void Open() => GetWindow<ChartTimelineWindow>("Chart Timeline");

    private void OnEnable()
    {
        titleContent = new GUIContent("Chart Timeline");
        minSize = new Vector2(400, 200);
    }

    private void OnDisable() => StopPlayback();

    private void OnGUI()
    {
        if (HandleKeyboardShortcuts(Event.current))
            return;

        DrawToolbar();

        if (targetPlayer == null || targetPlayer.chart == null)
        {
            EditorGUILayout.HelpBox("请在上方选择 ChartPlayer（且它绑定了 ChartSO）", MessageType.Info);
            return;
        }

        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;
        NodeMarker[] markers = root.GetComponentsInChildren<NodeMarker>(false);
        System.Array.Sort(markers, (a, b) =>
        {
            int c = a.beat.CompareTo(b.beat);
            return c != 0 ? c : a.order.CompareTo(b.order);
        });

        selection.RemoveAll(m => m == null || !markers.Contains(m));

        Rect rulerRect = new Rect(0, ToolbarHeight, position.width, RulerHeight);
        Rect trackRect = new Rect(0, ToolbarHeight + RulerHeight, position.width, TrackHeight);
        Rect scrollRect = new Rect(0, ToolbarHeight + RulerHeight + TrackHeight, position.width, ScrollbarHeight);

        UpdatePlayheadIfPlaying();

        DrawRuler(rulerRect);
        DrawTrack(trackRect, markers);
        DrawPlayhead(rulerRect, trackRect);
        DrawScrollbar(scrollRect, markers);
        DrawBoxSelection();
        HandleInput(rulerRect, trackRect, scrollRect, markers);

        if (DoEdgeScroll(trackRect))
        {
            if (draggingNodes) UpdateDrag(Event.current, trackRect);
            Repaint();
        }
    }

    // ====================== 播放头 ======================

    private void UpdatePlayheadIfPlaying()
    {
        if (!playing) return;

        if (previewSource != null && !previewSource.isPlaying)
        {
            StopPlayback();
            return;
        }

        float startSec = targetPlayer.chart.bpmTimeline.BeatToSeconds(playStartBeat);
        float elapsed = (float)(AudioSettings.dspTime - playStartDspTime);
        if (elapsed < 0f) elapsed = 0f;
        playheadBeat = SecondsToBeat(startSec + elapsed, targetPlayer.chart.bpmTimeline);

        float viewBeats = Mathf.Max(1f, position.width) / pixelsPerBeat;
        if (playheadBeat > scrollBeat + viewBeats * 0.85f)
            scrollBeat = Mathf.Max(0f, playheadBeat - viewBeats * 0.2f);

        Repaint();
    }

    private static float SecondsToBeat(float seconds, BpmTimeline timeline)
    {
        if (timeline == null || timeline.segments == null || timeline.segments.Count == 0)
            return seconds * 2f;

        float acc = 0f;
        for (int i = 0; i < timeline.segments.Count; i++)
        {
            BpmSegment seg = timeline.segments[i];
            float nextBeat = (i + 1 < timeline.segments.Count)
                ? timeline.segments[i + 1].beat
                : float.MaxValue;

            if (nextBeat == float.MaxValue)
                return seg.beat + (seconds - acc) * seg.bpm / 60f;

            float segDuration = (nextBeat - seg.beat) * 60f / seg.bpm;
            if (acc + segDuration >= seconds)
                return seg.beat + (seconds - acc) * seg.bpm / 60f;
            acc += segDuration;
        }

        BpmSegment last = timeline.segments[timeline.segments.Count - 1];
        return last.beat + (seconds - acc) * last.bpm / 60f;
    }

    private void TogglePlayback()
    {
        if (playing) StopPlayback();
        else StartPlayback();
    }

    private void StartPlayback()
    {
        playing = true;
        playStartBeat = playheadBeat;

        AudioClip clip = targetPlayer.chart.clip;
        if (clip != null)
        {
            var go = new GameObject("~ChartPreviewAudio") { hideFlags = HideFlags.HideAndDontSave };
            previewSource = go.AddComponent<AudioSource>();
            previewSource.clip = clip;
            previewSource.playOnAwake = false;
            previewSource.time = targetPlayer.chart.bpmTimeline.BeatToSeconds(playheadBeat);
            playStartDspTime = AudioSettings.dspTime + 0.05;
            previewSource.PlayScheduled(playStartDspTime);
        }
        else
        {
            playStartDspTime = AudioSettings.dspTime;
        }
    }

    private void StopPlayback()
    {
        playing = false;
        if (previewSource != null)
        {
            previewSource.Stop();
            if (previewSource.gameObject != null) DestroyImmediate(previewSource.gameObject);
            previewSource = null;
        }
    }

    private void DrawPlayhead(Rect rulerRect, Rect trackRect)
    {
        float x = rulerRect.x + (playheadBeat - scrollBeat) * pixelsPerBeat;
        if (x < -2 || x > position.width + 2) return;

        Color c = playing ? new Color(1f, 0.25f, 0.25f) : new Color(1f, 0.6f, 0.6f);
        EditorGUI.DrawRect(new Rect(x - 1, rulerRect.y, 2, rulerRect.height), c);
        EditorGUI.DrawRect(new Rect(x - 1, trackRect.y, 2, trackRect.height), c);
        EditorGUI.DrawRect(new Rect(x - 5, rulerRect.yMax - 10, 10, 10), c);
    }

    // ====================== 边缘滚动 ======================

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

        float newScroll = Mathf.Max(0f, scrollBeat + delta / pixelsPerBeat);
        if (Mathf.Approximately(newScroll, scrollBeat)) return false;

        scrollBeat = newScroll;
        return true;
    }

    // ====================== 绘制 ======================

    private void DrawToolbar()
    {
        GUILayout.BeginArea(new Rect(4, 3, position.width - 8, ToolbarHeight - 3));
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        targetPlayer = (ChartPlayer)EditorGUILayout.ObjectField(
            targetPlayer, typeof(ChartPlayer), true, GUILayout.Width(150));

        GUILayout.Label("吸附", GUILayout.Width(28));
        string[] options = { "1", "1/2", "1/4", "1/8", "关" };
        float[] values = { 1f, 0.5f, 0.25f, 0.125f, 0f };
        int current = Mathf.Max(0, System.Array.IndexOf(values, snap));
        int next = EditorGUILayout.Popup(current, options, GUILayout.Width(48));
        if (next != current) snap = values[next];

        GUILayout.Space(8);
        if (GUILayout.Button(playing ? "■ 停" : "▶ 播", EditorStyles.toolbarButton, GUILayout.Width(50)))
            TogglePlayback();

        GUILayout.Label($"B:{playheadBeat:0.##}", EditorStyles.miniLabel, GUILayout.Width(70));

        GUILayout.FlexibleSpace();
        GUILayout.Label($"{pixelsPerBeat:0.#} px/beat", EditorStyles.miniLabel);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
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

        foreach (NodeMarker m in markers)
        {
            float x = (m.beat - scrollBeat) * pixelsPerBeat;
            if (x < -10 || x > rect.width + 10) continue;

            Color c = selection.Contains(m)
                ? Color.yellow
                : Color.HSVToRGB(Mathf.Repeat(m.order * 0.17f, 1f), 0.65f, 0.9f);
            EditorGUI.DrawRect(new Rect(x - 2, rect.y + 4, 4, rect.height - 8), c);
        }
    }

    private void DrawScrollbar(Rect rect, NodeMarker[] markers)
    {
        float viewBeats = Mathf.Max(1f, position.width) / pixelsPerBeat;
        float maxBeat = 0f;
        foreach (var m in markers) maxBeat = Mathf.Max(maxBeat, m.beat);
        float totalBeats = Mathf.Max(viewBeats, Mathf.Max(maxBeat, playheadBeat) + 4f);
        float maxScroll = Mathf.Max(0f, totalBeats - viewBeats);

        float newScroll = GUI.HorizontalScrollbar(rect, scrollBeat, viewBeats, 0f, totalBeats);
        newScroll = Mathf.Clamp(newScroll, 0f, maxScroll);
        if (!Mathf.Approximately(newScroll, scrollBeat))
        {
            scrollBeat = newScroll;
            Repaint();
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

    // ====================== 输入 ======================

    private void HandleInput(Rect rulerRect, Rect trackRect, Rect scrollRect, NodeMarker[] markers)
    {
        Event e = Event.current;

        if (scrollRect.Contains(e.mousePosition)) return;

        // 标尺：点击设置播放头
        if (e.type == EventType.MouseDown && e.button == 0 && rulerRect.Contains(e.mousePosition))
        {
            float newBeat = scrollBeat + (e.mousePosition.x - rulerRect.x) / pixelsPerBeat;
            if (snap > 0f) newBeat = Mathf.Round(newBeat / snap) * snap;
            playheadBeat = Mathf.Max(0f, newBeat);
            if (playing) { StopPlayback(); StartPlayback(); }
            e.Use(); Repaint(); return;
        }

        // 滚轮：默认水平滚动，Ctrl 缩放
        if (e.type == EventType.ScrollWheel && trackRect.Contains(e.mousePosition))
        {
            bool ctrl = e.control || e.command;
            if (ctrl)
            {
                float mouseBeat = scrollBeat + (e.mousePosition.x - trackRect.x) / pixelsPerBeat;
                pixelsPerBeat = Mathf.Clamp(
                    pixelsPerBeat * Mathf.Pow(1.1f, -e.delta.y),
                    MinPixelsPerBeat, MaxPixelsPerBeat);
                scrollBeat = Mathf.Max(0f, mouseBeat - (e.mousePosition.x - trackRect.x) / pixelsPerBeat);
            }
            else
            {
                float scrollDelta = (Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y))
                    ? e.delta.x * 100f / pixelsPerBeat
                    : e.delta.y * 100f / pixelsPerBeat;
                scrollBeat = Mathf.Max(0f, scrollBeat + scrollDelta);
            }
            e.Use(); Repaint(); return;
        }

        // 中键平移
        if (e.type == EventType.MouseDrag && e.button == 2 &&
            (rulerRect.Contains(e.mousePosition) || trackRect.Contains(e.mousePosition)))
        {
            scrollBeat = Mathf.Max(0f, scrollBeat - e.delta.x / pixelsPerBeat);
            e.Use(); Repaint(); return;
        }

        // 轨道左键按下
        if (e.type == EventType.MouseDown && e.button == 0 && trackRect.Contains(e.mousePosition))
        {
            bool ctrl = e.control || e.command;
            bool shift = e.shift;

            NodeMarker hit = FindMarkerAt(markers, e.mousePosition.x, trackRect);
            if (hit != null)
            {
                if (ctrl)
                {
                    if (selection.Contains(hit)) selection.Remove(hit);
                    else selection.Add(hit);
                }
                else if (shift)
                {
                    if (!selection.Contains(hit)) selection.Add(hit);
                }
                else
                {
                    if (!selection.Contains(hit))
                    {
                        selection.Clear();
                        selection.Add(hit);
                    }
                    BeginDrag(e, trackRect);
                }
                SyncSelectionToUnity();
                e.Use(); Repaint(); return;
            }
            else
            {
                if (!ctrl && !shift) selection.Clear();
                pendingClick = true;
                pendingStart = e.mousePosition;
                boxCurrent = e.mousePosition;
                boxSelecting = false;
                lastEdgeScrollTime = EditorApplication.timeSinceStartup;
                e.Use(); Repaint(); return;
            }
        }

        if (e.type == EventType.MouseDrag && e.button == 0)
        {
            if (draggingNodes)
            {
                UpdateDrag(e, trackRect);
                e.Use(); Repaint(); return;
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
                    e.Use(); Repaint(); return;
                }
            }
        }

        if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (draggingNodes)
            {
                EndDrag();
                e.Use(); Repaint(); return;
            }
            if (pendingClick)
            {
                if (boxSelecting) CompleteBoxSelection(markers, trackRect);
                else CreateNodeFromClick(e, trackRect, markers);

                pendingClick = false;
                boxSelecting = false;
                e.Use(); Repaint(); return;
            }
        }
    }

    // ====================== 辅助 ======================

    private bool HandleKeyboardShortcuts(Event e)
    {
        if (e.type != EventType.KeyDown || focusedWindow != this)
            return false;

        switch (e.keyCode)
        {
            case KeyCode.Delete:
                DeleteSelection();
                break;
            case KeyCode.Space:
                TogglePlayback();
                Repaint();
                break;
            case KeyCode.Escape:
                selection.Clear();
                SyncSelectionToUnity();
                Repaint();
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

    private void BeginDrag(Event e, Rect trackRect)
    {
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
        Undo.RegisterCompleteObjectUndo(selection.Cast<Object>().ToArray(), "Drag Nodes");
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
        draggingNodes = false;
        dragStartBeats = null;
    }

    private void CompleteBoxSelection(NodeMarker[] markers, Rect trackRect)
    {
        float x1 = Mathf.Min(pendingStart.x, boxCurrent.x);
        float x2 = Mathf.Max(pendingStart.x, boxCurrent.x);

        foreach (var m in markers)
        {
            float mx = trackRect.x + (m.beat - scrollBeat) * pixelsPerBeat;
            if (mx >= x1 && mx <= x2 && !selection.Contains(m))
                selection.Add(m);
        }
        SyncSelectionToUnity();
    }

    private void CreateNodeFromClick(Event e, Rect trackRect, NodeMarker[] markers)
    {
        float clickBeat = scrollBeat + (e.mousePosition.x - trackRect.x) / pixelsPerBeat;
        if (snap > 0f) clickBeat = Mathf.Round(clickBeat / snap) * snap;
        if (clickBeat < 0f) clickBeat = 0f;

        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;
        NodeMarker previous = null;
        foreach (NodeMarker candidate in markers)
        {
            if (candidate.beat > clickBeat) break;
            previous = candidate;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<NodeMarker>(NodePrefabPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, root);
        Undo.RegisterCreatedObjectUndo(go, "Create Node Marker");

        var marker = go.GetComponent<NodeMarker>();
        marker.beat = clickBeat;
        marker.order = 0;
        marker.transform.position = previous != null ? previous.transform.position : root.position;
        PrefabUtility.RecordPrefabInstancePropertyModifications(marker);

        targetPlayer.MarkDirty();
        EditorUtility.SetDirty(targetPlayer);
        Selection.activeGameObject = go;
    }

    private void DeleteSelection()
    {
        if (selection.Count == 0) return;
        foreach (var m in selection)
            if (m != null) Undo.DestroyObjectImmediate(m.gameObject);
        selection.Clear();
        SyncSelectionToUnity();
        targetPlayer.MarkDirty();
        Repaint();
    }

    private void AdjustOrder(int delta)
    {
        if (selection.Count == 0) return;
        Undo.RegisterCompleteObjectUndo(selection.Cast<Object>().ToArray(), "Adjust Order");
        foreach (var m in selection)
        {
            if (m == null) continue;
            m.order += delta;
            EditorUtility.SetDirty(m);
        }
        targetPlayer.MarkDirty();
        Repaint();
    }

    private void SyncSelectionToUnity()
    {
        var objs = selection.Where(m => m != null).Cast<Object>().ToArray();
        if (!Selection.objects.SequenceEqual(objs))
            Selection.objects = objs;
    }

    private void OnSelectionChange()
    {
        if (targetPlayer == null) return;
        var markers = Selection.GetFiltered<NodeMarker>(SelectionMode.Editable | SelectionMode.Deep);
        if (markers == null || markers.Length == 0) return;

        Transform root = targetPlayer.scanRoot != null ? targetPlayer.scanRoot : targetPlayer.transform;
        if (!markers.All(m => m != null && m.transform.IsChildOf(root))) return;

        var newSelection = new List<NodeMarker>(markers);
        if (!selection.SequenceEqual(newSelection))
        {
            selection.Clear();
            selection.AddRange(newSelection);
        }

        float minBeat = newSelection.Min(m => m.beat);
        float viewBeats = Mathf.Max(1f, position.width) / pixelsPerBeat;
        if (minBeat < scrollBeat || minBeat > scrollBeat + viewBeats * 0.8f)
            scrollBeat = Mathf.Max(0f, minBeat - viewBeats * 0.2f);

        Repaint();
    }
}
