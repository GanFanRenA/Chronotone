using System;
using UnityEngine;

public enum NodeMode
{
    JumpNode = 0,
    RespawnNode = 1,
    GravityNode = 2,
    FastFallNode = 3,
    GravityFastFallNode = 4,
    WeakGravityNode = 5,
    GravityJumpNode = 6
}

[Serializable]
public class NodeData
{
    public float beat;
    public int order;
    public NodeMode mode = NodeMode.JumpNode;
    public Vector2 position;
    [HideInInspector] public float deltaX;
}
