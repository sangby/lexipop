# LexiPop

Windows 11 桌面词典。目标：常驻内存小、唤出快、全局热键、划词翻译、准确翻译 + 发音。

技术方案与决策记录见 [zxt/词典软件实施方案.md](zxt/词典软件实施方案.md)。

## 当前进度

**M0 骨架（已完成代码，待编译验证）**

- [x] 单实例运行（第二次启动唤出已有窗口）
- [x] 系统托盘图标 + 右键菜单（查词 / 开机自启 / 退出）
- [x] 全局热键：`Ctrl+Alt+D` 唤出，`Ctrl+Alt+S` 划词翻译
- [x] 无边框唤出窗口（预创建，`Show()` 即用）
- [x] 取词第一级：UI Automation 读取焦点控件选区
- [x] 开机自启（HKCU Run 键）
- [ ] M1：牛津 MDX → SQLite、查询管线、结果渲染

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

脚本会编译 Release 并自动用自签名证书给 exe 签名。

**首次在新机器/新 Windows 用户上**，先运行一次证书初始化：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\setup-signing-cert.ps1
```

产物：`src\LexiPop\bin\Release\net10.0-windows\LexiPop.exe`

## 代码签名（避免 SmartScreen 拦截）

exe 用自签名证书 `CN=LexiPop Dev` 签名（5 年有效），证书已导入当前用户的
"受信任的根证书颁发机构"。这样每次编译出的新 exe 不会被 SmartScreen 当作
"未知发布者"拦截。

- 私钥：`Cert:\CurrentUser\My`
- 公钥：`lexipop-dev.cer`
- 每次重新编译后 exe 哈希变化，**必须重新签名**——`build.ps1` 已自动处理

> 换系统或换用户后需要重跑 `tools\setup-signing-cert.ps1`。
> 如果首次双击仍弹一次确认，点"仍要运行"即可，之后不再出现。

## 目录结构

```
src/LexiPop/
├─ App.xaml(.cs)              应用入口：单实例、托盘、热键装配
├─ MainWindow.xaml(.cs)       唤出窗口
├─ Interop/NativeMethods.cs   Win32 P/Invoke 集中处
└─ Services/
   ├─ MessageWindow.cs        不可见消息窗口（热键/托盘/广播）
   ├─ TrayService.cs          Shell_NotifyIcon 托盘
   ├─ SingleInstance.cs       Mutex + 广播唤出
   ├─ SelectionCapture.cs     UIA 取词（第一级）
   └─ AutoStart.cs            开机自启
```

## 已知限制（M0）

- 取词只实现 UIA 一级，剪贴板兜底与浮窗按钮在 M2。
- 托盘图标用系统默认图标占位，后续换项目图标。
- 查询结果是占位文本，真实管线在 M1。
