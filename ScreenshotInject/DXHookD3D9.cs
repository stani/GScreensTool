using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using SlimDX.Direct3D9;
using EasyHook;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SlimDX;


namespace ScreenshotInject
{
    internal class DXHookD3D9: BaseDXHook
    {
        public DXHookD3D9(ScreenshotInterface.ScreenshotInterface ssInterface)
            : base(ssInterface)
        {
        }

        LocalHook Direct3DDevice_EndSceneHook = null;
        LocalHook Direct3DDevice_ResetHook = null;
        LocalHook Direct3DDevice_PresentHook = null;

        object _lockRenderTarget = new object();
        Surface _renderTarget;
        Surface _renderTarget0;
        SlimDX.Direct3D9.Font _font;
        int screenWidth;
        int screenHeight;

        TimeSpan _lastScreenshotTime = new TimeSpan(0);


        //DateTime? _lastAutoScreenshotTime;
        //bool autoScreenshot = false;
        //int autoInterval = 0;
        //ScreenshotInterface.ScreenshotRequest autoRequest = null;

        int ratio = 1;

        Sprite _sprite;
        Texture _sTexture;

        protected override string HookName
        {
            get
            {
                return "DXHookD3D9";
            }
        }

        const int D3D9_DEVICE_METHOD_COUNT = 119;
        public override void Hook()
        {
            this.DebugMessage("Hook: Begin");
            // First we need to determine the function address for IDirect3DDevice9
            Device device;
            List<IntPtr> id3dDeviceFunctionAddresses = new List<IntPtr>();
            this.DebugMessage("Hook: Before device creation");
            using (Direct3D d3d = new Direct3D())
            {
                this.DebugMessage("Hook: Device created");
                using (device = new Device(d3d, 0, DeviceType.NullReference, IntPtr.Zero, CreateFlags.HardwareVertexProcessing, new PresentParameters() { BackBufferWidth = 1, BackBufferHeight = 1 }))
                {
                    id3dDeviceFunctionAddresses.AddRange(GetVTblAddresses(device.ComPointer, D3D9_DEVICE_METHOD_COUNT));
                }
            }

            // We want to hook each method of the IDirect3DDevice9 interface that we are interested in

            // 42 - EndScene (we will retrieve the back buffer here)
            Direct3DDevice_EndSceneHook = LocalHook.Create(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.EndScene],
                // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385, the address is equiv to:
                // (IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x1ce09),
                // A 64-bit app would use 0xff18
                // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
                new Direct3D9Device_EndSceneDelegate(EndSceneHook),
                this);
            Direct3DDevice_EndSceneHook.ThreadACL.SetExclusiveACL(new Int32[1]);

            // 16 - Reset (called on resolution change or windowed/fullscreen change - we will reset some things as well)
            Direct3DDevice_ResetHook = LocalHook.Create(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.Reset],
                // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385, the address is equiv to:
                //(IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x58dda),
                // A 64-bit app would use 0x3b3a0
                // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
                new Direct3D9Device_ResetDelegate(ResetHook),
                this);

            Direct3DDevice_ResetHook.ThreadACL.SetExclusiveACL(new Int32[1]);

