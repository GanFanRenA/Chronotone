using System;
using UnityEngine;

public enum NodeMode
{
    JumpNode = 0,
    RespawnNode = 1,
    GravityNode = 2
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
