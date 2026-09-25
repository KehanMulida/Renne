using UnityEngine;
using FischlWorks_FogWar;

/// <summary>
/// 按战争迷雾可见性隐藏物体——把「玩家不该看到的东西」从**渲染层**下沉到**可见性层**。
///
/// 为什么需要它：
///   场景里的 csFogWar 是一张 `fogPlaneHeight = 0` 的**雾平面**，它只盖住**地面**。
///   箱子、家具、敌人这些**立起来**的物体会从雾平面上方露出来，照样看得见——
///   所以"隔壁房间的东西不该被看到"**不能指望雾平面**，得让物体自己隐藏。
///
///   这也是比"把墙弄透明/不透明"更可靠的一层：等距相机本来就能越过矮墙看到隔壁，
///   靠墙挡视线是不牢靠的。
///
/// 用法：挂在**不希望被隔墙看到**的物体上——敌人、可拾取物、别的房间的道具/家具。
///   ⚠ 不要挂在墙和地板上，否则关卡结构会整块消失。
///
/// 只切 Renderer.enabled，不动碰撞体和逻辑：敌人依然存在、依然能被听到/被打到，只是看不见。
/// </summary>
[DisallowMultipleComponent]
public class FogHideable : MonoBehaviour
{
    [Tooltip("留空则自动查找场景里的 csFogWar")]
    [SerializeField] private csFogWar fogWar;

    [Tooltip("额外可见半径（格）。0 = 只看自己所在格；\n物体压在格边上来回闪烁时把它调到 1")]
    [SerializeField] private int extraRadius = 0;

    [Tooltip("检查间隔（秒）。雾本身 FogRefreshRate 有限，没必要每帧查")]
    [SerializeField] private float checkInterval = 0.1f;

    [Tooltip("物体在雾格范围之外（地图边界外）时是否显示")]
    [SerializeField] private bool visibleWhenOutOfGrid = true;

    private Renderer[] _renderers;
    private float _nextCheck;
    private bool  _visible = true;
    private bool  _searched;

    void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    void Update()
    {
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + Mathf.Max(0.02f, checkInterval);

        if (fogWar == null && !_searched)
        {
            fogWar = FindObjectOfType<csFogWar>();
            _searched = true;
        }
        if (fogWar == null) return;

        bool visible;
        if (!fogWar.CheckWorldGridRange(transform.position))
        {
            // 必须先兜这一层：CheckVisibility 在 additionalRadius==0 时**不做越界检查**，
            // 传地图外的坐标会直接抛数组越界。
            visible = visibleWhenOutOfGrid;
        }
        else
        {
            visible = fogWar.CheckVisibility(transform.position, extraRadius);
        }

        if (visible == _visible) return;
        _visible = visible;

        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].enabled = visible;
    }
}
