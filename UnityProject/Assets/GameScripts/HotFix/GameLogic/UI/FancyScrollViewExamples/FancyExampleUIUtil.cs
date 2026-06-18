using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// FancyScrollView 示例 UI 辅助工具。
    /// </summary>
    public static class FancyExampleUIUtil
    {
        /// <summary>
        /// 确保 <see cref="Dropdown"/> 拥有可用的 Template 子节点。
        /// <para>
        /// 部分示例 prefab 的 Dropdown 组件漏配 Template（m_Template 为空），运行时展开会报：
        /// "The dropdown template is not assigned. The template needs to be assigned and must have
        /// a child GameObject with a Toggle component serving as the item."。
        /// </para>
        /// <para>
        /// 本方法在 template 为空时，用 Unity 内置 <see cref="DefaultControls.CreateDropdown"/>
        /// 生成一个标准 Template 子树（ScrollRect/Viewport/Content/Item(Toggle)）并移植到目标 Dropdown，
        /// 作为 prefab 漏配的运行时兜底。已正确配置 Template 的 Dropdown 不受影响（直接返回）。
        /// </para>
        /// </summary>
        /// <remarks>
        /// 运行时补救：避免要求每个 prefab 都手工补 Template；治本仍可执行 Editor 菜单
        /// <c>TEngine/FancyScrollView/修复 Dropdown Template</c> 一劳永逸地修好源 prefab。
        /// </remarks>
        public static void EnsureDropdownTemplate(Dropdown dropdown)
        {
            if (dropdown == null)
            {
                return;
            }

            bool needTemplate = dropdown.template == null;
            bool needItemText = dropdown.itemText == null;
            if (!needTemplate && !needItemText)
            {
                return;
            }

            // 用内置工厂创建一个完整标准 Dropdown 作为配置捐赠者
            var donor = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            donor.SetActive(false); // 避免捐赠者在场景中闪现一帧
            try
            {
                var donorDropdown = donor.GetComponent<Dropdown>();
                Text donorItemLabel = donorDropdown != null ? donorDropdown.itemText : null;

                // 1) 移植 Template 子树（ScrollRect/Viewport/Content/Item(Toggle)）
                if (needTemplate && donorDropdown != null && donorDropdown.template != null)
                {
                    var template = donorDropdown.template;
                    template.SetParent(dropdown.transform, false);
                    dropdown.template = template;
                }

                // 2) 同步 itemText：AddOptions 靠它填充每个选项 Item Label 的文本，
                //    缺失时只改 GameObject 名字、Label 文本不变（用户所见现象）。
                if (needItemText)
                {
                    if (needTemplate && donorItemLabel != null)
                    {
                        // Template 从 donor 移植过来，donor 的 Item Label 已随之落在 dropdown 下
                        dropdown.itemText = donorItemLabel;
                    }
                    else if (dropdown.template != null)
                    {
                        // 目标自带 Template：在标准路径下找 Item Label
                        var label = dropdown.template.Find("Viewport/Content/Item/Item Label");
                        if (label != null)
                        {
                            dropdown.itemText = label.GetComponent<Text>();
                        }
                    }
                }

                Debug.Log($"[FancyExampleUIUtil] 已为 '{dropdown.gameObject.name}' 运行时补全 Dropdown 配置（template/itemText）。");
            }
            finally
            {
                // 销毁捐赠者其余节点（已 reparent 的 Template 子树不受影响）
                Object.Destroy(donor);
            }
        }
    }
}