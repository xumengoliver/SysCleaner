using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace SysCleaner.Infrastructure.Interop;

internal static class RestartManagerInterop
{
    public const int RmRebootReasonNone = 0;
    public const int CchRmSessionKey = 32;
    public const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    public struct RmUniqueProcess
    {
        public int dwProcessId;
        public FILETIME ProcessStartTime;
    }

    public enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;

        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmStartSession(out uint sessionHandle, int sessionFlags, string sessionKey);

    [DllImport("rstrtmgr.dll")]
    public static extern int RmEndSession(uint sessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmRegisterResources(uint sessionHandle, uint fileCount, string[]? fileNames, uint appCount, IntPtr applications, uint serviceCount, string[]? serviceNames);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmGetList(uint sessionHandle, out uint processInfoNeeded, ref uint processInfo, [In, Out] RmProcessInfo[]? processInfoArray, ref uint rebootReasons);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}