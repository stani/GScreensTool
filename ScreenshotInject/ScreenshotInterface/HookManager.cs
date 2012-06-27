using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using EasyHook;
using System.Threading;

namespace ScreenshotInterface
{
    public class HookManager
    {
        static internal List<Int32> HookedProcesses = new List<Int32>();

        /*
         * Please note that we have obtained this information with system privileges.
         * So if you get client requests with a process ID don't try to open the process
         * as this will fail in some cases. Just search the ID in the following list and
         * extract information that is already there...
         * 
         * Of course you can change the way this list is implemented and the information
         * it contains but you should keep the code semantic.
         */
        internal static List<ProcessInfo> ProcessList = new List<ProcessInfo>();
        //private static List<Int32> ActivePIDList = new List<Int32>();

        public static ProcessInfo AddHookedProcess(Process process)
        {
            lock (ProcessList)
            {
                ProcessInfo pInfo = new ProcessInfo(process);
                ProcessList.Add(pInfo);
                HookedProcesses.Add(process.Id);
                return pInfo;
            }
        }

        public static bool IsHooked(Process process)
        {
            lock (ProcessList)
            {
                return ProcessList.Contains(new ProcessInfo(process));
            }
        }

        public static ProcessInfo GetHookedProcess(Process process)
        {
            lock (ProcessList)
            {
                ProcessInfo pInfo = new ProcessInfo(process);
                int index = ProcessList.IndexOf(pInfo);
                if (index > -1)
                {
                    return ProcessList[index];
                }
                else
                {
                    return null;
                }
            }
        }

        [Serializable]
        public class ProcessInfo
        {
            
            public Process Process;
            public Thread IntervalThread;
            public int Interval;

            public ProcessInfo(Process process)
            {
                this.Process = process;
                
            }

            public override bool Equals(System.Object obj)
            {
                // If parameter is null return false.
                if (obj == null)
                {
                    return false;
                }

                // If parameter cannot be cast to Point return false.
                ProcessInfo p = obj as ProcessInfo;
                if ((System.Object)p == null)
                {
                    return false;
                }

                // Return true if the fields match:
                return p.Process.Id==this.Process.Id;
            }

            public bool Equals(ProcessInfo p)
            {
                // If parameter is null return false:
                if ((object)p == null)
                {
                    return false;
                }

                // Return true if the fields match:
                return p.Process.Id==this.Process.Id;
            }

            public override int GetHashCode()
            {
                return this.Process.Id;
            }
        }

        //public static ProcessInfo[] EnumProcesses0()
        //{
        //    List<ProcessInfo> result = new List<ProcessInfo>();
        //    Process[] procList = Process.GetProcesses();

        //    for (int i = 0; i < procList.Length; i++)
        //    {
        //        Process proc = procList[i];

        //        try
        //        {
        //            ProcessInfo info = new ProcessInfo();

        //            info.FileName = proc.MainModule.FileName;
        //            info.Id = proc.Id;
        //            info.Is64Bit = RemoteHooking.IsX64Process(proc.Id);
        //            info.User = RemoteHooking.GetProcessIdentity(proc.Id).Name;

        //            result.Add(info);
        //        }
        //        catch
        //        {
        //        }
        //    }

        //    return result.ToArray();
        //}
    }
}
