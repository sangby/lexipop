# Windows 本机 NuGet schannel TLS/SSL 错误排查指南

> 适用场景：`dotnet restore` / `dotnet add package` / `nuget push` 报
> "无法加载源 … 的服务索引"、"无法建立 SSL/TLS 安全通道"、`NU1301`、schannel 异常。

---

## 0. 本次环境的既有事实（先对齐，避免重复排查）

来自本项目此前的实际记录：

| 观察 | 结论 |
|---|---|
| `dotnet restore` → `NU1301: 无法加载源 https://api.nuget.org/v3/index.json` + `The SSL connection could not be established` + `Authentication failed` + **`安全包中没有可用的凭证`** | 是 **SChannel 客户端凭证**问题，不是"连不上网" |
| `curl.exe https://…` → `schannel: AcquireCredentialsHandle failed: SEC_E_NO_CREDENTIALS (0x8009030e)` | 与 .NET 同一个底层：SChannel |
| PowerShell `Invoke-WebRequest` 同样失败 | 同为 .NET/SChannel |
| **Node.js（OpenSSL）访问同一地址成功** | 网络可达；问题在 **SChannel 这一侧** |
| 本机有 Clash 代理（`127.0.0.1:7897`），Node 走代理可下载 GitHub/DeepSeek | 代理可用；需确认 SChannel 客户端是否配置了代理 |
| 出错进程运行在 **低完整性（Low Integrity）** 环境 | SChannel 可能无法访问证书存储/加密服务 → 直接表现为 `SEC_E_NO_CREDENTIALS` |

**结论先行**：本次现象不是 TLS 版本不匹配，而是 **SChannel 在初始化客户端凭证时失败**。最常见三种原因：
1. 运行进程权限/完整性不足（沙箱、受限账户）；
2. 系统证书存储或 CryptoAPI 服务异常；
3. 安全软件（火绒等）拦截了握手。

---

## 1. 现象确认

### 1.1 复现并记录原始错误

```powershell
# 用最简项目复现（避免被现有项目配置干扰）
mkdir C:\tmp\nugettest
cd C:\tmp\nugettest
dotnet new classlib -n Probe
cd Probe
dotnet restore --verbosity normal
```

重点看错误尾部，不同错误对应不同根因（见第 5 节对照表）。

### 1.2 分层确认是哪一层失败

```powershell
# A. 网络层：通不通
Test-NetConnection api.nuget.org -Port 443

# B. SChannel 层：curl 走 schannel
curl.exe -v --max-time 20 https://api.nuget.org/v3/index.json

# C. 另一种 TLS 实现（OpenSSL，Node 自带）做对照
#    若 B 失败而 C 成功 → 网络与证书都没问题，问题在 SChannel
node -e "fetch('https://api.nuget.org/v3/index.json').then(r=>console.log('status',r.status)).catch(e=>console.error('ERR',e.message))"
```

### 1.3 查看 SChannel 事件日志

```powershell
Get-WinEvent -LogName System -MaxEvents 200 |
  Where-Object { $_.ProviderName -eq 'Schannel' } |
  Select-Object -First 20 TimeCreated, Id, Message | Format-List
```

常见事件：`36887`（致命握手错误）、`36888`（收到致命警报），`Message` 里会带 `SEC_E_*` 码。

---

## 2. 原因定位

### 2.1 确认 TLS 协议版本

.NET 10 的 `HttpClient` 默认使用 TLS 1.2/1.3，**不依赖** `ServicePointManager`（那是 .NET Framework 的旧开关）。所以：

```powershell
# 只在 PowerShell 5.1 / .NET Framework 场景有意义
[Net.ServicePointManager]::SecurityProtocol
```

`api.nuget.org` 要求 TLS 1.2 及以上。若系统 SChannel **只启用了 TLS 1.0**（老系统或组策略锁定），会直接握手失败。

```powershell
$base = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols'
foreach ($v in 'TLS 1.2','TLS 1.3') {
    foreach ($side in 'Client','Server') {
        $p = Join-Path $base "$v\$side"
        if (Test-Path $p) {
            "$v $side : " + ((Get-ItemProperty $p).Enabled) + ' / DisabledByDefault=' + ((Get-ItemProperty $p).DisabledByDefault)
        } else {
            "$v $side : 键不存在（使用系统默认，Win10/11 默认启用）"
        }
    }
}
```

