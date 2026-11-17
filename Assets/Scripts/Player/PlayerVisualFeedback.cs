using UnityEngine;

/// <summary>
/// 只驱动 Player/VisualRoot 的表现，不改变 Player 根节点或碰撞体。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerVisualFeedback : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Player player;
    [SerializeField] private SpriteRenderer targetRenderer;

    [Header("Input Acknowledgement")]
    [SerializeField, Min(0.01f)] private float inputPulseDuration = 0.075f;
    [SerializeField, Min(0f)] private float inputPopScale = 0.055f;
    [SerializeField, Min(0f)] private float inputLift = 0.018f;

    [Header("Perfect Lock")]
    [SerializeField, Min(0.01f)] private float exactPulseDuration = 0.11f;
    [SerializeField, Min(0f)] private float exactPopScale = 0.1f;
    [SerializeField, Range(0f, 0.01f)] private float exactWindowSeconds = 0.006f;

    [Header("Early / Late Reaction")]
    [SerializeField, Min(0.01f)] private float timingPulseDuration = 0.12f;
    [SerializeField, Min(0f)] private float timingNudgeDistance = 0.055f;
    [SerializeField, Min(0f)] private float timingTiltDegrees = 2f;
    [SerializeField, Min(0f)] private float timingSquash = 0.035f;

    [Header("Late Reattach")]
    [SerializeField, Min(0.01f)] private float lateCatchUpDuration = 0.075f;
    [SerializeField, Min(0f)] private float maxLateCatchUpDistance = 0.16f;

    private BasicJudgement judgement;
    private Transform physicalRoot;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 baseLocalScale;
    private float inputPulseElapsed = -1f;
    private float exactPulseElapsed = -1f;
    private float timingPulseElapsed = -1f;
    private int timingDirection;
    private float timingIntensity;
    private Vector3 lateStartLocalOffset;
    private Vector3 lateCaptureWorldPosition;
    private float lateCatchUpElapsed = -1f;
    private bool lateCatchUpPending;
    private bool subscribed;

    private void Awake()
    {
        baseLocalPosition = transform.localPosition;
        baseLocalRotation = transform.localRotation;
        baseLocalScale = transform.localScale;

        if (player == null)
            player = GetComponentInParent<Player>();
        physicalRoot = player != null ? player.transform : transform.parent;
        if (targetRenderer == null)
            targetRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        transform.localPosition = baseLocalPosition;
        transform.localRotation = baseLocalRotation;
        transform.localScale = baseLocalScale;
        lateCatchUpPending = false;
        lateCatchUpElapsed = -1f;
    }

    private void Subscribe()
    {
        if (subscribed || player == null)
            return;

        judgement = player.GetComponent<BasicJudgement>();
        if (judgement == null)
            return;

        judgement.InputPressed += TriggerInputAcknowledgment;
        judgement.JudgementResolved += HandleJudgement;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || judgement == null)
            return;

        judgement.InputPressed -= TriggerInputAcknowledgment;
        judgement.JudgementResolved -= HandleJudgement;
        subscribed = false;
    }

    public void TriggerInputAcknowledgment()
    {
        inputPulseElapsed = 0f;
    }

    private void HandleJudgement(JudgementResult result)
    {
        if (!result.HasNote ||
            result.Grade == JudgementGrade.Miss)
        {
            return;
        }

        float timeDelta = (float)result.TimeDelta;
        float absoluteDelta = Mathf.Abs(timeDelta);
        if (absoluteDelta <= exactWindowSeconds)
        {
            exactPulseElapsed = 0f;
            return;
        }

        timingDirection = timeDelta < 0f ? 1 : -1;
        timingIntensity = Mathf.InverseLerp(
            exactWindowSeconds,
            Mathf.Max(
                exactWindowSeconds + 0.0001f,
                player != null
                    ? player.GameSettings.GreatWindowSeconds
                    : 0.12f),
            absoluteDelta);
        timingPulseElapsed = 0f;
        if (result.IsSuccessfulHit && timeDelta > 0f)
        {
            lateCaptureWorldPosition = transform.position;
            lateCatchUpPending = true;
        }
    }

    private void LateUpdate()
    {
        Vector3 lateCatchUpOffset = ConsumeLateCatchUp(
            Time.unscaledDeltaTime);
        float inputWeight = ConsumePulse(
            ref inputPulseElapsed,
            inputPulseDuration);
        float exactWeight = ConsumePulse(
            ref exactPulseElapsed,
            exactPulseDuration);
        float timingWeight = ConsumePulse(
            ref timingPulseElapsed,
            timingPulseDuration);

        float scaleAmount =
            inputPopScale * inputWeight +
            exactPopScale * exactWeight;
        float timingScale = timingSquash *
            timingIntensity *
            timingWeight;
        transform.localScale = Vector3.Scale(
            baseLocalScale,
            new Vector3(
                1f + scaleAmount - timingDirection * timingScale,
                1f - scaleAmount * 0.45f + timingDirection * timingScale * 0.5f,
                1f));
        transform.localPosition =
            baseLocalPosition +
            Vector3.up * (inputLift * inputWeight) +
            Vector3.right * (
                timingDirection *
                timingNudgeDistance *
                timingIntensity *
                timingWeight) +
            lateCatchUpOffset;
        transform.localRotation =
            baseLocalRotation *
            Quaternion.Euler(
                0f,
                0f,
                -timingDirection *
                timingTiltDegrees *
                timingIntensity *
                timingWeight);
    }

    private Vector3 ConsumeLateCatchUp(float deltaTime)
    {
        if (lateCatchUpPending)
        {
            lateCatchUpPending = false;
            lateCatchUpElapsed = 0f;
            Vector3 targetWorldPosition = physicalRoot != null
                ? physicalRoot.TransformPoint(baseLocalPosition)
                : baseLocalPosition;
            Vector3 worldOffset =
                lateCaptureWorldPosition - targetWorldPosition;
            if (worldOffset.magnitude > maxLateCatchUpDistance)
            {
                worldOffset = worldOffset.normalized *
                    maxLateCatchUpDistance;
            }

            Transform parent = transform.parent;
            lateStartLocalOffset = parent != null
                ? parent.InverseTransformVector(worldOffset)
                : worldOffset;
        }

        if (lateCatchUpElapsed < 0f)
            return Vector3.zero;

        lateCatchUpElapsed += Mathf.Max(0f, deltaTime);
        float progress = Mathf.Clamp01(
            lateCatchUpElapsed /
            Mathf.Max(0.01f, lateCatchUpDuration));
        if (progress >= 1f)
        {
            lateCatchUpElapsed = -1f;
            return Vector3.zero;
        }

        float smooth = progress * progress * (3f - 2f * progress);
        return lateStartLocalOffset * (1f - smooth);
    }

    private static float ConsumePulse(
        ref float elapsed,
        float duration)
    {
        if (elapsed < 0f)
            return 0f;

        elapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        if (progress >= 1f)
        {
            elapsed = -1f;
            return 0f;
        }

        float smooth = progress * progress * (3f - 2f * progress);
        return 1f - smooth;
    }
}
