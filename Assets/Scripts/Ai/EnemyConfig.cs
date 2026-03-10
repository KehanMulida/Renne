using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "EnemyConfig", menuName = "AI/Enemy Config")]
public class EnemyConfig : ScriptableObject
{
    
    [Header("感知参数")]
    public float visionRange = 15f;
    public float visionAngle = 90f;
    public float hearingRange = 10f;
    
    [Header("移动参数")]

    public float moveSpeed = 3f;
    public int moveRange = 5;
    public float chaseSpeed = 5f;
    public float rotationSpeed = 5f;
    
    [Header("战斗参数")]
    public float attackRange = 2f;
    public float attackCooldown = 1f;
    public int attackDamage = 10;
    
    [Header("生命值")]
    public int maxHp = 100;
    public int maxSanity = 100; 
    
    [Header("行为参数")]
    public float investigationDuration = 5f;
    
    [Header("行为树")]
    public TextAsset behaviorTreeAsset;
}