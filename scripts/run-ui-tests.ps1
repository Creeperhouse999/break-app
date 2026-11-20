# Runs Break App's end-to-end self-test on a separate, invisible Windows desktop,
# so no break window, notification or tray icon ever shows on your screen.
# Usage: scripts\run-ui-tests.ps1 [-Exe path\to\BreakApp.exe] [-TimeoutSeconds 300]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\BreakApp\bin\Debug\net10.0-windows\BreakApp.exe'),
    [int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$report = Join-Path ([IO.Path]::GetTempPath()) ("breakapp-selftest-{0}.txt" -f [Guid]::NewGuid().ToString('N'))

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class HiddenDesktop
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr sa);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd,
        ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    /// <summary>Runs exe on a new invisible desktop. Returns the exit code, or -1 on timeout.</summary>
    public static int Run(string exe, string args, int timeoutMs)
    {
        string name = "BreakAppTest_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        IntPtr desk = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000 /* GENERIC_ALL */, IntPtr.Zero);
        if (desk == IntPtr.Zero) throw new Exception("CreateDesktop failed: " + Marshal.GetLastWin32Error());
        try
        {
            var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), lpDesktop = name };
            PROCESS_INFORMATION pi;
            if (!CreateProcess(null, "\"" + exe + "\" " + args, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero,
                    System.IO.Path.GetDirectoryName(exe), ref si, out pi))
                throw new Exception("CreateProcess failed: " + Marshal.GetLastWin32Error());
            try
            {
                if (WaitForSingleObject(pi.hProcess, (uint)timeoutMs) != 0) { TerminateProcess(pi.hProcess, 1); return -1; }
                uint code; GetExitCodeProcess(pi.hProcess, out code);
                return (int)code;
            }
            finally { CloseHandle(pi.hProcess); CloseHandle(pi.hThread); }
        }
        finally { CloseDesktop(desk); }
    }
}
'@

$env:BREAKAPP_NO_AUTOSTART = '1'
Write-Host "Running self-test on a hidden desktop..." -ForegroundColor Cyan
$code = [HiddenDesktop]::Run($Exe, "--self-test `"$report`"", $TimeoutSeconds * 1000)
if (Test-Path $report) { Get-Content $report; Remove-Item $report }
else { Write-Host "no report written" -ForegroundColor Red }
if ($code -eq -1) { Write-Host "TIMED OUT after $TimeoutSeconds s" -ForegroundColor Red; exit 1 }
if ($code -ne 0) { Write-Host "self-test failed (exit $code)" -ForegroundColor Red; exit $code }
Write-Host "self-test passed" -ForegroundColor Green

