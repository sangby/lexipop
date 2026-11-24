using LexiPop.Interop;

namespace LexiPop.Services;

/// <summary>
/// 单实例控制：第一次启动持有 Mutex；后续启动通知已有实例打开设置。
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\LexiPop.SingleInstance";
    // 保留旧广播消息名，使已运行的旧版实例仍能收到第二次启动通知。
    private const string ShowMessageName = "LexiPop.ShowWindow";

    public static readonly int ShowMessageId = NativeMethods.RegisterWindowMessage(ShowMessageName);

    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

        if (createdNew) return true;

        // 对象已存在：可能只是上一个实例异常退出后残留的对象，未必有持有者。
        // 非阻塞尝试获取；拿不到才说明确实有实例在运行。
        try
        {
            if (_mutex.WaitOne(0)) return true;
        }
        catch (AbandonedMutexException)
        {
            // 上一个持有者已结束，本进程接管所有权
            return true;
        }

        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    public static void NotifyExistingInstance()
    {
        if (ShowMessageId == 0) return;
        NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, ShowMessageId, IntPtr.Zero, IntPtr.Zero);
    }

    public static void Release()
    {
        if (_mutex is null) return;

        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { /* 不是持有者，忽略 */ }

        _mutex.Dispose();
        _mutex = null;
    }
}
