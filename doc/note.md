# TouchChanX.WinUI

### MenuButton

1. 用 UserControl 实现，继承 Button 实现没有意义，因为需要主动放弃指针 capture，最终离不开在 code-behind 中实现状态变化代码。
2. `Background="Transparent"` 在 UserControl 最顶层或 StackPanel 上使用都不起效，必须包一层 Grid 或 Border 作为背景层，纯背景层一般直接无脑考虑 Grid + Transparent 的组合就好。
3. VisualStateManager 是给实际承载视觉状态的元素用的，所以通常在 Content 内部挂载，而不是 UserControl 根部，否则会无效。

# TouchChanX.UWP

* 使用了 MediumIL 无沙盒自定义权限（本质islands）后的 UWP，所有 FullTrustProcessLauncher 类中的方法无法工作。若使用 Process.Start 也需要封装一层由外部 FullTrust 程序启动才靠谱。