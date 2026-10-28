using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class ChartTimelineWindow : EditorWindow
{
    [SerializeField] private ChartPlayer targetPlayer;
    [SerializeField] private float pixelsPerBeat = 60f;
    [SerializeField] private float scrollBeat;
    [SerializeField] private float snap = 0.25f;
    [SerializeField] private float playheadBeat;
    [SerializeField] private float playStartBeat;
    [SerializeField] private float playbackSpeed = 1f;
    [SerializeField] private NodeMarker nodePrefab;
    [SerializeField] private bool previewEnabled;
    [SerializeField] private bool nodeClickEnabled;

    private const float ToolbarHeight = 54f;
    private const float RulerHeight = 32f;
    private const float TrackHeight = 80f;
    private const float ScrollbarHeight = 16f;
    private const float MinPixelsPerBeat = 4f;
    private const float MaxPixelsPerBeat = 400f;
    private const float EdgeScrollZone = 40f;
    private const float EdgeScrollSpeed = 600f;
    private const float RedAxisLeftInsetPixels = 24f;
    private const string NodePrefabPath = "Assets/Prefabs/Node.prefab";
    private const float PlaybackLeadTime = 0.1f;
    private static readonly string[] SnapLabels = { "1", "1/2", "1/4", "1/8", "1/16", "1/32", "关" };
    private static readonly float[] SnapValues = { 1f, 0.5f, 0.25f, 0.125f, 0.0625f, 0.03125f, 0f };
    private static readonly GUIContent OffsetLabel =
        new GUIContent("偏移拍", "正值让谱面整体晚于音乐；单位是拍。");
    private static readonly GUIContent PlaybackSpeedLabel =
        new GUIContent("速度", "只调整时间轴试听速度，不改变游戏运行速度。范围 0.1–2.0。");

    private readonly List<NodeMarker> selection = new List<NodeMarker>();
    private readonly HashSet<NodeMarker> selectionLookup = new HashSet<NodeMarker>();
    private readonly List<NodeMarker> boxSelectionBase = new List<NodeMarker>();
    private NodeMarker[] cachedMarkers = System.Array.Empty<NodeMarker>();
    private Transform cachedMarkerRoot;
    private bool markersDirty = true;
    private bool windowVisible = true;

    private Dictionary<NodeMarker, float> dragStartBeats;
    private float dragMouseStartBeat;
    private float dragMinBeat;
    private bool draggingNodes;

    private bool pendingClick;
    private Vector2 pendingStart;
    private Vector2 boxCurrent;
    private float pendingStartBeat;
    private float boxCurrentBeat;
    private bool boxSelecting;

    private double lastEdgeScrollTime;

    private bool playing;
    private bool playbackUsesAudio;
    private double playStartDspTime;
    private float playbackAnchorBeat;
    private AudioSource previewSource;

    private float MinScrollBeat =>
        -RedAxisLeftInsetPixels / pixelsPerBeat;

    [MenuItem("Window/Rhythm/Chart Timeline")]
    public static void Open() => GetWindow<ChartTimelineWindow>("Chart Timeline");

    private void OnEnable()
    {
        titleContent = new GUIContent("Chart Timeline");
        minSize = new Vector2(400, 200);
        NodeAppearanceSet.Changed -= OnNodeAppearanceChanged;
        NodeAppearanceSet.Changed += OnNodeAppearanceChanged;
        if (nodePrefab == null)
            nodePrefab = AssetDatabase.LoadAssetAtPath<NodeMarker>(NodePrefabPath);
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
        Undo.postprocessModifications -= OnPostprocessModifications;
        Undo.postprocessModifications += OnPostprocessModifications;
        EditorApplication.update -= UpdateNodeClickSchedule;
        EditorApplication.update += UpdateNodeClickSchedule;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        SceneView.duringSceneGui -= DrawTrajectoryPreview;
        SceneView.duringSceneGui += DrawTrajectoryPreview;
        windowVisible = true;
        InvalidateMarkerCache();
        EnsurePreviewSource();
        PreparePreviewClip();
    }

    private void OnDisable()
    {
        NodeAppearanceSet.Changed -= OnNodeAppearanceChanged;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        Undo.postprocessModifications -= OnPostprocessModifications;
        EditorApplication.update -= UpdateNodeClickSchedule;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SceneView.duringSceneGui -= DrawTrajectoryPreview;
        StopPlayback();
        if (previewSource != null && previewSource.gameObject != null)
            DestroyImmediate(previewSource.gameObject);
        previewSource = null;
    }

    private void OnNodeAppearanceChanged(NodeAppearanceSet appearanceSet)
    {
        InvalidateMarkerCache();
    }

    private void OnBecameVisible()
    {
        windowVisible = true;
        InvalidateMarkerCache();
    }

    private void OnBecameInvisible()
    {
        windowVisible = false;
        if (draggingNodes && targetPlayer != null)
            targetPlayer.EndPositionUpdateBatch(GetEarliestSelectedBeat());
        draggingNodes = false;
        dragStartBeats = null;
        pendingClick = false;
        boxSelecting = false;
        boxSelectionBase.Clear();
    }

    private void OnGUI()
    {
        if (HandleKeyboardShortcuts(Event.current))
            return;

        DrawToolbar();

        if (targetPlayer == null || targetPlayer.chart == null)
        {
            StopPlayback();
            EditorGUILayout.HelpBox("请在上方选择 ChartPlayer（且它绑定了 ChartSO）", MessageType.Info);
            return;
        }

        NodeMarker[] markers = GetMarkers();

        Rect rulerRect = new Rect(0, ToolbarHeight, position.width, RulerHeight);
        Rect trackRect = new Rect(0, ToolbarHeight + RulerHeight, position.width, TrackHeight);
        Rect scrollRect = new Rect(
            0,
            ToolbarHeight + RulerHeight + TrackHeight,
            position.width,
            ScrollbarHeight);

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
            RequestRepaint();
        }
    }
}
