using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BitBar
{
    public class NetworkConsumerTracker
    {
        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);

        private const int SystemProcessInformation = 5;
        private Dictionary<int, long> previousIo = new Dictionary<int, long>();

        public string GetTopConsumer()
        {
            try
            {
                int bufferSize = 1024 * 1024; // 1MB
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                
                int status = NtQuerySystemInformation(SystemProcessInformation, buffer, bufferSize, out int returnLength);
                
                if (status != 0)
                {
                    Marshal.FreeHGlobal(buffer);
                    return "Unavailable";
                }

                long maxDelta = 0;
                int topPid = 0;
                IntPtr currentPtr = buffer;
                
                while (true)
                {
                    int nextEntryOffset = Marshal.ReadInt32(currentPtr);
                    
                    // Note: Offsets are specific to 64-bit Windows
                    int pid = Marshal.ReadInt32(currentPtr, 80);
                    long readTransfer = Marshal.ReadInt64(currentPtr, 136);
                    long writeTransfer = Marshal.ReadInt64(currentPtr, 144);
                    
                    long totalIo = readTransfer + writeTransfer;

                    if (pid > 0 && pid != Process.GetCurrentProcess().Id)
                    {
                        if (previousIo.TryGetValue(pid, out long prevTotal))
                        {
                            long delta = totalIo - prevTotal;
                            if (delta > maxDelta)
                            {
                                maxDelta = delta;
                                topPid = pid;
                            }
                        }
                        previousIo[pid] = totalIo;
                    }

                    if (nextEntryOffset == 0) break;
                    currentPtr = new IntPtr(currentPtr.ToInt64() + nextEntryOffset);
                }

                Marshal.FreeHGlobal(buffer);

                if (topPid > 0 && maxDelta > 0)
                {
                    try
                    {
                        var proc = Process.GetProcessById(topPid);
                        string name = proc.ProcessName;
                        if (name.Equals("svchost", StringComparison.OrdinalIgnoreCase) || 
                            name.Equals("System", StringComparison.OrdinalIgnoreCase))
                        {
                            return $"System Services ({FormatData(maxDelta)}/s)";
                        }
                        return $"{name} ({FormatData(maxDelta)}/s)";
                    }
                    catch
                    {
                        return $"PID {topPid} ({FormatData(maxDelta)}/s)";
                    }
                }
                
                return "Idle";
            }
            catch
            {
                return "Unavailable";
            }
        }
        
        private string FormatData(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
