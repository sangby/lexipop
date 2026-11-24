# LexiPop 构建脚本：编译 Release，并用自签名证书给 exe 签名。
# 用法：powershell -ExecutionPolicy Bypass -File .\build.ps1

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\LexiPop\LexiPop.csproj"
$exe = Join-Path $root "src\LexiPop\bin\Release\net10.0-windows\LexiPop.exe"

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $root ".nuget"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$systemDotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
$dotnet = if ($null -ne $systemDotnet) {
    $systemDotnet.Source
} else {
    Join-Path $root ".dotnet\dotnet.exe"
}

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "找不到 .NET SDK，请安装 .NET 10 SDK。"
}

& $dotnet build $project -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "编译失败。"
    exit $LASTEXITCODE
}

$thumbprint = "F02F1A26EF1E6D7513F3AC13BA5231C8BB6DF959"
$cert = Get-ChildItem "Cert:\CurrentUser\My\$thumbprint" -ErrorAction SilentlyContinue

if ($null -eq $cert) {
    $cert = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq "CN=LexiPop Dev" } |
        Select-Object -First 1
}

if ($null -eq $cert) {
    Write-Warning "找不到 CN=LexiPop Dev 证书，跳过签名。"
    exit 0
}

Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -HashAlgorithm SHA256 | Out-Null
$signature = Get-AuthenticodeSignature $exe

Write-Host ""
Write-Host "输出：$exe"
Write-Host "签名：$($signature.Status)"

if ($signature.Status -ne "Valid") {
    Write-Warning "签名状态异常：$($signature.StatusMessage)"
}