### 2.2 SChannel 注册表状态

Win10/11 默认已启用 TLS 1.2/1.3 客户端。若被安全软件或组策略改成禁用，需要恢复：

```powershell
$base = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols'
foreach ($v in 'TLS 1.2','TLS 1.3') {
    foreach ($side in 'Client') {
        $p = Join-Path $base "$v\$side"
        New-Item -Path $p -Force | Out-Null
        Set-ItemProperty -Path $p -Name 'Enabled' -Value 1 -Type DWord
        Set-ItemProperty -Path $p -Name 'DisabledByDefault' -Value 0 -Type DWord
    }
}
```

> 修改后需重启相关进程（或重启系统）生效。**不要**关闭 TLS 1.0/1.1 之外再乱改，避免影响系统更新。

### 2.3 证书链 / 代理 / 防火墙 / 杀毒

| 检查项 | 命令 / 位置 |
|---|---|
| 根证书数量是否异常少 | `(Get-ChildItem Cert:\LocalMachine\Root).Count`（正常 50+） |
| 证书存储是否可读 | `certutil -store Root` 不报错 |
| 系统代理（WinHTTP） | `netsh winhttp show proxy` |
| 用户级代理环境变量 | `[Environment]::GetEnvironmentVariable('HTTP_PROXY')`、`HTTPS_PROXY` |
| 防火墙出站 | `netsh advfirewall show allprofiles` |
| 安全软件 | 火绒/360/Defender 的"网络防护"是否拦截 `dotnet.exe`、`curl.exe` |

**重点**：`SEC_E_NO_CREDENTIALS` 经常不是"证书不受信任"，而是 **SChannel 拿不到客户端凭证句柄**，典型诱因：

- 进程以 **低完整性/受限令牌** 运行（沙箱、AppContainer、部分远程注入环境）
- **CryptoAPI 相关服务被禁用**：`CryptSvc`（Cryptographic Services）
- 安全软件 hook 了 `AcquireCredentialsHandle`

```powershell
Get-Service CryptSvc | Select-Object Status, StartType
```

### 2.4 NuGet.Config

```powershell
# 查看生效的包源
dotnet nuget list source

# 查看用户级配置路径
%APPDATA%\NuGet\NuGet.Config

# 查看是否配置了代理（.NET 8+ 的 NuGet 支持 http_proxy）
dotnet nuget config get http_proxy
```

需要检查的点：

- **源地址**是否仍是 `https://api.nuget.org/v3/index.json`，有没有被改成 http 或失效镜像
- 是否有 **`<packageSourceCredentials>`**、**`<config>` 里的 `http_proxy`** 指向了不可用代理
- 是否有企业证书/自签证书源需要额外信任
- 如果有 `nuget.config` 里写了 `signatureValidationMode` 等，也要核对

---

## 3. 修复步骤（按优先级，逐条验证）

### 步骤 1：确认是不是"运行环境受限"

如果在沙箱/受限进程里执行，先换到普通权限的 PowerShell：

- 开始菜单搜索 PowerShell → 右键"以普通用户身份运行"（**不要**用管理员，除非必要）
- 或直接在资源管理器地址栏输入 `cmd` 回车

**本机特别注意**：本项目此前的 `dotnet` 命令是在 DSH 沙箱（Low Integrity）里跑的，SChannel 无法访问证书存储，必然报 `SEC_E_NO_CREDENTIALS`。**在普通 PowerShell 里重试是第一步**。

### 步骤 2：清理并确认代理

```powershell
# 有代理（如 Clash 7897）时，给 dotnet 显式设置
$env:HTTP_PROXY  = 'http://127.0.0.1:7897'
$env:HTTPS_PROXY = 'http://127.0.0.1:7897'
dotnet restore --verbosity normal
```

如果**不用代理**而系统代理开着，可以临时关掉用户代理再试：

```powershell
dotnet nuget config unset http_proxy
```

### 步骤 3：修复 SChannel / 证书存储

