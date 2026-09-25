using UnityEngine;

/// <summary>
/// 标记：挂上这个组件的物体（含其所有子物体）**永远不会被相机遮挡系统弄透明**。
///
/// 用途：有些墙的存在意义就是"挡住玩家视线、不让玩家看到那一侧"——
/// 这类墙一旦变透明就会泄漏隔壁房间的信息，所以直接禁止它淡出。
///
/// 重要：被标记的墙**仍然会被计入"遮挡程度"**，于是
/// <see cref="IsometricCameraController"/> 的相机让位照常触发——
/// 也就是说：**墙不让路，就让相机让路**。两者是配套的。
///
/// 用法：挂在墙（或整片墙体的父物体）上即可，无需任何配置。
/// </summary>
[DisallowMultipleComponent]
public class NoOcclusionFade : MonoBehaviour
{
}
