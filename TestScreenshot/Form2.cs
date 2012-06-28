using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using ScreenshotInterface;
using EasyHook;
using System.Runtime.Remoting.Channels.Ipc;
using System.Runtime.Remoting;
using System.Runtime.InteropServices;
using System.IO;

namespace TestScreenshot
{
    public partial class Form2 : Form
    {
        private String ChannelName = null;
        private IpcServerChannel ScreenshotServer;
        private static String[] supportedModules = { "d3d9.dll", "d3d10.dll", "d3d10_1.dll", "d3d11.dll", "d3d11_1.dll" };
        private static String[] autoAttach = { "MassEffect3" };

        public Form2()
        {
            InitializeComponent();
        }

        private void Form2_Load(object sender, EventArgs e)
        {
            // Initialise the IPC server
            ScreenshotServer = RemoteHooking.IpcCreateServer<ScreenshotInterface.ScreenshotInterface>(
                ref ChannelName,
                WellKnownObjectMode.Singleton);
            ScreenshotManager.OnScreenshotDebugMessage += new ScreenshotDebugMessage(ScreenshotManager_OnScreenshotDebugMessage);

            textBox3.Text = Properties.Settings.Default.path;

            textBox4.Text = Properties.Settings.Default.key.ToString();
            textBox4.KeyDown+=new KeyEventHandler(textBox4_KeyDown);

            comboBox1.Items.AddRange(new Object[]{
                ImageFormat.Jpeg,
                ImageFormat.Bmp,
                ImageFormat.Png});

            comboBox1.SelectedItem = Properties.Settings.Default.format;

            textBox2.Text = Properties.Settings.Default.interval.ToString();
            textBox2.Validating+=new CancelEventHandler(textBox2_Validating);
            textBox2.Validated+=new EventHandler(textBox2_Validated);
            this.Resize += new System.EventHandler(this.Form2_Resize);
            notifyIcon1.DoubleClick+=new EventHandler(notifyIcon1_DoubleClick);

            Thread poller = new Thread(new ThreadStart(PollProcesses));
            poller.IsBackground = true;
            poller.Start();

            Thread poller2 = new Thread(new ThreadStart(PollKeyboard));
            poller2.IsBackground = true;
            poller2.Start();
        }
        /// <summary>
        /// Display debug messages from the target process
        /// </summary>
        /// <param name="clientPID"></param>
        /// <param name="message"></param>
        void ScreenshotManager_OnScreenshotDebugMessage(int clientPID, string message)
        {
            txtDebugLog.Invoke(new MethodInvoker(delegate()
            {
                txtDebugLog.Text = String.Format("{0}:{1}\r\n{2}", clientPID, message, txtDebugLog.Text);
            })
            );
        }

        private void PollProcesses()
        {
            while (true)
            {
                Thread.Sleep(1000);
                AttachCurrentProcess(force:false);

            }
        }

        private void PollKeyboard()
        {
            bool pressed = false;
            Keys key = Properties.Settings.Default.key;// Keys.K;
            DateTime pressedTime=DateTime.Now;
            bool longPress=false;

            while (true)
            {
                if (!pressed)
                {
                    if (Keyboard.IsKeyPressed((int)key))
                    {
                        //ScreenshotManager_OnScreenshotDebugMessage(0, "Down");
                        pressed = true;
                        pressedTime = DateTime.Now;
                        
                    }
                }
                else
                {
                    if (!Keyboard.IsKeyPressed((int)key))
                    {

                        if (!longPress)
                        {
                            DoRequest();
                        }
                        pressed = false;
                        longPress = false;
                    }
                    else
                    {
                        
                        if (!longPress && (DateTime.Now - pressedTime).TotalMilliseconds > 2000)
                        {
                            longPress = true;
                            DoRequest(true);
                        }
                        
                    }

                }


                Thread.Sleep(10);
            }
        }

