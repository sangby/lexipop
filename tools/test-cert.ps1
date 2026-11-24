$ErrorActionPreference = "Continue"

Write-Host "PSVersion: $($PSVersionTable.PSVersion)"

$thumbprint = "F02F1A26EF1E6D7513F3AC13BA5231C8BB6DF959"

$c1 = Get-ChildItem "Cert:\CurrentUser\My\$thumbprint" -ErrorAction SilentlyContinue
Write-Host "by thumbprint: $($c1.Subject)"

$c2 = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -eq "CN=LexiPop Dev" } |
    Select-Object -First 1
Write-Host "by subject: $($c2.Subject)"

try {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("My", "CurrentUser")
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    Write-Host "store count: $($store.Certificates.Count)"
    $store.Close()
}
catch {
    Write-Host "store error: $($_.Exception.Message)"
}
