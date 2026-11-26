using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[System.Serializable]
public struct NodeClipboardEntry
{
    public float beat;
    public int order;
    public NodeAction action;
    public NodeGravity gravity;
    public bool auto;
    public bool visible;
    public float strength;
    public float positionY;
}

public partial class ChartTimelineWindow : EditorWindow
{
    [SerializeField] private ChartPlayer targetPlayer;
    [SerializeField] private float pixelsPerBeat = 60f;
    [SerializeField] private float scrollBeat;
    [SerializeField] private float snap = 0.25f;
    [SerializeField] private float playheadBeat;
    [SerializeField] private float playStartBeat;
    [SerializeField] private float loopEndBeat;
    [SerializeField] private bool loopEnabled;
    [SerializeField] private List<float> beatMarks = new List<float>();
    [SerializeField] private List<int> beatMarkLoopCounts = new List<int>();
    [SerializeField] private List<NodeClipboardEntry> clipboardNodes = new List<NodeClipboardEntry>();
    [SerializeField] private float playbackSpeed = 1f;
    [SerializeField] private NodeMarker nodePrefab;
    [SerializeField] private bool previewEnabled;

    private const float ToolbarHeight = 54f;
    private const float RulerHeight = 32f;
    private const float ScrollbarHeight = 16f;
    private const float MinPixelsPerBeat = 4f;
    private const float MaxPixelsPerBeat = 400f;
    private const float EdgeScrollZone = 40f;
    private const float EdgeScrollSpeed = 600f;
    private const float RedAxisLeftInsetPixels = 24f;
    private const string NodePrefabPath = "Assets/Prefabs/Node.prefab";
    private const float PlaybackLeadTime = 0.1f;
    private const int BeatMarkMaxLoops = 5;
    private static readonly string[] SnapLabels = { "1", "1/2", "1/4", "1/8", "1/16", "1/32", "关" };
    private static readonly float[] SnapValues = { 1f, 0.5f, 0.25f, 0.125f, 0.0625f, 0.03125f, 0f };
    private static readonly GUIContent OffsetLabel =
        new GUIContent("偏移秒", "全局音画偏移，单位是秒；正值让谱面整体晚于音乐。");
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
        minSize = new Vector2(400, 280);
        NodeAppearanceSet.Changed -= OnNodeAppearanceChanged;
        NodeAppearanceSet.Changed += OnNodeAppearanceChanged;
        NodeMarker.EditorChanged -= OnNodeMarkerChanged;
        NodeMarker.EditorChanged += OnNodeMarkerChanged;
        StageFlyAnimation.EditorChanged -= InvalidateAnimationEvents;
        StageFlyAnimation.EditorChanged += InvalidateAnimationEvents;
        StageDisplayAnimation.EditorChanged -= InvalidateAnimationEvents;
        StageDisplayAnimation.EditorChanged += InvalidateAnimationEvents;
        if (nodePrefab == null)
            nodePrefab = AssetDatabase.LoadAssetAtPath<NodeMarker>(NodePrefabPath);
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
        Undo.postprocessModifications -= OnPostprocessModifications;
        Undo.postprocessModifications += OnPostprocessModifications;
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
        NodeMarker.EditorChanged -= OnNodeMarkerChanged;
        StageFlyAnimation.EditorChanged -= InvalidateAnimationEvents;
        StageDisplayAnimation.EditorChanged -= InvalidateAnimationEvents;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        Undo.postprocessModifications -= OnPostprocessModifications;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SceneView.duringSceneGui -= DrawTrajectoryPreview;
        StopPlayback();
        DisposeAnimationMoveTargets();
        CancelSegmentRename();
        segmentDragging = false;
        if (previewSource != null && previewSource.gameObject != null)
            DestroyImmediate(previewSource.gameObject);
        previewSource = null;
    }

    private void OnNodeAppearanceChanged(NodeAppearanceSet appearanceSet)
    {
        InvalidateMarkers();
    }

    private void OnNodeMarkerChanged(NodeMarker marker)
    {
        if (marker == null)
            return;

        InvalidateMarkers();
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
        CancelAnimationDrag();
        CancelSegmentRename();
        segmentDragging = false;
    }

    private void OnGUI()
    {
        if (HandleKeyboardShortcuts(Event.current))
            return;

        DrawToolbar();

        if (targetPlayer == null)
        {
            StopPlayback();
            EditorGUILayout.HelpBox("请在上方选择 ChartPlayer", MessageType.Info);
            return;
        }

        NodeMarker[] markers = GetMarkers();
        List<StageAnimationEvent> animationEvents = GetAnimationEvents();
        List<StageVfxEvent> vfxEvents = GetVfxEvents();
        ValidateVfxSelection();

        float detailHeight = selectedVfxIndex >= 0 ? VfxDetailPanelHeight : 0f;
        ResolveLaneHeights(detailHeight, out float nodeHeight, out float vfxHeight, out float animationHeight);
        ValidateSegmentSelection();

        Rect rulerRect = new Rect(0, ToolbarHeight, position.width, RulerHeight);
        Rect segmentRect = new Rect(0, ToolbarHeight + RulerHeight, position.width, SegmentLaneHeight);
        Rect trackRect = new Rect(
            0,
            ToolbarHeight + RulerHeight + SegmentLaneHeight,
            position.width,
            nodeHeight);
        Rect vfxRect = new Rect(
            0,
            ToolbarHeight + RulerHeight + SegmentLaneHeight + nodeHeight,
            position.width,
            vfxHeight);
        Rect animationRect = new Rect(
            0,
            ToolbarHeight + RulerHeight + SegmentLaneHeight + nodeHeight + vfxHeight,
            position.width,
            animationHeight);
        Rect scrollRect = new Rect(
            0,
            ToolbarHeight + RulerHeight + SegmentLaneHeight + nodeHeight + vfxHeight +
            animationHeight,
            position.width,
            ScrollbarHeight);
        Rect detailRect = new Rect(
            0,
            ToolbarHeight + RulerHeight + SegmentLaneHeight + nodeHeight + vfxHeight +
            animationHeight + ScrollbarHeight,
            position.width,
            detailHeight);

        UpdatePlayheadIfPlaying();

        DrawRuler(rulerRect);
        DrawSegmentLane(segmentRect);
        DrawTrack(trackRect, markers);
        DrawVfxLane(vfxRect, vfxEvents);
        DrawAnimationLane(animationRect, animationEvents);
        DrawLaneDividers(trackRect, vfxRect);
        DrawPlayhead(rulerRect, segmentRect, trackRect, vfxRect, animationRect);
        DrawBeatMarks(rulerRect, segmentRect, trackRect, vfxRect, animationRect);
        DrawScrollbar(scrollRect, markers, animationEvents, vfxEvents);
        DrawBoxSelection();

        if (!HandleLaneDividerInput(trackRect, vfxRect, detailHeight))
        {
            HandleSegmentInput(segmentRect);
            HandleInput(rulerRect, trackRect, scrollRect, markers);
            HandleVfxInput(vfxRect);
            HandleAnimationInput(animationRect);
        }
        DrawVfxDetailPanel(detailRect);

        if (DoEdgeScroll(trackRect))
        {
            if (draggingNodes) UpdateDrag(Event.current, trackRect);
            RequestRepaint();
        }
    }
}