        private List<String> GetProcessModules(Process p)
        {
            // Setting up the variable for the second argument for EnumProcessModules
            IntPtr[] hMods = new IntPtr[1024];

            GCHandle gch = GCHandle.Alloc(hMods, GCHandleType.Pinned); // Don't forget to free this later
            IntPtr pModules = gch.AddrOfPinnedObject();

            List<String> result = new List<String>();

            // Setting up the rest of the parameters for EnumProcessModules
            uint uiSize = (uint)(Marshal.SizeOf(typeof(IntPtr)) * (hMods.Length));
            uint cbNeeded = 0;

            if (EnumProcessModulesEx(p.Handle, hMods, uiSize, out cbNeeded, 0x03))
            {
                Int32 uiTotalNumberofModules = (Int32)(cbNeeded / (Marshal.SizeOf(typeof(IntPtr))));

                for (int i = 0; i < (int)uiTotalNumberofModules; i++)
                {
                    StringBuilder strbld = new StringBuilder(1024);

                    GetModuleFileNameEx(p.Handle, hMods[i], strbld, (uint)(strbld.Capacity));
                    String module = strbld.ToString();
                    result.Add(System.IO.Path.GetFileName(module).ToLower());

                }
                
            }

            // Must free the GCHandle object
            gch.Free();

            return result;
        }

        public void PrintKey(String s)
        {
            ScreenshotManager_OnScreenshotDebugMessage(0, s);
        }

        //int processId = 0;
        //Process _process;
        private Process GetForegroundProcess()
        {
            Process[] processes = Process.GetProcesses();

            foreach (Process process in processes)
            {
                IntPtr handle = process.MainWindowHandle;
                //ScreenshotManager_OnScreenshotDebugMessage(0, "Searching in "+process.ProcessName);
                if (NativeMethods.IsWindowInForeground(handle)) {

                    //ScreenshotManager_OnScreenshotDebugMessage(0, "Found main window!");
                    return process;
                }
            }
            //ScreenshotManager_OnScreenshotDebugMessage(0, "Not found");
            return null;
        }

        private HookManager.ProcessInfo AttachCurrentProcess(bool force=true)
        {
            try
            {
                Process currentProcess = GetForegroundProcess();
                
                if (currentProcess == null)
                {
                    if (force == true)
                    {
                        ScreenshotManager_OnScreenshotDebugMessage(0, "No Foreground  found");
                    }
                    return null;
                }

                HookManager.ProcessInfo pInfo = HookManager.GetHookedProcess(currentProcess);

                // Skip if the process is already hooked (and we want to hook multiple applications)
                if (pInfo == null)
                {
                    if (force == false & !autoAttach.Contains(currentProcess.ProcessName))
                    {
                        return null;
                    }

                    List<String> modules = GetProcessModules(currentProcess);
                    bool hasDx = false;
                    foreach(String module in supportedModules)
                    {
                        hasDx = (hasDx | modules.Contains(module));
                        
                    }

                    if (!hasDx)
                    {
                        ScreenshotManager_OnScreenshotDebugMessage(0, "No DX found");
                        return null;
                    }

                    // Keep track of hooked processes in case more than one needs to be hooked
                    pInfo = HookManager.AddHookedProcess(currentProcess);
                    // Inject DLL into target process
                    try
                    {

                        RemoteHooking.Inject(
                            currentProcess.Id,
                            InjectionOptions.Default,
                            typeof(ScreenshotInject.ScreenshotInjection).Assembly.Location,//"ScreenshotInject.dll", // 32-bit version (the same because AnyCPU) could use different assembly that links to 32-bit C++ helper dll
                            typeof(ScreenshotInject.ScreenshotInjection).Assembly.Location, //"ScreenshotInject.dll", // 64-bit version (the same because AnyCPU) could use different assembly that links to 64-bit C++ helper dll
                            // the optional parameter list...
                            ChannelName, // The name of the IPC channel for the injected assembly to connect to
                            Direct3DVersion.AutoDetect.ToString(), // The direct3DVersion used in the target application
                            false //cbDrawOverlay.Checked
                        );
                    }
                    catch (Exception e)
                    {
                        ScreenshotManager_OnScreenshotDebugMessage(currentProcess.Id, e.GetType().FullName + ": " + e.Message);
                    }
                    ScreenshotManager_OnScreenshotDebugMessage(currentProcess.Id, "Injected ");
                }
                return pInfo;
            }
            catch (Exception e)
            {
                ScreenshotManager_OnScreenshotDebugMessage(0, e.ToString());
                return null;
            }
        }


