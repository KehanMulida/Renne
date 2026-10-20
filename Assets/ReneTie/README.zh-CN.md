# Rene 领带：使用说明

修改源文件：E:/Rene/Rene_c1_5.1_TieUnity.blend
原文件没有覆盖。当前会话的未保存工作已另存到 E:/Rene/Tie_Work/Rene_current_before_tie.blend。

## 在当前 Unity 工程中使用

将 Assets/ReneTie/Rene_Player_TiePhysics.prefab 拖入场景。
Prefab 已绑定 ReneTieDynamics、五段领带骨骼及端点，并使用当前工程的 URP/Lit 材质和原始贴图。
运行后，移动/转动角色，领带会产生惯性摆动。默认原地静止时不会一直自动摇摆。
不依赖第三方插件，不需要给 Blender 添加不可移植的布料缓存。
本次没有替换已有 Player Prefab、场景或 Animator Controller。

## 动画与导出

FBX 只导出了当前 Player 及其骨架，保留领带蒙皮；没有导出整个场景或烘焙全部角色动作。
Prefab 默认 Generic Rig。若要复用现有 Humanoid 动画，在 FBX Rig 页设置 Humanoid、检查 Avatar 映射，再给 Prefab 的 Animator 设置对应 Controller。
不要启用 Optimize Game Objects，除非已明确暴露 tie_01 至 tie_05、tie_end 和 spine_03.x；脚本需要这些 Transform。
重新导出时使用普通 FBX，Selected Objects、Mesh + Armature、Only Deform Bones、关闭 Add Leaf Bones。
使用 Auto-Rig Pro 专用导出器或重新生成 Rig 时，务必检查新领带骨骼仍存在。此次实际验证的是交付的普通 FBX。

## 物理参数

ReneTieDynamics 在 LateUpdate 中更新，跟随 Animator 之后的胸骨姿态。
Stiffness：回弹强度。Damping：衰减阻尼。Gravity Scale：重力影响。Max Swing：摆角限制。
默认有胸部局部坐标中的防穿透包络；Body Colliders 可额外指定手臂或躯干的 CapsuleCollider/SphereCollider。
这是一条五段骨骼的轻量近似，不是完整布料、自碰撞或任意动作下零穿模保证。极端趴伏、翻滚和手臂贴胸的动作需要结合实际动画调参及补充碰撞体。
角色请保持均匀缩放；改变运行时比例后调用 ResetSimulation。瞬移会自动重置。

## Blender 修改

仅修改 Player 的领带下摆 86 个顶点的权重；其余 7,867 个顶点权重已对照原始会话验证不变。
新增 tie_01 -> tie_02 -> tie_03 -> tie_04 -> tie_05 -> tie_end，挂在 spine_03.x 下。
领带根部固定，沿长度做平滑的双骨骼渐变，身体及领带原有 UV/材质保留。
Blender 文件内没有实时 Unity 物理脚本；可在 Pose Mode 旋转 tie 骨骼检查变形。

## 验证与已知外观限制

使用 Unity 2022.3.50f1 独立项目实际导入交付 FBX、编译脚本并生成 Prefab。
30/60/120/144 FPS 数值仿真检查通过，无 NaN；骨段长度最大误差约 0.000000425 米。
单独弯曲领带骨骼的蒙皮检查：52 个导入顶点移动，身体无关顶点移动数为 0。瞬移重置检查通过。
验证记录：E:/Rene/Tie_Work/unity_validation.txt。
当前工程采用 URP，因此交付到 Assets/ReneTie 的材质已换为工程现有 URP/Lit；没有在完整游戏工程中运行现有角色动画。
原始衬衫贴图烘焙了领带形状的暗痕，领带摆开时会露出；没有将有接缝的 UV 修补实验用于交付。彻底消除需要另外重绘衬衫贴图。

参考：
https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MonoBehaviour.LateUpdate.html
https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ModelImporter-optimizeGameObjects.html
