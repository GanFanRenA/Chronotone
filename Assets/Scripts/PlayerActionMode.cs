using UnityEngine;

/// <summary>
/// 玩家行动模式的基类。模式本身不实现 Update/FixedUpdate，
/// 由 Player 统一驱动，确保同一时刻只有当前模式接收更新。
/// </summary>
public abstract class PlayerActionMode : MonoBehaviour
{
    public Player Player { get; private set; }
    public bool IsActive { get; private set; }
    public virtual string DisplayName => GetType().Name;

    internal void Bind(Player player)
    {
        if (Player != null && Player != player)
        {
            Debug.LogError("行动模式不能绑定到多个 Player", this);
            return;
        }

        Player = player;
    }

    internal void Enter()
    {
        if (IsActive) return;

        IsActive = true;
        OnModeEnter();
    }

    internal void Exit()
    {
        if (!IsActive) return;

        try
        {
            OnModeExit();
        }
        finally
        {
            IsActive = false;
        }
    }

    internal void Tick(double songTime)
    {
        if (IsActive) OnModeUpdate(songTime);
    }

    internal void FixedTick(double songTime, float fixedDeltaTime)
    {
        if (IsActive) OnModeFixedUpdate(songTime, fixedDeltaTime);
    }

    internal void Judge(JudgementResult result)
    {
        if (IsActive) OnModeJudgement(result);
    }

    internal void RequestJump()
    {
        if (IsActive) OnModeRequestJump();
    }

    internal void RespawnTo(
        Vector2 position,
        double checkpointSongTime,
        int checkpointNodeIndex)
    {
        if (IsActive)
            OnModeRespawn(position, checkpointSongTime, checkpointNodeIndex);
    }

    internal void SetControlLocked(bool locked)
    {
        if (IsActive)
            OnModeControlLockChanged(locked);
    }

    internal void SetRewindPose(Vector2 position, Quaternion rotation)
    {
        if (IsActive)
            OnModeRewindPose(position, rotation);
    }

    protected abstract void OnModeEnter();
    protected virtual void OnModeUpdate(double songTime) { }
    protected virtual void OnModeFixedUpdate(double songTime, float fixedDeltaTime) { }
    protected virtual void OnModeJudgement(JudgementResult result) { }
    protected virtual void OnModeRequestJump() { }
    protected virtual void OnModeRespawn(
        Vector2 position,
        double checkpointSongTime,
        int checkpointNodeIndex) { }
    protected virtual void OnModeControlLockChanged(bool locked) { }
    protected virtual void OnModeRewindPose(
        Vector2 position,
        Quaternion rotation)
    {
        Vector3 worldPosition = new Vector3(
            position.x,
            position.y,
            Player.transform.position.z);
        Player.transform.SetPositionAndRotation(worldPosition, rotation);
    }
    protected virtual void OnModeExit() { }
}