        DateTime start;
        DateTime end;
        Thread IntervalThread=null;
        String key_const;


 
        /// <summary>
        /// Create the screen shot request
        /// </summary>
        public void DoRequest(bool setInterval=false)
        {
            HookManager.ProcessInfo pInfo = AttachCurrentProcess();
            if (pInfo == null)
            {
                return;
            }

            ScreenshotManager_OnScreenshotDebugMessage(pInfo.Process.Id, "Do request "+setInterval);
            start = DateTime.Now;

            ScreenshotManager.AddScreenshotRequest(pInfo.Process.Id, new ScreenshotRequest(FileName(pInfo.Process), Properties.Settings.Default.format.ToString(), setInterval, Properties.Settings.Default.interval), Callback);

        }

        /// <summary>
        /// Create the screen shot request
        /// </summary>
        public void DoRequestOld()
        {
            HookManager.ProcessInfo pInfo = AttachCurrentProcess();
            if (pInfo == null)
            {
                return;
            }

            ScreenshotManager_OnScreenshotDebugMessage(pInfo.Process.Id, "Do request ");
            start = DateTime.Now;

            //int j;
            //ImageCodecInfo[] encoders;
            //encoders = ImageCodecInfo.GetImageEncoders();
            //for(j = 0; j < encoders.Length; ++j)
            //{
            //    ScreenshotManager_OnScreenshotDebugMessage(process.Id, encoders[j].MimeType);

            //}

            if (pInfo.IntervalThread == null)
            {
                if (Properties.Settings.Default.interval > 0)
                {
                    pInfo.Interval = Properties.Settings.Default.interval;
                    pInfo.IntervalThread = new Thread(new ParameterizedThreadStart(IntervalCapture));
                    pInfo.IntervalThread.Start(pInfo);

                }
                else
                {
                    // Add a request to the screenshot manager - the ScreenshotInterface will pass this on to the injected assembly
                    ScreenshotManager.AddScreenshotRequest(pInfo.Process.Id, new ScreenshotRequest(FileName(pInfo.Process), Properties.Settings.Default.format.ToString()), Callback);
                }
            }
            else
            {
                pInfo.IntervalThread = null;
            }
        }

        
        public void IntervalCapture(object prs)
        {

            HookManager.ProcessInfo pInfo = (HookManager.ProcessInfo)prs;
            Process process = pInfo.Process;

            while (Thread.CurrentThread==pInfo.IntervalThread && !pInfo.Process.HasExited)
            {
                ScreenshotManager.AddScreenshotRequest(process.Id, new ScreenshotRequest(FileName(process), Properties.Settings.Default.format.ToString()), Callback);
                Thread.Sleep(pInfo.Interval * 1000);
            }

            if (Thread.CurrentThread == pInfo.IntervalThread)
            {
                pInfo.IntervalThread = null;
            }
        }

        /// <summary>
        /// The callback for when the screenshot has been taken
        /// </summary>
        /// <param name="clientPID"></param>
        /// <param name="status"></param>
        /// <param name="screenshotResponse"></param>
        void Callback(Int32 clientPID, ResponseStatus status, ScreenshotResponse screenshotResponse)
        {
            //try
            //{
            //    if (screenshotResponse != null && screenshotResponse.CapturedBitmap != null)
            //    {
            //        Bitmap bmp = screenshotResponse.CapturedBitmapAsImage;
            //        Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            //        System.Drawing.Imaging.BitmapData bmpData =
            //            bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
            //            bmp.PixelFormat);
            //        bmp.UnlockBits(bmpData);
            //        ScreenshotManager_OnScreenshotDebugMessage(clientPID, "Size is " + bmp.Size.ToString());
            //        //MessageBox.Show("Size is " + screenshotResponse.CapturedBitmapAsImage.Size.ToString());
                    

            //        String dir=_process.ProcessName+@"\"+_process.StartTime+@"\";
            //        String file = _process.ProcessName + "_" + DateTime.Now + ".jpg";

            //        dir = @"c:\temp\" + dir.Replace(':','-').Replace(' ','-');
            //        file = file.Replace(':', '-').Replace(' ', '-');


            //        ScreenshotManager_OnScreenshotDebugMessage(clientPID, "Saving to " + dir + file);
            //        if (!Directory.Exists(dir)) {
            //            Directory.CreateDirectory(dir);
            //        }
            //        bmp.Save(dir+file, ImageFormat.Jpeg);
            //        ScreenshotManager_OnScreenshotDebugMessage(clientPID, "Saved to "+dir+file);
            //     }

            //    //Thread t = new Thread(new ThreadStart(DoRequest));
            //    //t.Start();
            //}
            //catch(Exception e)
            //{
            //    ScreenshotManager_OnScreenshotDebugMessage(clientPID, e.ToString());
            //}
        }

