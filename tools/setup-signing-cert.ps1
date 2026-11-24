# 生成并信任 LexiPop 开发用自签名代码签名证书。
# 重装系统或换 Windows 用户后需要重新运行一次。
# 用法：powershell -ExecutionPolicy Bypass -File .\tools\setup-signing-cert.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$subject = "CN=LexiPop Dev"

$existing = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject } |
    Select-Object -First 1

if ($null -eq $existing) {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $subject `
        -FriendlyName "LexiPop Dev Code Signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(5)

    Write-Host "已生成证书：$($cert.Thumbprint)"
}
else {
    $cert = $existing
    Write-Host "证书已存在：$($cert.Thumbprint)"
}

$cerPath = Join-Path $root "lexipop-dev.cer"
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null

# 直接写 X509Store，避免 Import-Certificate 触发的交互式安全确认。
$cer = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "CurrentUser")
$store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
$store.Add($cer)
$store.Close()

Write-Host "证书已写入受信任根存储（CurrentUser\Root）。"

$exe = Join-Path $root "src\LexiPop\bin\Release\net10.0-windows\LexiPop.exe"
if (Test-Path $exe) {
    Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -HashAlgorithm SHA256 | Out-Null
    $signature = Get-AuthenticodeSignature $exe
    Write-Host "已签名，状态：$($signature.Status)"
}
