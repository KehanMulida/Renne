using UnityEngine;

/// <summary>
/// 角色视觉：把占位胶囊换成真实角色模型（如 BeiYe），并让模型正确贴地，避免穿模。
///
/// 背景：网格系统把角色【根物体】放在"楼层地面 Y"（轴心贴地）。Unity 原始胶囊轴心在中央，
/// 贴地会下沉半个身位穿模；而角色模型轴心通常在脚底，作为子物体放在根上即正好站立。
///
/// 用法：挂在角色根物体（敌人/玩家）上 →
///   - 隐藏根上的占位胶囊 MeshRenderer（碰撞体保留，供命中/视线检测）
///   - 实例化 modelPrefab（BeiYe.fbx）作为子物体，或使用已有的 existingModel
///   - 脚底对齐（footOffset 微调；uneven 地面可开 snapToGround 射线吸附）
///   - 缓存 Animator，供后续程序化动画脚本使用（CharacterVisual.Animator）
/// </summary>
[DisallowMultipleComponent]
public class CharacterVisual : MonoBehaviour
{
    [Header("模型")]
    [Tooltip("要显示的角色模型（把 BeiYe.fbx 拖进来）；留空则使用下方 existingModel")]
    public GameObject modelPrefab;
    [Tooltip("已作为子物体存在的模型（不想用 Prefab 实例化时指定）")]
    public Transform existingModel;
    [Tooltip("模型朝向偏移（度，绕 Y）。模型正面与角色 forward 不一致时用")]
    public float modelYawOffset = 0f;

    [Header("隐藏占位胶囊")]
    [Tooltip("启动时隐藏根物体上的占位胶囊 MeshRenderer（碰撞体不动）")]
    public bool hidePlaceholderRenderer = true;

    [Header("贴地")]
    [Tooltip("脚底相对根物体(地面Y)的偏移。模型轴心在脚→填0；轴心在中央→填 +半身高")]
    public float footOffset = 0f;
    [Tooltip("向下射线把模型吸附到真实地面（修不平地面/道具上的穿模）；平坦楼层可关")]
    public bool snapToGround = false;
    [Tooltip("射线检测的地面层（勿含角色自身层）")]
    public LayerMask groundMask = ~0;
    [Tooltip("从根物体上方多高开始向下射线")]
    public float rayStartHeight = 2f;

    /// <summary>模型上的 Animator（供程序化动画脚本使用）</summary>
    public Animator Animator { get; private set; }
    /// <summary>当前模型根 Transform</summary>
    public Transform Model => _model;

    private Transform _model;

    void Start()
    {
        // 1. 隐藏占位胶囊的渲染（保留碰撞体：命中判定/视线遮挡仍需要）
        if (hidePlaceholderRenderer)
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
        }

        // 2. 取得或实例化模型
        if (existingModel != null)
        {
            _model = existingModel;
        }
        else if (modelPrefab != null)
        {
            GameObject go = Instantiate(modelPrefab, transform);
            go.name = modelPrefab.name;
            _model = go.transform;
            _model.localPosition = Vector3.zero;
            _model.localRotation = Quaternion.Euler(0f, modelYawOffset, 0f);
        }
        else
        {
            Debug.LogWarning($"[CharacterVisual] {name} 没有指定 modelPrefab / existingModel");
            return;
        }

        Animator = _model.GetComponentInChildren<Animator>();

        GroundModel();
    }

    void LateUpdate()
    {
        // 平坦楼层无需每帧；仅在开启射线吸附时逐帧对齐（不平地面/移动中）
        if (snapToGround) GroundModel();
    }

    /// <summary>把模型脚底对齐到地面（供外部在移动后手动调用）。</summary>
    public void GroundModel()
    {
        if (_model == null) return;

        float localY = footOffset;

        if (snapToGround)
        {
            Vector3 origin = transform.position + Vector3.up * rayStartHeight;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                rayStartHeight * 2f + 5f, groundMask, QueryTriggerInteraction.Ignore))
            {
                // 命中地面：把模型脚底放到命中点（子物体用世界Y换算局部Y）
                localY = (hit.point.y + footOffset) - transform.position.y;
            }
        }

        Vector3 lp = _model.localPosition;
        lp.y = localY;
        _model.localPosition = lp;
    }
}
