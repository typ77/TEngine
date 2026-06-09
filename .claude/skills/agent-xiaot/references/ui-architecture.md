# 通用 UI 架构设计参考

> 小T的设计笔记 — 让 UI 开发像搭积木一样简单

## 一、现状分析

TEngine 已有 UIModule/UIWindow/UIWidget 的基础框架和代码生成器。需要补齐：
1. ✅ 已有：窗口管理、生命周期、UIWindow/UIWidget 分层
2. ❌ 缺失：通用弹窗模板（Toast/Loading/Confirm/Alert）
3. ❌ 缺失：UI 动效系统
4. ❌ 缺失：页面导航/路由管理
5. ⚠️ 半成品：SafeArea 适配
6. ⚠️ 半成品：UI 代码生成器增强

## 二、UI 架构总览

```
UIModule（框架核心）
├── UIRoot（场景中唯一的UI根节点）
│   ├── Canvas 层级0（背景层）
│   ├── Canvas 层级1（普通页面层）  ← UIWindow
│   ├── Canvas 层级2（弹窗层）      ← Toast/Confirm/Alert
│   ├── Canvas 层级3（Loading层）
│   └── Canvas 层级4（Debug/Overlay层）
│
├── UIWindow 管理
│   ├── 生命周期：Create → Prepare → Show → Hide → Close
│   ├── 堆栈管理：Push / Pop / Top / Back
│   └── 传参机制：通过 IUIWindowParam 接口
│
├── UIWidget 管理（内嵌可复用组件）
│   ├── 模板化：预定义 Widget 模板
│   └── 数据绑定：Widget.DataContext 驱动刷新
│
├── 通用弹窗系统
│   ├── Toast：轻提示，自动消失
│   ├── Loading：模态/非模态
│   ├── Confirm：确定/取消
│   ├── Alert：信息提示
│   └── Input：输入弹窗
│
├── 动效系统
│   ├── 进出场动画（FadeIn/FadeOut/SlideIn/SlideOut/Scale）
│   ├── UIWindow 生命周期自动绑定
│   └── 链式动画 API
│
└── UI 生成器
    ├── Prefab 绑定代码自动生成
    ├── 支持 Button/Text/Image/Slider/Toggle/InputField/Dropdown...
    └── 自定义组件绑定规则
```

## 三、通用弹窗 API 设计

```csharp
// Toast — 轻量提示，自动销毁
GameModule.UI.ShowToast("保存成功");                         // 默认底部
GameModule.UI.ShowToast("网络异常", 3f, ToastPosition.Center); // 自定义时长+位置

// Loading — 等待动画，支持超时
var loadingHandle = GameModule.UI.ShowLoading("正在加载...", cancelable: true);
await SomeTask();
loadingHandle.Close(); // 主动关闭

// Confirm — 确认弹窗
var result = await GameModule.UI.ShowConfirmAsync("提示", "确定删除吗？");
if (result == ConfirmResult.Ok) { /* 执行删除 */ }

// Alert — 提示弹窗
GameModule.UI.ShowAlert("提示", "操作完成");

// Input — 输入弹窗
var input = await GameModule.UI.ShowInputAsync("改名", "请输入新名字", "默认名");
```

## 四、UI 动效系统设计

```csharp
// 声明式（通过 Attribute）
[UIAnimation(
    Enter = UIAnimType.FadeIn, 
    EnterDuration = 0.3f,
    Exit = UIAnimType.SlideOutRight,
    ExitDuration = 0.2f
)]
public class BattleMainUI : UIWindow { }

// 链式（动态追加）
window.PlaySequence()
    .FadeIn(0.3f, EaseType.QuadOut)
    .ScaleFrom(0.8f, EaseType.BackOut)
    .Delay(0.2f)
    .Callback(() => Debug.Log("入场动画结束"))
    .Play();

// 自定义动效
public class CustomEnterAnimation : IUIAnimation
{
    public async UniTask Play(UIWindow window, CancellationToken ct)
    {
        var rt = window.GetComponent<RectTransform>();
        rt.localScale = Vector3.zero;
        await rt.DOScale(1f, 0.5f).WithCancellation(ct);
    }
}
```

## 五、UI 导航/路由设计

```csharp
// 打开页面
var loginUI = await GameModule.UI.ShowUIAsync<LoginUI>(new { from = "splash" });

// 返回上一页
await GameModule.UI.GoBackAsync();

// 带参数返回
await GameModule.UI.GoBackAsync(new { result = "ok" });

// 关闭到指定页面
await GameModule.UI.PopToAsync<MainUI>();
```