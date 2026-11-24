# LexiPop

Windows 划词翻译工具。后台常驻托盘，拖选文字后显示「译」按钮，点击后在浮窗中查词或翻译；也可通过全局热键直接展开浮窗。没有独立的主查询窗口。

技术方案与决策记录见 [zxt/词典软件实施方案.md](zxt/词典软件实施方案.md)。

## 使用方式

- 拖选文字后，点击鼠标旁的「译」按钮；或按 `Ctrl+Alt+S`，直接在鼠标旁打开结果卡片。
- 单词先查本地 `dict.bin` 词库。词库未命中的文本可由配置的模型提供方翻译。
- 点击托盘图标或右键选择「设置」来调整划词、长句翻译、模型与主题；再次启动程序也会打开设置。
- 可选 DeepSeek、OpenAI、OpenRouter 或自定义 OpenAI 兼容服务。选定提供方会填入端点；「抓取模型」从其 `/models` 接口取得模型 ID。模型列表可能包含不支持文本聊天的模型，需自行选择兼容 Chat Completions 的模型。

## CSS 自定义主题

设置页只保留「浅色」「玻璃」两个内置主题。点击「新建主题」会从当前主题生成 CSS 模板；编辑 `:root` 内的变量并点击「保存主题」，主题文件会保存在 `%LOCALAPPDATA%\LexiPop\themes\custom-*.css`。选中自定义主题后可再次编辑，也可用文本编辑器直接修改该文件。

WPF 不直接使用浏览器 CSS。程序读取模板中的 `--name`、`--is-dark`、`--is-glass`、各颜色变量和三个 `--radius-...` 变量，再映射到 WPF 资源。颜色格式为 `#RRGGBB` 或 `#AARRGGBB`，圆角范围为 `0px` 到 `32px`；保存时会校验所需变量。

## 构建

```powershell
pwsh.exe -NoProfile -File ./build.ps1
```

需要 .NET 10 SDK。脚本会编译 Release；当前用户已初始化签名证书时，还会对 exe 签名。

**首次在新机器/新 Windows 用户上**，先运行一次证书初始化：

```powershell
pwsh.exe -NoProfile -File ./tools/setup-signing-cert.ps1
```

产物：`src\LexiPop\bin\Release\net10.0-windows\LexiPop.exe`

## 代码签名

`setup-signing-cert.ps1` 会生成当前用户的自签名开发证书 `CN=LexiPop Dev`，并导入当前用户的受信任根存储。证书只供本机开发签名使用，不代表公共可信发布者。

- 私钥：`Cert:\CurrentUser\My`
- 公钥：`lexipop-dev.cer`
- 每次重新编译后 exe 哈希变化，**必须重新签名**——`build.ps1` 已自动处理

> 换系统或换用户后需要重跑 `tools\setup-signing-cert.ps1`。
> 如果首次双击仍弹一次确认，点"仍要运行"即可，之后不再出现。

## 目录结构

```
src/LexiPop/
├─ App.xaml(.cs)              应用入口：单实例、托盘、热键装配
├─ Views/SelectionPopup*      划词按钮与结果卡片
├─ Views/SettingsWindow*      设置、模型抓取与 CSS 主题编辑
├─ Interop/NativeMethods.cs   Win32 P/Invoke 集中处
└─ Services/
   ├─ MessageWindow.cs        不可见消息窗口（热键/托盘/广播）
   ├─ TrayService.cs          Shell_NotifyIcon 托盘
   ├─ SingleInstance.cs       Mutex + 广播打开设置
   ├─ SelectionCapture.cs     UI Automation 取词
   ├─ ClipboardCapture.cs     剪贴板取词兜底
   ├─ LookupService.cs        本地词典与在线翻译路由
   ├─ ModelProviders.cs       提供方端点与模型列表抓取
   └─ CustomThemeService.cs   CSS 自定义主题
```

## 本地数据与仓库

`data/`、`dist/`、`.dotnet/` 与 `.nuget/` 不提交到 Git。克隆仓库后需准备 .NET 10 SDK；要使用离线查词，还需将 `dict.bin` 放到 `data/` 后构建。`tools/build-dict-sqlite.py` 可从本地 `data/stardict.db` 生成词库。API Key 和缓存保存在 `%LOCALAPPDATA%\LexiPop`，不进入仓库。
