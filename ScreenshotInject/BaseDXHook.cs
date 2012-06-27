using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using EasyHook;
using System.IO;
using System.Runtime.Remoting;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
//using System.Windows.Media.Imaging;

namespace ScreenshotInject
{
    internal abstract class BaseDXHook: IDXHook
    {
        public BaseDXHook(ScreenshotInterface.ScreenshotInterface ssInterface)
        {
            this.Interface = ssInterface;
        }

        int _processId = 0;
        protected Bitmap bitmap;

        protected int ProcessId
        {
            get
            {
                if (_processId == 0)
                {
                    _processId = RemoteHooking.GetCurrentProcessId();
                }
                return _processId;
            }
        }

        protected virtual string HookName
        {
            get
            {
                return "BaseDXHook";
            }
        }

        protected void DebugMessage(string message)
        {
#if DEBUG
            try
            {
                Interface.OnDebugMessage(this.ProcessId, HookName + ": " + message);
            }
            catch (RemotingException re)
            {
                // Ignore remoting exceptions
            }
#endif
        }

        protected IntPtr[] GetVTblAddresses(IntPtr pointer, int numberOfMethods)
        {
            List<IntPtr> vtblAddresses = new List<IntPtr>();

            IntPtr vTable = Marshal.ReadIntPtr(pointer);
            for (int i = 0; i < numberOfMethods; i++)
                vtblAddresses.Add(Marshal.ReadIntPtr(vTable, i * IntPtr.Size)); // using IntPtr.Size allows us to support both 32 and 64-bit processes

            return vtblAddresses.ToArray();
        }

        protected static void CopyStream(Stream input, Stream output)
        {
            int bufferSize = 32768;
            byte[] buffer = new byte[bufferSize];
            while (true)
            {
                int read = input.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    return;
                }
                output.Write(buffer, 0, read);
            }
        }

        /// <summary>
        /// Reads data from a stream until the end is reached. The
        /// data is returned as a byte array. An IOException is
        /// thrown if any of the underlying IO calls fail.
        /// </summary>
        /// <param name="stream">The stream to read data from</param>
        protected static byte[] ReadFullStream(Stream stream)
        {
            if (stream is MemoryStream)
            {
                stream.Position = 0;
                return ((MemoryStream)stream).ToArray();
            }
            else
            {
                byte[] buffer = new byte[32768];
                using (MemoryStream ms = new MemoryStream())
                {
                    while (true)
                    {
                        int read = stream.Read(buffer, 0, buffer.Length);
                        if (read > 0)
                            ms.Write(buffer, 0, read);
                        if (read < buffer.Length)
                            return ms.ToArray();
                    }
                }
            }
        }

        protected void SendResponse(Stream stream, Guid requestId)
        {
            SendResponse(ReadFullStream(stream), requestId);
        }

        protected void SendResponse(byte[] bitmapData, Guid requestId)
        {
            try
            {
                // Send the buffer back to the host process
                Interface.OnScreenshotResponse(RemoteHooking.GetCurrentProcessId(), requestId, bitmapData);
            }
            catch (RemotingException re)
            {
                // Ignore remoting exceptions
                // .NET Remoting will throw an exception if the host application is unreachable
            }
        }

        protected void SaveFile()
        {
            Thread t = new Thread(new ParameterizedThreadStart(SaveFileInt));
            t.Start(new ScreenshotInterface.ScreenshotRequest(Request.FileName, Request.Format));
        }


        protected string PrepareFile(String fileName)
        {
            String file = String.Format(fileName,
                    Process.GetCurrentProcess().ProcessName,
                    Process.GetCurrentProcess().StartTime,
                    DateTime.Now);

            String dir = Path.GetDirectoryName(file);

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return file;
        }