```powershell
# 启动加密服务
Start-Service CryptSvc -ErrorAction SilentlyContinue

# 刷新证书存储缓存
certutil -URLCache * delete

# 重新导入根证书（从系统更新）
certutil -generateSSTFromWU C:\tmp\roots.sst
```

### 步骤 4：启用 TLS 1.2/1.3（见 2.2 的命令），重启系统后重试

### 步骤 5：排除安全软件

临时退出火绒/360/Defender 实时防护，或在其中把 `dotnet.exe`、`nuget.exe`、`curl.exe` 加入白名单后重试。

### 步骤 6：临时换源（国内网络环境）

```xml
<!-- %APPDATA%\NuGet\NuGet.Config -->
<packageSources>
  <clear />
  <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  <add key="cn" value="https://mirrors.cloud.tencent.com/nuget/" />
</packageSources>
```

> 换源只是绕开网络/证书问题，**不解决** `SEC_E_NO_CREDENTIALS` 这类本地 SChannel 故障；若换源后仍报同样错误，说明问题确实在本机 SChannel。

### 步骤 7：最后一招——用 OpenSSL 栈的工具验证包能否下载

```powershell
# 若能下载 nupkg，说明网络/证书侧没问题，问题100%在 SChannel
curl.exe -L -o C:\tmp\probe.nupkg https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.3/newtonsoft.json.13.0.3.nupkg
```

---

## 4. 验证方法

### 4.1 配置层

```powershell
dotnet nuget list source
```

期望输出包含 `nuget [Enabled] https://api.nuget.org/v3/index.json`。

### 4.2 握手层

```powershell
curl.exe -v --max-time 20 https://api.nuget.org/v3/index.json
```

成功标志：`HTTP/1.1 200` 或 `HTTP/2 200`，且 TLS 段显示 `TLS 1.2` / `TLS 1.3`。

### 4.3 业务层

```powershell
cd C:\tmp\nugettest\Probe
dotnet restore --verbosity normal
```

成功标志：`已还原 …`（`Restored …`），无 `NU1301`。

### 4.4 推送层（若有私有源）

```powershell
dotnet nuget push .\bin\Release\*.nupkg --source https://api.nuget.org/v3/index.json --api-key <key> --skip-duplicate
```

---

## 5. 常见错误对照表

| 错误文本 / 错误码 | 含义 | 优先排查 |
|---|---|---|
| `SEC_E_NO_CREDENTIALS (0x8009030e)` / `安全包中没有可用的凭证` | SChannel 无法创建客户端凭证 | **运行权限/完整性**、CryptSvc、安全软件 |
| `SEC_E_UNTRUSTED_ROOT (0x80090325)` | 根证书不受信任 | 证书链、企业代理 MITM 证书、根证书缺失 |
| `SEC_E_WRONG_PRINCIPAL` / `SEC_E_ILLEGAL_MESSAGE` | 证书主机名或协议不被接受 | 代理劫持、SNI、TLS 版本 |
| `The SSL connection could not be established` + inner `Authentication failed` | 握手失败（上层笼统描述） | 看 inner exception 里的具体 `SEC_E_*` |
| `NU1301` | 包源索引加载失败 | 源地址、代理、TLS |
| `NU1101` | 找不到包（源里没有） | 源配置，不是 TLS 问题 |
| `无法建立 SSL/TLS 安全通道` | 中文版握手失败 | 同上，需 `-v` 看细节 |
| `The request was aborted: Could not create SSL/TLS secure channel.` | .NET Framework 旧版 TLS 未启用 | `ServicePointManager.SecurityProtocol`、TLS 注册表 |

---

## 6. 本机结论（针对当前这台机器）

1. **网络可达**（Node/OpenSSL 能访问 nuget.org、GitHub、DeepSeek）。
2. **SChannel 在受限进程里不可用**：`SEC_E_NO_CREDENTIALS` 在低完整性沙箱中出现。
3. **解决路径**：
   - 在**普通权限 PowerShell**（非沙箱）里执行 `dotnet restore`，大概率直接成功；
   - 若仍失败，按第 3 节步骤 2→3→5 顺序排查（代理 → 证书服务 → 安全软件）；
   - 临时替代：用 Node/curl 下载 `.nupkg`，或改用 `--source` 指向国内镜像。