        private String FileName(Process process)
        {
            String ext = "jpg";

            if (Properties.Settings.Default.format == ImageFormat.Bmp)
            {
                ext = "bmp";
            }
            else if (Properties.Settings.Default.format == ImageFormat.Png)
            {
                ext = "png";
            }
            
            
            //String dir = process.ProcessName + @"\" + process.StartTime + @"\";
            //String file = process.ProcessName + "_" + DateTime.Now + "."+ext;

            //dir = Properties.Settings.Default.path + @"\" + dir.Replace(':', '-').Replace(' ', '-');
            //file = file.Replace(':', '-').Replace(' ', '-');

            //if (!Directory.Exists(dir))
            //{
            //    Directory.CreateDirectory(dir);
            //}

            String f = Properties.Settings.Default.path +
                Path.DirectorySeparatorChar +
                "{0}" +
                Path.DirectorySeparatorChar +
                "{1:yyyy-MM-dd-HH-mm-ss}" +
                Path.DirectorySeparatorChar+
                "{0}-{2:HH-mm-ss.fff}"+
                "." +ext;

            return f;
        }

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                textBox3.Text = folderBrowserDialog1.SelectedPath;
                Properties.Settings.Default.path = folderBrowserDialog1.SelectedPath;
                Properties.Settings.Default.Save();
            }

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.format = (ImageFormat)comboBox1.SelectedItem;
            Properties.Settings.Default.Save();
        }

        private void textBox4_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            textBox4.Text = e.KeyData.ToString();
            e.Handled = true;
            e.SuppressKeyPress = true;
            Properties.Settings.Default.key = e.KeyData;
            Properties.Settings.Default.Save();
        }

        private void Logo_Click(object sender, EventArgs e)
        {

        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox2_Validating(object sender,
                System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                int interval = int.Parse(textBox2.Text);
                
                if (interval >= 0)
                {
                    Properties.Settings.Default.interval = interval;
                    Properties.Settings.Default.Save();
                    return;
                }
            }
            catch{}

            e.Cancel = true;
            textBox2.Select(0, textBox2.Text.Length);
            errorProvider1.SetError(textBox2, "Must be positive number or zero");
            
        }
        private void textBox2_Validated(object sender, System.EventArgs e)
        {
            // If all conditions have been met, clear the ErrorProvider of errors.
            errorProvider1.SetError(textBox2, "");
        }
        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            try
            {
                
                Properties.Settings.Default.interval = int.Parse(textBox2.Text);
                Properties.Settings.Default.Save();                
            }
            catch
            {
                textBox2.Text = Properties.Settings.Default.interval.ToString();

            }
        }


        private void Form2_Resize(object sender, System.EventArgs e)
        {
            if (FormWindowState.Minimized == WindowState)
                Hide();
        }
        private void notifyIcon1_DoubleClick(object sender,
                                     System.EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("psapi.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
        public static extern int EnumProcessModules(IntPtr hProcess, [Out] IntPtr lphModule, uint cb, out uint lpcbNeeded);

        [DllImport("psapi.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpBaseName, uint nSize);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumProcessModulesEx(
            [In] IntPtr ProcessHandle,
            [Out] IntPtr[] ModuleHandles,
            [In] uint Size,
            [Out] out uint RequiredSize,
            [In] uint dwFilterFlag
            );

        private void label5_Click(object sender, EventArgs e)
        {

        }

        private void label7_Click(object sender, EventArgs e)
        {

        }
    }

    public class Keyboard
    {
        [DllImport("user32.dll")]
        static extern short GetKeyState(int nVirtKey);

        public static bool IsKeyPressed(int testKey)
        {
            bool keyPressed = false;
            short result = GetKeyState(testKey);

            switch (result)
            {
                case 0:
                    // Not pressed and not toggled on.
                    keyPressed = false;
                    break;

                case 1:
                    // Not pressed, but toggled on
                    keyPressed = false;
                    break;

                default:
                    // Pressed (and may be toggled on)
                    keyPressed = true;
                    break;
            }

            return keyPressed;
        }

        public enum VirtualKeyStates : int
        {
            VK_LBUTTON = 0x01,
            VK_RBUTTON = 0x02,
            VK_CANCEL = 0x03,
            VK_MBUTTON = 0x04,
            //
            VK_XBUTTON1 = 0x05,
            VK_XBUTTON2 = 0x06,
            //
            VK_BACK = 0x08,
            VK_TAB = 0x09,
            //
            VK_CLEAR = 0x0C,
            VK_RETURN = 0x0D,
            //
            VK_SHIFT = 0x10,
            VK_CONTROL = 0x11,
            VK_MENU = 0x12,
            VK_PAUSE = 0x13,
            VK_CAPITAL = 0x14,
            //
            VK_KANA = 0x15,
            VK_HANGEUL = 0x15, /* old name - should be here for compatibility */
            VK_HANGUL = 0x15,
            VK_JUNJA = 0x17,
            VK_FINAL = 0x18,
            VK_HANJA = 0x19,
            VK_KANJI = 0x19,
            //
            VK_ESCAPE = 0x1B,
            //
            VK_CONVERT = 0x1C,
            VK_NONCONVERT = 0x1D,
            VK_ACCEPT = 0x1E,
            VK_MODECHANGE = 0x1F,
            //
            VK_SPACE = 0x20,
            VK_PRIOR = 0x21,
            VK_NEXT = 0x22,
            VK_END = 0x23,
            VK_HOME = 0x24,
            VK_LEFT = 0x25,
            VK_UP = 0x26,
            VK_RIGHT = 0x27,
            VK_DOWN = 0x28,
            VK_SELECT = 0x29,
            VK_PRINT = 0x2A,
            VK_EXECUTE = 0x2B,
            VK_SNAPSHOT = 0x2C,
            VK_INSERT = 0x2D,
            VK_DELETE = 0x2E,
            VK_HELP = 0x2F,
            //
            VK_LWIN = 0x5B,
            VK_RWIN = 0x5C,
            VK_APPS = 0x5D,
            //
            VK_SLEEP = 0x5F,
            //
            VK_NUMPAD0 = 0x60,
            VK_NUMPAD1 = 0x61,
            VK_NUMPAD2 = 0x62,
            VK_NUMPAD3 = 0x63,
            VK_NUMPAD4 = 0x64,
            VK_NUMPAD5 = 0x65,
            VK_NUMPAD6 = 0x66,
            VK_NUMPAD7 = 0x67,
            VK_NUMPAD8 = 0x68,
            VK_NUMPAD9 = 0x69,
            VK_MULTIPLY = 0x6A,
            VK_ADD = 0x6B,
            VK_SEPARATOR = 0x6C,
            VK_SUBTRACT = 0x6D,
            VK_DECIMAL = 0x6E,
            VK_DIVIDE = 0x6F,
            VK_F1 = 0x70,
            VK_F2 = 0x71,
            VK_F3 = 0x72,
            VK_F4 = 0x73,
            VK_F5 = 0x74,
            VK_F6 = 0x75,
            VK_F7 = 0x76,
            VK_F8 = 0x77,
            VK_F9 = 0x78,
            VK_F10 = 0x79,
            VK_F11 = 0x7A,
            VK_F12 = 0x7B,
            VK_F13 = 0x7C,
            VK_F14 = 0x7D,
            VK_F15 = 0x7E,
            VK_F16 = 0x7F,
            VK_F17 = 0x80,
            VK_F18 = 0x81,
            VK_F19 = 0x82,
            VK_F20 = 0x83,
            VK_F21 = 0x84,
            VK_F22 = 0x85,
            VK_F23 = 0x86,
            VK_F24 = 0x87,
            //
            VK_NUMLOCK = 0x90,
            VK_SCROLL = 0x91,
            //
            VK_OEM_NEC_EQUAL = 0x92, // '=' key on numpad
            //
            VK_OEM_FJ_JISHO = 0x92, // 'Dictionary' key
            VK_OEM_FJ_MASSHOU = 0x93, // 'Unregister word' key
            VK_OEM_FJ_TOUROKU = 0x94, // 'Register word' key
            VK_OEM_FJ_LOYA = 0x95, // 'Left OYAYUBI' key
            VK_OEM_FJ_ROYA = 0x96, // 'Right OYAYUBI' key
            //
            VK_LSHIFT = 0xA0,
            VK_RSHIFT = 0xA1,
            VK_LCONTROL = 0xA2,
            VK_RCONTROL = 0xA3,
            VK_LMENU = 0xA4,
            VK_RMENU = 0xA5,
            //
            VK_BROWSER_BACK = 0xA6,
            VK_BROWSER_FORWARD = 0xA7,
            VK_BROWSER_REFRESH = 0xA8,
            VK_BROWSER_STOP = 0xA9,
            VK_BROWSER_SEARCH = 0xAA,
            VK_BROWSER_FAVORITES = 0xAB,
            VK_BROWSER_HOME = 0xAC,
            //
            VK_VOLUME_MUTE = 0xAD,
            VK_VOLUME_DOWN = 0xAE,
            VK_VOLUME_UP = 0xAF,
            VK_MEDIA_NEXT_TRACK = 0xB0,
            VK_MEDIA_PREV_TRACK = 0xB1,
            VK_MEDIA_STOP = 0xB2,
            VK_MEDIA_PLAY_PAUSE = 0xB3,
            VK_LAUNCH_MAIL = 0xB4,
            VK_LAUNCH_MEDIA_SELECT = 0xB5,
            VK_LAUNCH_APP1 = 0xB6,
            VK_LAUNCH_APP2 = 0xB7,
            //
            VK_OEM_1 = 0xBA, // ';:' for US
            VK_OEM_PLUS = 0xBB, // '+' any country
            VK_OEM_COMMA = 0xBC, // ',' any country
            VK_OEM_MINUS = 0xBD, // '-' any country
            VK_OEM_PERIOD = 0xBE, // '.' any country
            VK_OEM_2 = 0xBF, // '/?' for US
            VK_OEM_3 = 0xC0, // '`~' for US
            //
            VK_OEM_4 = 0xDB, // '[{' for US
            VK_OEM_5 = 0xDC, // '\|' for US
            VK_OEM_6 = 0xDD, // ']}' for US
            VK_OEM_7 = 0xDE, // ''"' for US
            VK_OEM_8 = 0xDF,
            //
            VK_OEM_AX = 0xE1, // 'AX' key on Japanese AX kbd
            VK_OEM_102 = 0xE2, // "<>" or "\|" on RT 102-key kbd.
            VK_ICO_HELP = 0xE3, // Help key on ICO
            VK_ICO_00 = 0xE4, // 00 key on ICO
            //
            VK_PROCESSKEY = 0xE5,
            //
            VK_ICO_CLEAR = 0xE6,
            //
            VK_PACKET = 0xE7,
            //
            VK_OEM_RESET = 0xE9,
            VK_OEM_JUMP = 0xEA,
            VK_OEM_PA1 = 0xEB,
            VK_OEM_PA2 = 0xEC,
            VK_OEM_PA3 = 0xED,
            VK_OEM_WSCTRL = 0xEE,
            VK_OEM_CUSEL = 0xEF,
            VK_OEM_ATTN = 0xF0,
            VK_OEM_FINISH = 0xF1,
            VK_OEM_COPY = 0xF2,
            VK_OEM_AUTO = 0xF3,
            VK_OEM_ENLW = 0xF4,
            VK_OEM_BACKTAB = 0xF5,
            //
            VK_ATTN = 0xF6,
            VK_CRSEL = 0xF7,
            VK_EXSEL = 0xF8,
            VK_EREOF = 0xF9,
            VK_PLAY = 0xFA,
            VK_ZOOM = 0xFB,
            VK_NONAME = 0xFC,
            VK_PA1 = 0xFD,
            VK_OEM_CLEAR = 0xFE
        }
    }
}
