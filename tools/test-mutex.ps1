$name = 'Local\LexiPop.SingleInstance'
try {
    $m = New-Object System.Threading.Mutex($false, $name)
    try {
        $got = $m.WaitOne(0)
        "WaitOne(0) = $got"
    }
    catch {
        "WaitOne exception: $($_.Exception.GetType().Name)"
    }
    finally {
        $m.Dispose()
    }
}
catch {
    "Open exception: $($_.Exception.GetType().Name): $($_.Exception.Message)"
}
