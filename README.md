# LexiPop

LexiPop 是一款 Windows 划词翻译工具。它常驻系统托盘：选中文字后，点击鼠标旁的「译」按钮即可查看结果；也可以用全局快捷键打开或恢复结果小窗。程序没有独立的主查询窗口。

## 下载与运行

在 [Releases](https://github.com/sangby/lexipop/releases) 下载最新的 `LexiPop-*-win-x64.zip`，解压到可写的普通文件夹，再从 Windows 文件资源管理器双击 `LexiPop.exe`。发布包为 Windows x64 自包含版本，无需另外安装 .NET Desktop Runtime。启动后请在系统托盘中查找 LexiPop 图标；单击图标或右键选择「设置」即可配置程序。

首次使用请在设置页选择模型提供方、填写自己的 API Key，并选择支持 Chat Completions 的文本模型，然后保存。提供方包括 DeepSeek、OpenAI、OpenRouter 和自定义兼容服务。选择内置提供方会自动填入接口地址；「抓取模型」可读取该服务的模型 ID 列表。部分模型 ID 不支持文本聊天，仍需按提供方的说明选择。

发布包**不包含词典数据**。配置在线翻译后仍可使用划词翻译；如需本地单词释义，请将你有权使用的 `dict.bin` 放在 `LexiPop.exe` 同目录，并重新启动程序。词库构建方法见[从源码构建](#从源码构建)。

> Windows 可能会对尚未使用公共受信任证书签名的程序显示安全提示。请先核对下载地址和发布页提供的 SHA-256 摘要，再决定是否运行。不要在不明来源的安装包中填写 API Key。

## 划词与快捷键

- 拖选文字后点击「译」，在鼠标附近打开结果小窗。单词会优先查询本地词库；其他文本交给已配置的模型翻译。
- `Ctrl+Alt+S`：翻译当前选中的文字，并打开结果小窗。
- `Ctrl+Alt+R`：隐藏当前结果小窗；窗口关闭后再次按下，会恢复上一次查询卡片。正在进行的翻译完成后，恢复的卡片也会更新。
- 在设置页点击快捷键输入框，再按目标组合键进行录入；点击「保存」后，全局快捷键才会更新。若组合键被其他程序占用，程序会提示注册失败。
- 点击浮窗外部会收起浮窗。长文本的原文和译文可以在小窗内滚动查看。

划词抓取依赖目标程序提供的文本选区，必要时会尝试剪贴板方式；部分程序、管理员权限窗口或受保护内容可能无法取词。若「译」按钮、托盘图标或设置保存异常，请确认程序是从正常的 Windows 桌面会话启动，而非受限宿主。程序会在检测到低完整性权限时提示并退出。

## 设置与主题

设置页可切换浅色、偏灰的深色及玻璃主题，也可新建或编辑 CSS 自定义主题。点击「新建主题」会生成变量模板；修改后点击「保存主题」。主题保存在 `%LOCALAPPDATA%\LexiPop\themes\custom-*.css`，可以用文本编辑器再次修改。

主题 CSS 使用 `:root` 中的约定变量，程序会将它们映射为 WPF 资源。支持模板中的 `--name`、`--is-dark`、`--is-glass`、颜色变量和三个 `--radius-...` 变量；颜色为 `#RRGGBB` 或 `#AARRGGBB`，圆角范围为 `0px` 至 `32px`。普通网页 CSS 选择器和布局规则不会作用于界面。保存时会校验必需变量。

设置及翻译缓存位于 `%LOCALAPPDATA%\LexiPop`。设置页保存的 API Key 会写入本机的 `settings.json`；如果不希望把 Key 写入该文件，可使用 `LEXIPOP_API_KEY` 环境变量，它的优先级高于设置文件。退出程序请使用托盘菜单中的「退出」。

## 从源码构建

需要 Windows 与 .NET 10 SDK。在 PowerShell 中运行：

```powershell
pwsh.exe -NoProfile -File ./build.ps1
```

构建输出位于 `src/LexiPop/bin/Release/net10.0-windows/`。`build.ps1` 会编译 Release；若当前用户已有 `CN=LexiPop Dev` 开发证书，还会对 exe 进行本机开发签名。开发证书只供本机使用，不代表公开可信的代码签名。需要在本机初始化证书时运行 `pwsh.exe -NoProfile -File ./tools/setup-signing-cert.ps1`。

词库文件是可选的，不进入 Git。若你拥有可使用的 `data/stardict.db`，可参考 `tools/build-dict-sqlite.py` 生成 `data/dict.bin`；构建时项目会将存在的 `dict.bin` 复制到输出目录。分发词库前请自行确认原始数据的授权。

## 项目结构与开发记录

- `src/LexiPop/App.xaml.cs`：单实例、托盘、热键和划词入口。
- `src/LexiPop/Views/`：设置窗口与划词结果小窗。
- `src/LexiPop/Services/`：取词、词库、在线翻译、主题与配置。
- `tools/`：词库构建、签名设置和开发辅助脚本。
- [开发日志](zxt/开发日志.md)：迭代结果、验证状态与踩坑记录。
- [技术方案](zxt/词典软件实施方案.md)：设计和实现背景。

`data/`、`dist/`、本地 SDK/依赖缓存、密钥和运行日志均由 `.gitignore` 排除。发布包的内容和已知限制见对应版本的 Release 说明。