        private void SaveFileInt(object param)
        {
            try
            {

                ScreenshotInterface.ScreenshotRequest r = (ScreenshotInterface.ScreenshotRequest)param;

                String file = String.Format(r.FileName, 
                    Process.GetCurrentProcess().ProcessName, 
                    Process.GetCurrentProcess().StartTime, 
                    DateTime.Now);
                
                String dir = Path.GetDirectoryName(file);

                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }


                DebugMessage("Saving " + r.Format.ToString() + " in " + file);
                bitmap.Save(file, GetImageFormat(r.Format));

            }
            catch {
                DebugMessage("Save failed!");
            }

        }

        protected ImageFormat GetImageFormat(String format)
        {
            switch (format)
            {
                case "Jpeg": return ImageFormat.Jpeg;
                case "Png": return ImageFormat.Png;
                case "Bmp": return ImageFormat.Bmp;
            }
            return ImageFormat.Jpeg;
        }


        // Used in the overlay
        DateTime? _lastRequestTime;

        protected DateTime? LastRequestTime
        {
            get {  return _lastRequestTime; }
            set { _lastRequestTime = value; }
        }

        DateTime _lastFPSUpdate;
        int _countFrames;
        int _FPS;
        //List<double> totals = new List<double>();
        public int FPS
        {
            get { return _FPS; }
        }

        protected int UpdateFPS()
        {       
            _countFrames++;

            if (_lastFPSUpdate != null)
            {
                //totals.Add((DateTime.Now - _lastFPSUpdate).TotalMilliseconds);

                if((DateTime.Now - _lastFPSUpdate).TotalMilliseconds > 1000)
                {
                    _FPS= (int)Math.Round(1000.0 * _countFrames / (DateTime.Now - _lastFPSUpdate).TotalMilliseconds); 
                    _countFrames=0;
                    _lastFPSUpdate = DateTime.Now;
                    //String t = "totals: \n";

                    //foreach (double d in totals)
                    //{
                    //    t += d + " \n";
                    //}
                    //DebugMessage(t);

                    //totals.Clear();
                    return _FPS;
                }
            }
            else
            {
                
                _lastFPSUpdate = DateTime.Now;
            }
            return 0;
        }

        DateTime? _lastAutoScreenshotTime;
        bool autoScreenshot = false;
        int autoInterval = 0;
        ScreenshotInterface.ScreenshotRequest autoRequest = null;
        
        protected void CheckAuto()
        {
            if (Request != null)
            {
                if (Request.SetInterval)
                {
                    if (autoScreenshot)
                    {
                        autoInterval = 0;
                        _lastAutoScreenshotTime = null;
                        autoRequest = null;
                        autoScreenshot = false;
                        Request = null;
                        
                    }
                    else if (Request.Interval > 0)
                    {
                        autoInterval = Request.Interval;
                        autoRequest = Request;
                        autoScreenshot = true;
                        _lastAutoScreenshotTime = DateTime.Now;
                        
                    }
                }
            }
            if (Request == null && autoScreenshot)
            {
                if ((DateTime.Now - _lastAutoScreenshotTime.Value).TotalSeconds > autoInterval)
                {
                    Request = autoRequest;
                    _lastAutoScreenshotTime = DateTime.Now;
                }
            }

        }

        protected void DrawOverlay(Object dev)
        {
            int alpha = 0;
            
            if (_lastRequestTime != null)
            {
                TimeSpan timeSinceRequest = DateTime.Now - _lastRequestTime.Value;
                alpha = (int)(255 * (3000.0 - timeSinceRequest.TotalMilliseconds) / 3000.0);
                if (alpha < 0)
                {
                    alpha = 0;
                }
            }
            String text = FPS + "fps " + DateTime.Now.ToString("HH:mm:ss");

            if (autoScreenshot)
            {
                int nextAuto = (int)Math.Round(autoInterval - (DateTime.Now - _lastAutoScreenshotTime.Value).TotalSeconds);

                text += " I" + nextAuto;
            }

            DrawText(dev, text,
                "Screenshot saved", alpha);
        }

        protected virtual  void DrawText(Object device, String text, String text2 = null, int alpha = 0)
        {

        }

        #region IDXHook Members

        public ScreenshotInterface.ScreenshotInterface Interface
        {
            get;
            set;
        }

        public bool ShowOverlay
        {
            get;
            set;
        }

        private ScreenshotInterface.ScreenshotRequest _request;
        public ScreenshotInterface.ScreenshotRequest Request
        {
            get { return _request; }
            set { _request = value;  }
        }

        public abstract void Hook();

        public abstract void Cleanup();

        #endregion

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        protected static extern void CopyMemory(IntPtr Destination, IntPtr Source, uint Length);

    }
}