            //// 16 - Reset (called on resolution change or windowed/fullscreen change - we will reset some things as well)
            Direct3DDevice_PresentHook = LocalHook.Create(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.Present],
                // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385, the address is equiv to:
                //(IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x58dda),
                // A 64-bit app would use 0x3b3a0
                // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
                new Direct3D9Device_PresentDelegate(PresentHook),
                this);

            Direct3DDevice_PresentHook.ThreadACL.SetExclusiveACL(new Int32[1]);            
            /*
             * Don't forget that all hooks will start deactivated...
             * The following ensures that all threads are intercepted:
             * Note: you must do this for each hook.
             */

            //// 42 - EndScene (we will retrieve the back buffer here)
            //Direct3DDevice_ReleaseHook = LocalHook.Create(
            //    id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.Release],
            //    // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385, the address is equiv to:
            //    // (IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x1ce09),
            //    // A 64-bit app would use 0xff18
            //    // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
            //    new Direct3D9Device_ReleaseDelegate(RelseaseHook),
            //    this);
            //Direct3DDevice_ReleaseHook.ThreadACL.SetExclusiveACL(new Int32[1]);

            Process.GetCurrentProcess();

            this.DebugMessage("Hook: End");
        }

        /// <summary>
        /// Just ensures that the surface we created is cleaned up.
        /// </summary>
        public override void Cleanup()
        {
            DebugMessage("Cleanup");
            try
            {
                lock (_lockRenderTarget)
                {
                    if (_renderTarget != null)
                    {
                        _renderTarget.Dispose();
                        _renderTarget = null;
                    }

                    if (_renderTarget0 != null)
                    {
                        _renderTarget0.Dispose();
                        _renderTarget0 = null;
                    }

                    Request = null;

                    if (bitmap != null)
                    {
                       
                        bitmap.Dispose();
                        bitmap = null;
                    }

                    if (_font != null)
                    {
                        _font.OnLostDevice();
                        _font.Dispose();
                        _font = null;
                    }

                    if (_sprite != null)
                    {
                        _sprite.Dispose();
                        _sprite = null;
                    }

                    screenWidth = screenHeight = 0;
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// The IDirect3DDevice9.EndScene function definition
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_EndSceneDelegate(IntPtr device);

        /// <summary>
        /// The IDirect3DDevice9.Reset function definition
        /// </summary>
        /// <param name="device"></param>
        /// <param name="presentParameters"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_ResetDelegate(IntPtr device, ref D3DPRESENT_PARAMETERS presentParameters);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_PresentDelegate(IntPtr device, RECT src, RECT dst, IntPtr handle, IntPtr regdata);

        bool checkPresent = true;

        int PresentHook(IntPtr devicePtr, RECT src, RECT dst, IntPtr handle, IntPtr regdata)
        {
            try
            {
                //this.DebugMessage("ResetHook: start");
                using (Device device = Device.FromPointer(devicePtr))
                {
                    UpdateFPS();
                    //Region region = new Region(regionData);
                    //DebugMessage("coucou");
                    //Rectangle? srcR = null;
                    //if (src != null)
                    //{
                    //    srcR = new Rectangle(src.left, src.top, src.right - src.left, src.bottom - src.top);
                    //}
                    //Rectangle? dstR = null;
                    //if (dst != null)
                    //{
                    //    dstR = new Rectangle(dst.left, dst.top, dst.right - dst.left, dst.bottom - dst.top);
                    //}

                    //Rectangle srcR = new Rectangle(src.left, src.top, src.right - src.left, src.bottom - src.top);
                    //Rectangle dstR = new Rectangle(dst.left, dst.top, dst.right - dst.left, dst.bottom - dst.top);
                    //return 0;
                    if (checkPresent)
                    {
                        if (src != null)
                        {
                            DebugMessage("src!=null");
                            checkPresent = false;
                        }
                        if (dst != null)
                        {
                            DebugMessage("dst!=null");
                            checkPresent = false;
                        }
                        if (handle != IntPtr.Zero)
                        {
                            DebugMessage("handle!=null");
                            checkPresent = false;
                        }
                        if (regdata != IntPtr.Zero)
                        {
                            DebugMessage("regdata!=null");
                            checkPresent = false;
                        }
                    }
                    return device.Present().Code;
                }
            }
            catch (Exception ex)
            {
                //DebugMessage(ex.ToString());
                return System.Runtime.InteropServices.Marshal.GetHRForException(ex);
            }
        }


        /// <summary>
        /// The IDirect3DDevice9.Reset function definition
        /// </summary>
        /// <param name="device"></param>
        /// <param name="presentParameters"></param>
        /// <returns></returns>
        //[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        //delegate int Direct3D9Device_ReleaseDelegate(IntPtr device);


        //int ReleaseHook(IntPtr devicePtr)
        //{
        //    try
        //    {
        //        //this.DebugMessage("ResetHook: start");
        //        using (Device device = Device.FromPointer(devicePtr))
        //        {
        //            return device.
        //        }
        //    }
        //}


        /// <summary>
        /// Reset the _renderTarget so that we are sure it will have the correct presentation parameters (required to support working across changes to windowed/fullscreen or resolution changes)
        /// </summary>
        /// <param name="devicePtr"></param>
        /// <param name="presentParameters"></param>
        /// <returns></returns>
        int ResetHook(IntPtr devicePtr, ref D3DPRESENT_PARAMETERS presentParameters)
        {
            try
            {
                DebugMessage("ResetHook: start");
                using (Device device = Device.FromPointer(devicePtr))
                {
                    PresentParameters pp = new PresentParameters()
                    {
                        AutoDepthStencilFormat = (Format)presentParameters.AutoDepthStencilFormat,
                        BackBufferCount = presentParameters.BackBufferCount,
                        BackBufferFormat = (Format)presentParameters.BackBufferFormat,
                        BackBufferHeight = presentParameters.BackBufferHeight,
                        BackBufferWidth = presentParameters.BackBufferWidth,
                        DeviceWindowHandle = presentParameters.DeviceWindowHandle,
                        EnableAutoDepthStencil = presentParameters.EnableAutoDepthStencil,
                        FullScreenRefreshRateInHertz = presentParameters.FullScreen_RefreshRateInHz,
                        Multisample = (MultisampleType)presentParameters.MultiSampleType,
                        MultisampleQuality = presentParameters.MultiSampleQuality,
                        PresentationInterval = (PresentInterval)presentParameters.PresentationInterval,
                        PresentFlags = (PresentFlags)presentParameters.Flags,
                        SwapEffect = (SwapEffect)presentParameters.SwapEffect,
                        Windowed = presentParameters.Windowed
                    };

                    String s=
                        "AutoDepthStencilFormat = "+pp.AutoDepthStencilFormat.ToString()+
                        ", BackBufferCount = "+pp.BackBufferCount.ToString()+
                        ", BackBufferFormat = "+pp.BackBufferFormat.ToString()+
                        ", BackBufferHeight = " + pp.BackBufferHeight.ToString() +
                        ", BackBufferWidth = " + pp.BackBufferWidth.ToString() +
                        ", DeviceWindowHandle = " + pp.DeviceWindowHandle.ToString() +
                        ", EnableAutoDepthStencil = "+pp.EnableAutoDepthStencil.ToString()+
                        ", FullScreenRefreshRateInHertz = " + pp.FullScreenRefreshRateInHertz.ToString() +
                        ", Multisample = "+pp.Multisample.ToString()+
                        ", MultisampleQuality = "+pp.MultisampleQuality.ToString()+
                        ", PresentationInterval = "+pp.PresentationInterval.ToString()+
                        ", PresentFlags = "+pp.PresentFlags.ToString()+
                        ", SwapEffect = " + pp.SwapEffect.ToString() +
                        ", Windowed = "+pp.Windowed.ToString();

                    //DebugMessage(s);

                    lock (_lockRenderTarget)
                    {
                        Cleanup();
                    }

                    //this.DebugMessage("ResetHook: end");
                    // EasyHook has already repatched the original Reset so calling it here will not cause an endless recursion to this function
                    return device.Reset(pp).Code;
                }
            }

            catch (Exception ex)
            {
                Cleanup();
                DebugMessage(ex.ToString());
                return System.Runtime.InteropServices.Marshal.GetHRForException(ex); 
            }
        }

       
        /// <summary>
        /// Hook for IDirect3DDevice9.EndScene
        /// </summary>
        /// <param name="devicePtr">Pointer to the IDirect3DDevice9 instance. Note: object member functions always pass "this" as the first parameter.</param>
        /// <returns>The HRESULT of the original EndScene</returns>
        /// <remarks>Remember that this is called many times a second by the Direct3D application - be mindful of memory and performance!</remarks>
        int EndSceneHook(IntPtr devicePtr)
        {
            

            try
            {
                using (Device device = Device.FromPointer(devicePtr))
                {

                    // If you need to capture at a particular frame rate, add logic here decide whether or not to skip the frame
                    try
                    {
                        #region Screenshot Request
                        // Is there a screenshot request? If so lets grab the backbuffer
                        lock (_lockRenderTarget)
                        {

                            CheckAuto();

                            if (Request != null)
                            {
                                //this.DebugMessage("EndSceneHook: got device");
                                LastRequestTime = DateTime.Now;
                                DateTime start = DateTime.Now;
                                try
                                {
                                    // First ensure we have a Surface to the render target data into


                                   

                                    using (Surface backBuffer = device.GetBackBuffer(0, 0))
                                    {

                                        if (_renderTarget == null)
                                        {
                                            // Create offscreen surface to use as copy of render target data
                                            using (SwapChain sc = device.GetSwapChain(0))
                                            {
                                                DebugMessage("Create render target " + sc.PresentParameters.Multisample.ToString());
                                                
                                                int width = sc.PresentParameters.BackBufferWidth/ratio;
                                                int height = sc.PresentParameters.BackBufferHeight/ratio;
                                                _renderTarget = Surface.CreateOffscreenPlain(device, width, height, sc.PresentParameters.BackBufferFormat, Pool.SystemMemory);
                                                _renderTarget0 = Surface.CreateRenderTarget(device, width, height, sc.PresentParameters.BackBufferFormat, MultisampleType.None, 0, false);
                                                DebugMessage(sc.PresentParameters.BackBufferFormat.ToString());
                                                //s = _renderTarget.LockRectangle(LockFlags.ReadOnly).Data;
                                                //int bytes = width * height * 4;
                                                //data = new byte[bytes];
                                                //b = new Bitmap(width, height, 4 * width, PixelFormat.Format32bppRgb, s.DataPointer);
                                                ////d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, b.PixelFormat);
                                                bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);

                                                
                                            }
                                        }
                                        //this.DebugMessage("EndSceneHook: pre GetRenderTargetData");
                                        // Create a super fast copy of the back buffer on our Surface
                                        
                                        copyBuffer(device, backBuffer);

                                        

                                        using (DataStream stream = _renderTarget.LockRectangle(LockFlags.ReadOnly).Data)
                                        {
                                            int width = _renderTarget.Description.Width;
                                            int height = _renderTarget.Description.Height;

                                            //Bitmap bitmap = new Bitmap(width, height, 4 * width, PixelFormat.Format32bppRgb, stream.DataPointer);
                                            
                                                
 
                                                BitmapData bd = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                                                CopyMemory(bd.Scan0, stream.DataPointer, (uint)(width * height * 4));
                                                //System.Runtime.InteropServices.Marshal.Copy(data, 0, d.Scan0, data.Length);
                                                bitmap.UnlockBits(bd);

                                               

                                            //}
                                        }
                                        _renderTarget.UnlockRectangle();
                                       
                                        //using (DataStream s = _renderTarget.LockRectangle(LockFlags.ReadOnly).Data)
                                        //{
                                            
                                            
                                            //CopyMemory(d.Scan0, s.DataPointer, (uint)data.Length);
                                            //s.Read(data, 0, data.Length);
                                            //System.Runtime.InteropServices.Marshal.Copy(data, 0, d.Scan0, data.Length);
                                            //b.UnlockBits(d);
                                            //b.Save(this.Request.FileName, ImageFormat.Jpeg);
                                            //CopyStream(s, @"c:\temp\screenshot6.ttt");

                                        //}
                                        //_renderTarget.UnlockRectangle();

                                        SaveFile();
                                        


                                    }
                                }
                                finally
                                {
                                    // We have completed the request - mark it as null so we do not continue to try to capture the same request
                                    // Note: If you are after high frame rates, consider implementing buffers here to capture more frequently
                                    //         and send back to the host application as needed. The IPC overhead significantly slows down 
                                    //         the whole process if sending frame by frame.
                                    Request = null;
                                }
                                DateTime end = DateTime.Now;
                                this.DebugMessage("EndSceneHook: Capture time: " + (end - start).ToString());

                                _lastScreenshotTime = (end - start);
                            }


                            //UpdateFPS();

                            DrawOverlay(device);

                            //DrawSprite(device);


                            
                        }
                        #endregion

                        
                    }
                    catch (Exception e)
                    {
                        // If there is an error we do not want to crash the hooked application, so swallow the exception
                        this.DebugMessage("EndSceneHook: Exeception: " + e);
                    }


                    // EasyHook has already repatched the original EndScene - so calling EndScene against the SlimDX device will call the original
                    // EndScene and bypass this hook. EasyHook will automatically reinstall the hook after this hook function exits.
                    int code= device.EndScene().Code;


                    return code;
                }
            }

            catch (Exception ex)
            {
                Cleanup();
                DebugMessage(ex.ToString());
                return System.Runtime.InteropServices.Marshal.GetHRForException(ex);
            }
        }

        private DateTime _lastSprite=DateTime.Now;

        private void DrawSprite(Device device)
        {
            if (_sprite == null)
            {
                _sprite = new Sprite(device);
                _sTexture = Texture.FromFile(device, @"C:\temp\fish.png");
            }

            if ((DateTime.Now - _lastSprite).TotalMilliseconds > 1000)
            {
                _sprite.Begin(SpriteFlags.AlphaBlend);
                _sprite.Draw(_sTexture, new Vector3(0, 0, 0), new Vector3(0, 0, 0), Color.White);
                _sprite.End();
                _lastSprite = DateTime.Now;
            }
        }
        private void CopyStream(Stream input, String filename)
        {
            using (Stream output = File.OpenWrite(filename))
            {

                byte[] buffer = new byte[16 * 1024];
                int len;
                while ((len = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, len);
                }
            }
        }

        private void copyBuffer(Device device, Surface bb)
        {
            device.StretchRectangle(bb, _renderTarget0, TextureFilter.Point);
            device.GetRenderTargetData(_renderTarget0, _renderTarget);
        }

        private void SaveFileOld(object param)
        {
            DebugMessage("SaveFile start new");
            String FileName = (String)param;
            int width = _renderTarget.Description.Width;
            int height = _renderTarget.Description.Height;
            DebugMessage(width + "," + height);
            //lock (_lockRenderTarget)
            //{
            using (DataStream stream = _renderTarget.LockRectangle(LockFlags.ReadOnly).Data)
            {
                //Bitmap bitmap = new Bitmap(width, height, 4 * width, PixelFormat.Format32bppRgb, stream.DataPointer);
                using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb))
                {
                    BitmapData bd = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                    CopyMemory(bd.Scan0, stream.DataPointer, (uint)(width * height * 4));
                    //System.Runtime.InteropServices.Marshal.Copy(data, 0, d.Scan0, data.Length);
                    bitmap.UnlockBits(bd);

                    bitmap.Save(FileName, ImageFormat.Jpeg);

                }
            }
            _renderTarget.UnlockRectangle();
            //}
            DebugMessage("SaveFile end new");
        }

        protected override void DrawText(Object dev, String text, String text2=null, int alpha=0)
        {
            Device device = (Device)dev;

            if (_font == null)
            {
                _font = new SlimDX.Direct3D9.Font(device, new System.Drawing.Font("Lucida Console", 10.0f));
                
            }

            if (screenHeight == 0 | screenWidth == 0)
            {
                using (Surface backBuffer = device.GetBackBuffer(0, 0))
                {
                    screenWidth = backBuffer.Description.Width;
                    screenHeight = backBuffer.Description.Height;
                }
            }
            _font.DrawString(null,
                text,
                new Rectangle(0, 0, screenWidth, screenHeight),
                DrawTextFormat.Top | DrawTextFormat.Right,
                System.Drawing.Color.Red);


            if (alpha>0)
            {
                Color c = Color.FromArgb(alpha, System.Drawing.Color.Red);
                _font.DrawString(null,
                    text2,
                    new Rectangle(0, 15, screenWidth, screenHeight-15),
                    DrawTextFormat.Top | DrawTextFormat.Right,
                    c);

            }
        }

        /// <summary>
        /// Copies the _renderTarget surface into a stream and starts a new thread to send the data back to the host process
        /// </summary>
        void ProcessRequest()
        {
            if (Request != null)
            {
                // SlimDX now uses a marshal_as for Rectangle to RECT that correctly does the conversion for us, therefore no need
                // to adjust the region Width/Height to fit the x1,y1,x2,y2 format.
                //Rectangle region = Request.RegionToCapture;

                // Prepare the parameters for RetrieveImageData to be called in a separate thread.
                RetrieveImageDataParams retrieveParams = new RetrieveImageDataParams();

                // After the Stream is created we are now finished with _renderTarget and have our own separate copy of the data,
                // therefore it will now be safe to begin a new thread to complete processing.
                // Note: RetrieveImageData will take care of closing the stream.
                // Note 2: Surface.ToStream is the slowest part of the screen capture process - the other methods
                //         available to us at this point are _renderTarget.GetDC(), and _renderTarget.LockRectangle/UnlockRectangle
                //if (Request.RegionToCapture.Width == 0)
                //{
                //    // The width is 0 so lets grab the entire window
                // retrieveParams.Stream = Surface.ToStream(_renderTarget, ImageFileFormat.Bmp);
                //}
                //else if (Request.RegionToCapture.Height > 0)
                //{
                //    retrieveParams.Stream = Surface.ToStream(_renderTarget, ImageFileFormat.Bmp, region);
                //}

                //if (retrieveParams.Stream != null)
                //{
                    // _screenshotRequest will most probably be null by the time RetrieveImageData is executed 
                    // in a new thread, therefore we must provide the RequestId separately.
                    retrieveParams.RequestId = Request.RequestId;
                    retrieveParams.FileName = Request.FileName;

                    // Begin a new thread to process the image data and send the request result back to the host application
                    Thread t = new Thread(new ParameterizedThreadStart(RetrieveImageData));
                    t.Start(retrieveParams);
                //}
            }
        }

        /// <summary>
        /// Used to hold the parameters to be passed to RetrieveImageData
        /// </summary>
        struct RetrieveImageDataParams
        {
            internal Stream Stream;
            internal Guid RequestId;
            internal String FileName;
        }

        /// <summary>
        /// ParameterizedThreadStart method that places the image data from the stream into a byte array and then sets the Interface screenshot response. This can be called asynchronously.
        /// </summary>
        /// <param name="param">An instance of RetrieveImageDataParams is required to be passed as the parameter.</param>
        /// <remarks>The stream object passed will be closed!</remarks>
        void RetrieveImageData(object param)
        {
            RetrieveImageDataParams retrieveParams = (RetrieveImageDataParams)param;
            try
            {

                SendResponse(retrieveParams.Stream, retrieveParams.RequestId);
            }
            finally
            {
                //retrieveParams.Stream.Close();
            }
        }


        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr hdc, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, Int32 dwRop);



    }
}
