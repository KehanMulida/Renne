using UnityEngine;

/// <summary>
/// 当指定布尔字段为 false 时，在 Inspector 中完全隐藏此字段（高度归零，不占空间）
///
/// 用法：
///   public bool isToggleable = false;
///   [ConditionalHide("isToggleable")]
///   public ToggleConfig toggleConfig = new ToggleConfig();
///
/// 支持多条件（全部为 true 才显示）：
///   [ConditionalHide("isLockable", "isToggleable")]
///   public LockConfig lockConfig;
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
public class ConditionalHideAttribute : PropertyAttribute
{
    /// <summary>条件布尔字段名列表（同一对象内的字段，全部为 true 才显示）</summary>
    public readonly string[] conditionFields;

    public ConditionalHideAttribute(params string[] conditionFields)
    {
        this.conditionFields = conditionFields;
    }
}

// ── Editor-only PropertyDrawer ────────────────────────────────────────
#if UNITY_EDITOR
namespace ConditionalHideEditor
{
    [UnityEditor.CustomPropertyDrawer(typeof(ConditionalHideAttribute))]
    internal class ConditionalHidePropertyDrawer : UnityEditor.PropertyDrawer
    {
        public override void OnGUI(
            UnityEngine.Rect position,
            UnityEditor.SerializedProperty property,
            UnityEngine.GUIContent label)
        {
            if (AllConditionsMet(property))
                UnityEditor.EditorGUI.PropertyField(position, property, label, includeChildren: true);
        }

        public override float GetPropertyHeight(
            UnityEditor.SerializedProperty property,
            UnityEngine.GUIContent label)
        {
            if (AllConditionsMet(property))
                return UnityEditor.EditorGUI.GetPropertyHeight(property, label, includeChildren: true);

            // 高度设为负标准间距 → Unity 完全消除此属性占用的空间
            return -UnityEditor.EditorGUIUtility.standardVerticalSpacing;
        }

        private bool AllConditionsMet(UnityEditor.SerializedProperty property)
        {
            var attr = (ConditionalHideAttribute)attribute;
            foreach (var fieldName in attr.conditionFields)
            {
                var condProp = property.serializedObject.FindProperty(fieldName);
                // 找不到字段 or 字段不是 bool → 默认显示
                if (condProp == null || condProp.propertyType != UnityEditor.SerializedPropertyType.Boolean)
                    continue;
                if (!condProp.boolValue) return false;
            }
            return true;
        }
    }
}
#endif
