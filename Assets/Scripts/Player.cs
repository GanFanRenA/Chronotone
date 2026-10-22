using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class Player : MonoBehaviour
{
    public ChartPlayer chartPlayer;

    [Tooltip("调度前置时间，给音频混音器准备缓冲")]
    public double scheduleLead = 0.15;

    private IReadOnlyList<NodeData> nodes;
    private float[] nodeTimes;
    private AudioSource audioSource;
    private double startTime;   // dsp 时钟上的绝对开始时刻
    private int currentIndex;
    private bool running;

    public bool IsRunning => running;
    public double SongTime => running ? AudioSettings.dspTime - startTime : 0d;

    void Awake()
    {
        if (!TryGetComponent<BasicJudgement>(out _))
            gameObject.AddComponent<BasicJudgement>();
    }

    void Start()
    {
        if (chartPlayer == null || chartPlayer.chart == null)
        {
            Debug.LogError("Block 没有绑定 ChartPlayer 或 ChartSO", this);
            enabled = false;
            return;
        }

        var chart = chartPlayer.chart;
        if (chart.clip == null)
        {
            Debug.LogError("ChartSO 上没有 AudioClip", this);
            enabled = false;
            return;
        }

        nodes = chart.nodes;
        nodeTimes = new float[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
            nodeTimes[i] = chart.bpmTimeline.BeatToSeconds(nodes[i].beat);

        audioSource = GetComponent<AudioSource>();
        audioSource.clip = chart.clip;
        audioSource.playOnAwake = false;

        // 关键：dspTime 和 PlayScheduled 是同一时钟源
        startTime = AudioSettings.dspTime + scheduleLead;
        audioSource.PlayScheduled(startTime);

        currentIndex = 0;
        running = true;
    }

    void Update()
    {
        if (!running || nodes.Count == 0) return;

        float now = (float)(AudioSettings.dspTime - startTime);

        // 音乐还没开始：停在第一个节点
        if (now < 0f)
        {
            transform.position = nodes[0].position;
            return;
        }

        if (nodes.Count < 2)
        {
            transform.position = nodes[0].position;
            return;
        }

        while (currentIndex < nodes.Count - 1 && now >= nodeTimes[currentIndex + 1])
            currentIndex++;

        if (currentIndex >= nodes.Count - 1)
        {
            transform.position = nodes[nodes.Count - 1].position;
            return;
        }

        NodeData a = nodes[currentIndex];
        NodeData b = nodes[currentIndex + 1];

        float span = nodeTimes[currentIndex + 1] - nodeTimes[currentIndex];
        float t = span <= 0f ? 1f : (now - nodeTimes[currentIndex]) / span;

        transform.position = Vector2.Lerp(a.position, b.position, Mathf.Clamp01(t));
    }
}