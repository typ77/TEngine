using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TEngine.Editor.UI
{
    /// <summary>
    /// 批量修复 <see cref="Dropdown"/> 组件缺失的 Template 子节点。
    /// <para>
    /// Unity 的 Dropdown 需要 Template 子节点（含 ScrollRect/Viewport/Content/Item(Toggle)）才能展开下拉列表；
    /// Template 为空时运行时每帧报错："The dropdown template is not assigned. The template needs to be
    /// assigned and must have a child GameObject with a Toggle component serving as the item."。
    /// </para>
    /// <para>
    /// 本工具扫描指定目录下的 prefab，对 template 为空的 Dropdown 用 Unity 内置
    /// <see cref="DefaultControls.CreateDropdown"/> 生成一个标准 Template 子树并移植过去，
    /// 避免手写 prefab YAML（Template 子树含 9+ 个 GameObject，极易出错）。
    /// </para>
    /// <para>
    /// 触发问题场景：Example07_ScrollRectUI / Example08_GridViewUI 的 m_dropdown_Alignment 节点
    /// Dropdown 组件 template 字段为 {fileID: 0}。
    /// </para>
    /// </summary>
    public static class DropdownTemplateFixer
    {
        /// <summary>默认扫描根目录（FancyScrollView 示例 prefab 集中存放处）。</summary>
        private const string ScanRoot = "Assets/AssetRaw/UI/FancyScrollViewExamples";

        [MenuItem("TEngine/FancyScrollView/修复 Dropdown Template（扫描示例 prefab）")]
        public static void FixAllInExamples()
        {
            if (!AssetDatabase.IsValidFolder(ScanRoot))
            {
                Debug.LogWarning($"[DropdownTemplateFixer] 目录不存在: {ScanRoot}");
                return;
            }

            int scanned = 0;
            int fixedCount = 0;
            var prefabPaths = Directory.GetFiles(ScanRoot, "*.prefab", SearchOption.AllDirectories);
            foreach (var path in prefabPaths)
            {
                scanned++;
                // LoadPrefabContents 返回隔离的 prefab 实例，修改后 SaveAsPrefabAsset 再 Unload
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var dropdowns = contents.GetComponentsInChildren<Dropdown>(true);
                    if (dropdowns.Length == 0)
                    {
                        continue;
                    }

                    bool changed = false;
                    foreach (var dd in dropdowns)
                    {
                        if (dd.template != null && dd.itemText != null)
                        {
                            continue;
                        }

                        AttachStandardTemplate(dd);
                        changed = true;
                        fixedCount++;
                        Debug.Log($"[DropdownTemplateFixer] 已补 Template/ItemText: '{dd.gameObject.name}' @ {path}");
                    }

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[DropdownTemplateFixer] 完成：扫描 {scanned} 个 prefab，修复 {fixedCount} 个 Dropdown Template。");
        }

        /// <summary>
        /// 用 Unity 内置 <see cref="DefaultControls.CreateDropdown"/> 生成一个完整标准 Dropdown，
        /// 将其 Template 子节点移植到 <paramref name="target"/> 下，再销毁捐赠者其余节点。
        /// <para>Template 已 reparent，销毁捐赠者 GO 不影响被移植的 Template 子树。</para>
        /// </summary>
        /// <summary>
        /// 用 Unity 内置 <see cref="DefaultControls.CreateDropdown"/> 生成一个完整标准 Dropdown，
        /// 将缺失的 Template 子节点和/或 itemText 引用从捐赠者移植到 <paramref name="target"/>，再销毁捐赠者。
        /// <para>itemText 必须同步：<see cref="Dropdown.AddOptions"/> 靠它填充每个选项 Label 文本，
        /// 缺失时只会改 Item 的 GameObject 名字、Label 文本不变。</para>
        /// </summary>
        private static void AttachStandardTemplate(Dropdown target)
        {
            bool needTemplate = target.template == null;
            bool needItemText = target.itemText == null;
            if (!needTemplate && !needItemText)
            {
                return;
            }

            var donor = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            try
            {
                var donorDropdown = donor.GetComponent<Dropdown>();
                Text donorItemLabel = donorDropdown != null ? donorDropdown.itemText : null;

                // 1) 移植 Template 子节点（仅当目标缺失时）
                if (needTemplate && donorDropdown != null && donorDropdown.template != null)
                {
                    var template = donorDropdown.template;
                    template.SetParent(target.transform, false);
                    target.template = template;
                }

                // 2) 同步 itemText
                if (needItemText)
                {
                    if (needTemplate && donorItemLabel != null)
                    {
                        // Template 从 donor 移植过来，donor 的 Item Label 已随之落在 target 下
                        target.itemText = donorItemLabel;
                    }
                    else if (target.template != null)
                    {
                        // 目标自带 Template：在标准路径下找 Item Label，避免引用即将被销毁的 donor
                        var label = target.template.Find("Viewport/Content/Item/Item Label");
                        if (label != null)
                        {
                            target.itemText = label.GetComponent<Text>();
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(donor);
            }
        }
    }
}