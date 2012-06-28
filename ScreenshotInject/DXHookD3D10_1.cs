using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using EasyHook;
using SlimDX.Direct3D10;
using SlimDX.Direct3D10_1;
using SlimDX.DXGI;
using SlimDX.D3DCompiler;
using SlimDX;
using System.IO;
using System.Threading;
using System.Drawing;
using System.Drawing.Imaging;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;

namespace ScreenshotInject
{
    enum D3D10_1DeviceVTbl : short
    {
        // IUnknown
        QueryInterface = 0,
        AddRef = 1,
        Release = 2,

        // ID3D10Device
        VSSetConstantBuffers = 3,
        PSSetShaderResources = 4,
        PSSetShader = 5,
        PSSetSamplers = 6,
        VSSetShader = 7,
        DrawIndexed = 8,
        Draw = 9,
        PSSetConstantBuffers = 10,
        IASetInputLayout = 11,
        IASetVertexBuffers = 12,
        IASetIndexBuffer = 13,
        DrawIndexedInstanced = 14,
        DrawInstanced = 15,
        GSSetConstantBuffers = 16,
        GSSetShader = 17,
        IASetPrimitiveTopology = 18,
        VSSetShaderResources = 19,
        VSSetSamplers = 20,
        SetPredication = 21,
        GSSetShaderResources = 22,
        GSSetSamplers = 23,
        OMSetRenderTargets = 24,
        OMSetBlendState = 25,
        OMSetDepthStencilState = 26,
        SOSetTargets = 27,
        DrawAuto = 28,
        RSSetState = 29,
        RSSetViewports = 30,
        RSSetScissorRects = 31,
        CopySubresourceRegion = 32,
        CopyResource = 33,
        UpdateSubresource = 34,
        ClearRenderTargetView = 35,
        ClearDepthStencilView = 36,
        GenerateMips = 37,
        ResolveSubresource = 38,
        VSGetConstantBuffers = 39,
        PSGetShaderResources = 40,
        PSGetShader = 41,
        PSGetSamplers = 42,
        VSGetShader = 43,
        PSGetConstantBuffers = 44,
        IAGetInputLayout = 45,
        IAGetVertexBuffers = 46,
        IAGetIndexBuffer = 47,
        GSGetConstantBuffers = 48,
        GSGetShader = 49,
        IAGetPrimitiveTopology = 50,
        VSGetShaderResources = 51,
        VSGetSamplers = 52,
        GetPredication = 53,
        GSGetShaderResources = 54,
        GSGetSamplers = 55,
        OMGetRenderTargets = 56,
        OMGetBlendState = 57,
        OMGetDepthStencilState = 58,
        SOGetTargets = 59,
        RSGetState = 60,
        RSGetViewports = 61,
        RSGetScissorRects = 62,
        GetDeviceRemovedReason = 63,
        SetExceptionMode = 64,
        GetExceptionMode = 65,
        GetPrivateData = 66,
        SetPrivateData = 67,
        SetPrivateDataInterface = 68,
        ClearState = 69,
        Flush = 70,
        CreateBuffer = 71,
        CreateTexture1D = 72,
        CreateTexture2D = 73,
        CreateTexture3D = 74,
        CreateShaderResourceView = 75,
        CreateRenderTargetView = 76,
        CreateDepthStencilView = 77,
        CreateInputLayout = 78,
        CreateVertexShader = 79,
        CreateGeometryShader = 80,
        CreateGemoetryShaderWithStreamOutput = 81,
        CreatePixelShader = 82,
        CreateBlendState = 83,
        CreateDepthStencilState = 84,
        CreateRasterizerState = 85,
        CreateSamplerState = 86,
        CreateQuery = 87,
        CreatePredicate = 88,
        CreateCounter = 89,
        CheckFormatSupport = 90,
        CheckMultisampleQualityLevels = 91,
        CheckCounterInfo = 92,
        CheckCounter = 93,
        GetCreationFlags = 94,
        OpenSharedResource = 95,
        SetTextFilterSize = 96,
        GetTextFilterSize = 97,

        // ID3D10Device1
        CreateShaderResourceView1 = 98,
        CreateBlendState1 = 99,
        GetFeatureLevel = 100,
    }

    /// <summary>
    /// Direct3D 10 Hook - this hooks the SwapChain.Present method to capture images
    /// </summary>
    internal class DXHookD3D10_1: BaseDXHook
    {
        const int D3D10_1_DEVICE_METHOD_COUNT = 101;

        public DXHookD3D10_1(ScreenshotInterface.ScreenshotInterface ssInterface)
            : base(ssInterface)
        {
            this.DebugMessage("Create");
        }

        List<IntPtr> _d3d10_1VTblAddresses = null;
        List<IntPtr> _dxgiSwapChainVTblAddresses = null;

        LocalHook DXGISwapChain_PresentHook = null;
        LocalHook DXGISwapChain_ResizeTargetHook = null;

        protected override string HookName
        {
            get
            {
                return "DXHookD3D10_1";
            }
        }

        public override void Hook()
        {
            this.DebugMessage("Hook: Begin");

            // Determine method addresses in Direct3D10.Device, and DXGI.SwapChain
            if (_d3d10_1VTblAddresses == null)
            {
                _d3d10_1VTblAddresses = new List<IntPtr>();
                _dxgiSwapChainVTblAddresses = new List<IntPtr>();
                this.DebugMessage("Hook: Before device creation");
                using (Factory1 factory = new Factory1())
                {
                    using (var device = new SlimDX.Direct3D10_1.Device1(factory.GetAdapter(0), DriverType.Hardware, SlimDX.Direct3D10.DeviceCreationFlags.None, FeatureLevel.Level_10_1))
                    {
                        this.DebugMessage("Hook: Device created");
                        _d3d10_1VTblAddresses.AddRange(GetVTblAddresses(device.ComPointer, D3D10_1_DEVICE_METHOD_COUNT));

                        using (var renderForm = new SlimDX.Windows.RenderForm())
                        {
                            using (var sc = new SwapChain(factory, device, DXGI.CreateSwapChainDescription(renderForm.Handle)))
                            {
                                _dxgiSwapChainVTblAddresses.AddRange(GetVTblAddresses(sc.ComPointer, DXGI.DXGI_SWAPCHAIN_METHOD_COUNT));
                            }
                        }
                    }
                }
            }

            // We will capture the backbuffer here
            DXGISwapChain_PresentHook = LocalHook.Create(
                _dxgiSwapChainVTblAddresses[(int)DXGI.DXGISwapChainVTbl.Present],
                new DXGISwapChain_PresentDelegate(PresentHook),
                this);

            // We will capture target/window resizes here
            DXGISwapChain_ResizeTargetHook = LocalHook.Create(
                _dxgiSwapChainVTblAddresses[(int)DXGI.DXGISwapChainVTbl.ResizeTarget],
                new DXGISwapChain_ResizeTargetDelegate(ResizeTargetHook),
                this);

            /*
             * Don't forget that all hooks will start deactivated...
             * The following ensures that all threads are intercepted:
             * Note: you must do this for each hook.
             */
            DXGISwapChain_PresentHook.ThreadACL.SetExclusiveACL(new Int32[1]);

            DXGISwapChain_ResizeTargetHook.ThreadACL.SetExclusiveACL(new Int32[1]);
        }

        public override void Cleanup()
        {
            DebugMessage("Cleanup");

            try
            {
                //if (_swapChain != null)
                //{
                //    _swapChain.Dispose();
                //    _swapChain = null;
                //}
                Request = null;
            }
            catch(Exception e)
            {
                DebugMessage(e.ToString());
            }
        }

        /// <summary>
        /// The IDXGISwapChain.Present function definition
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int DXGISwapChain_PresentDelegate(IntPtr swapChainPtr, int syncInterval, /* int */ SlimDX.DXGI.PresentFlags flags);

        /// <summary>
        /// The IDXGISwapChain.ResizeTarget function definition
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int DXGISwapChain_ResizeTargetDelegate(IntPtr swapChainPtr, ref DXGI.DXGI_MODE_DESC newTargetParameters);

        /// <summary>
        /// Hooked to allow resizing a texture/surface that is reused. Currently not in use as we create the texture for each request
        /// to support different sizes each time (as we use DirectX to copy only the region we are after rather than the entire backbuffer)
        /// </summary>
        /// <param name="swapChainPtr"></param>
        /// <param name="newTargetParameters"></param>
        /// <returns></returns>
        int ResizeTargetHook(IntPtr swapChainPtr, ref DXGI.DXGI_MODE_DESC newTargetParameters)
        {
            DebugMessage("ResizeTarget");
            try
            {
                if (_swapChain == null)
                {
                    _swapChain = SlimDX.DXGI.SwapChain.FromPointer(swapChainPtr);
                }
                SwapChain swapChain = _swapChain;
                //using (SlimDX.DXGI.SwapChain swapChain = SlimDX.DXGI.SwapChain.FromPointer(swapChainPtr))
                {
                    // This version creates a new texture for each request so there is nothing to resize.
                    // IF the size of the texture is known each time, we could create it once, and then possibly need to resize it here

                    return swapChain.ResizeTarget(
                        new SlimDX.DXGI.ModeDescription()
                        {
                            Format = newTargetParameters.Format,
                            Height = newTargetParameters.Height,
                            RefreshRate = newTargetParameters.RefreshRate,
                            Scaling = newTargetParameters.Scaling,
                            ScanlineOrdering = newTargetParameters.ScanlineOrdering,
                            Width = newTargetParameters.Width
                        }
                    ).Code;
                }
            }
            catch (Exception ex)
            {
                DebugMessage(ex.ToString());
                return System.Runtime.InteropServices.Marshal.GetHRForException(ex);
            }
            
        }

        DateTime? _lastFrame;
        private SwapChain _swapChain;
        private IntPtr _swapChainPointer;
        private byte[] dd;
        Texture2D textureDest;
        SlimDX.Direct3D10.Font _font;
        FontDescription _fd = new SlimDX.Direct3D10.FontDescription()
        {

            Height = 15,
            FaceName = "Lucida Console",
            IsItalic = false,
            Width = 0,
            MipLevels = 1,
            CharacterSet = SlimDX.Direct3D10.FontCharacterSet.Default,
            Precision = SlimDX.Direct3D10.FontPrecision.Default,
            Quality = SlimDX.Direct3D10.FontQuality.Antialiased,
            PitchAndFamily = FontPitchAndFamily.Default | FontPitchAndFamily.DontCare
        };

        /// <summary>
        /// Our present hook that will grab a copy of the backbuffer when requested. Note: this supports multi-sampling (anti-aliasing)
        /// </summary>
        /// <param name="swapChainPtr"></param>
        /// <param name="syncInterval"></param>
        /// <param name="flags"></param>
        /// <returns>The HRESULT of the original method</returns>
        int PresentHook(IntPtr swapChainPtr, int syncInterval, SlimDX.DXGI.PresentFlags flags)
        {
            try
            {
                if (_swapChain == null)
                {
                    _swapChain = SlimDX.DXGI.SwapChain.FromPointer(swapChainPtr);
                }
                SwapChain swapChain = _swapChain;
                {
                    CheckAuto();
                    UpdateFPS();

                    try
                    {
                        #region Screenshot Request
                        if (this.Request != null)
                        {
                            try
                            {
                                this.DebugMessage("PresentHook: Request Start");
                                DateTime startTime = DateTime.Now;
                                using (Texture2D texture = Texture2D.FromSwapChain<SlimDX.Direct3D10.Texture2D>(swapChain, 0))
                                {
                                    #region Determine region to capture
                                    System.Drawing.Rectangle regionToCapture = new System.Drawing.Rectangle(0, 0, texture.Description.Width, texture.Description.Height);
                                    System.Drawing.Rectangle regionToCaptureDest = new System.Drawing.Rectangle(0, 0, texture.Description.Width, texture.Description.Height);

                                    DebugMessage("from " + regionToCapture.ToString() + " to " + regionToCaptureDest.ToString());

                                    //DebugMessage("Flags: "+flags.ToString());
                                    //if (this.Request.RegionToCapture.Width > 0)
                                    //{
                                    //    regionToCapture = this.Request.RegionToCapture;
                                    //}
                                    #endregion

                                    ////start old
                                    //var theTexture = texture;

                                    //// If texture is multisampled, then we can use ResolveSubresource to copy it into a non-multisampled texture
                                    //Texture2D textureResolved = null;
                                    //if (texture.Description.SampleDescription.Count > 1)
                                    //{
                                    //    this.DebugMessage("PresentHook: resolving multi-sampled texture");
                                    //    // texture is multi-sampled, lets resolve it down to single sample
                                    //    textureResolved = new Texture2D(texture.Device, new Texture2DDescription()
                                    //    {
                                    //        CpuAccessFlags = CpuAccessFlags.None,
                                    //        Format = texture.Description.Format,
                                    //        Height = texture.Description.Height,
                                    //        Usage = ResourceUsage.Default,
                                    //        Width = texture.Description.Width,
                                    //        ArraySize = 1,
                                    //        SampleDescription = new SlimDX.DXGI.SampleDescription(1, 0), // Ensure single sample
                                    //        BindFlags = BindFlags.None,
                                    //        MipLevels = 1,
                                    //        OptionFlags = texture.Description.OptionFlags
                                    //    });
                                    //    // Resolve into textureResolved
                                    //    texture.Device.ResolveSubresource(texture, 0, textureResolved, 0, texture.Description.Format);

                                    //    // Make "theTexture" be the resolved texture
                                    //    theTexture = textureResolved;
                                    //}

                                    //// Create destination texture
                                    //Texture2D textureDest = new Texture2D(texture.Device, new Texture2DDescription()
                                    //{
                                    //    CpuAccessFlags = CpuAccessFlags.None,// CpuAccessFlags.Write | CpuAccessFlags.Read,
                                    //    Format = SlimDX.DXGI.Format.R8G8B8A8_UNorm, // Supports BMP/PNG
                                    //    Height = regionToCapture.Height,
                                    //    Usage = ResourceUsage.Default,// ResourceUsage.Staging,
                                    //    Width = regionToCapture.Width,
                                    //    ArraySize = 1,//texture.Description.ArraySize,
                                    //    SampleDescription = new SlimDX.DXGI.SampleDescription(1, 0),// texture.Description.SampleDescription,
                                    //    BindFlags = BindFlags.None,
                                    //    MipLevels = 1,//texture.Description.MipLevels,
                                    //    OptionFlags = texture.Description.OptionFlags
                                    //});

                                    //// Copy the subresource region, we are dealing with a flat 2D texture with no MipMapping, so 0 is the subresource index
                                    //theTexture.Device.CopySubresourceRegion(theTexture, 0, new ResourceRegion()
                                    //{
                                    //    Top = regionToCapture.Top,
                                    //    Bottom = regionToCapture.Bottom,
                                    //    Left = regionToCapture.Left,
                                    //    Right = regionToCapture.Right,
                                    //    Front = 0,
                                    //    Back = 1 // Must be 1 or only black will be copied
                                    //}, textureDest, 0, 0, 0, 0);
                                    ////end old

                                    var theTexture = texture;
                                    DebugMessage(texture.Description.Format.ToString());
                                    // If texture is multisampled, then we can use ResolveSubresource to copy it into a non-multisampled texture
                                    Texture2D textureResolved = null;
                                    if (texture.Description.SampleDescription.Count > 1)
                                    {
                                        this.DebugMessage("PresentHook: resolving multi-sampled texture");
                                        // texture is multi-sampled, lets resolve it down to single sample
                                        textureResolved = new Texture2D(texture.Device, new Texture2DDescription()
                                        {
                                            CpuAccessFlags = CpuAccessFlags.None,
                                            Format = texture.Description.Format,
                                            Height = texture.Description.Height,
                                            Usage = ResourceUsage.Default,
                                            Width = texture.Description.Width,
                                            ArraySize = 1,
                                            SampleDescription = new SlimDX.DXGI.SampleDescription(1, 0), // Ensure single sample
                                            BindFlags = BindFlags.None,
                                            MipLevels = 1,
                                            OptionFlags = texture.Description.OptionFlags
                                        });
                                        // Resolve into textureResolved
                                        texture.Device.ResolveSubresource(texture, 0, textureResolved, 0, texture.Description.Format);

                                        // Make "theTexture" be the resolved texture
                                        theTexture = textureResolved;
                                    }

                                    //SlimDX.Direct3D10.Device d2 = new SlimDX.Direct3D10_1.Device1(DriverType.Hardware, SlimDX.Direct3D10.DeviceCreationFlags.None, FeatureLevel.Level_10_1);
                    
                                    // Create destination texture
                                    if (textureDest == null)
                                    {
                                        textureDest = new Texture2D(texture.Device, new Texture2DDescription()
                                            {
                                                CpuAccessFlags = CpuAccessFlags.Read,//CpuAccessFlags.Read,
                                                Format = SlimDX.DXGI.Format.R8G8B8A8_UNorm, // Supports BMP/PNG
                                                Height = regionToCaptureDest.Height,
                                                Usage = ResourceUsage.Staging,// ResourceUsage.Default,
                                                Width = regionToCaptureDest.Width,
                                                ArraySize = 1,//texture.Description.ArraySize,
                                                SampleDescription = new SlimDX.DXGI.SampleDescription(1, 0),// texture.Description.SampleDescription,
                                                BindFlags = BindFlags.None,
                                                MipLevels = 1,//texture.Description.MipLevels,
                                                OptionFlags = texture.Description.OptionFlags //| ResourceOptionFlags.GdiCompatible
                                            });
                                    }
                                    

                                    Texture2D theTexture2 = new Texture2D(texture.Device, new Texture2DDescription()
                                    {
                                        CpuAccessFlags = CpuAccessFlags.None,
                                        Format = SlimDX.DXGI.Format.R8G8B8A8_UNorm,//texture.Description.Format,
                                        Height = regionToCaptureDest.Height,
                                        Usage = ResourceUsage.Default,
                                        Width = regionToCaptureDest.Width,
                                        ArraySize = 1,
                                        SampleDescription = new SlimDX.DXGI.SampleDescription(1, 0), // Ensure single sample
                                        BindFlags = BindFlags.None,
                                        MipLevels = 1,
                                        OptionFlags = texture.Description.OptionFlags //| ResourceOptionFlags.GdiCompatible
                                    });

                                    Result r = SlimDX.Direct3D10.Resource.LoadTextureFromTexture(theTexture, theTexture2,
                                        new TextureLoadInformation()
                                        {
                                            SourceRegion = new ResourceRegion()
                                            {
                                                Top = regionToCapture.Top,
                                                Bottom = regionToCapture.Bottom,
                                                Left = regionToCapture.Left,
                                                Right = regionToCapture.Right,
                                                Front = 0,
                                                Back = 1 // Must be 1 or only black will be copied
                                            },
                                            DestinationRegion = new ResourceRegion()
                                            {
                                                Top = regionToCaptureDest.Top,
                                                Bottom = regionToCaptureDest.Bottom,
                                                Left = regionToCaptureDest.Left,
                                                Right = regionToCaptureDest.Right,
                                                Front = 0,
                                                Back = 1 // Must be 1 or only black will be copied
                                            },
                                            ElementCount = 1,
                                            Filter = FilterFlags.Linear,
                                            FirstDestinationElement = 0,
                                            FirstDestinationMip = 0,
                                            FirstSourceElement = 0,
                                            FirstSourceMip = 0,
                                            MipCount = 1,
                                            MipFilter = FilterFlags.Linear
                                        });
                                    DebugMessage("Result: " + r.ToString());
                                    // Copy the subresource region, we are dealing with a flat 2D texture with no MipMapping, so 0 is the subresource index
                                    theTexture.Device.CopySubresourceRegion(theTexture2, 0, new ResourceRegion()
                                    {
                                        Top = regionToCaptureDest.Top,
                                        Bottom = regionToCaptureDest.Bottom,
                                        Left = regionToCaptureDest.Left,
                                        Right = regionToCaptureDest.Right,
                                        Front = 0,
                                        Back = 1 // Must be 1 or only black will be copied
                                    }, textureDest, 0, 0, 0, 0);
                                    theTexture.Device.CopyResource(theTexture, textureDest);

                                    #region copy fucking texture to fucking bitmap and save it


                                    DataRectangle dr = textureDest.Map(0, MapMode.Read, SlimDX.Direct3D10.MapFlags.None);
                                    IntPtr data = dr.Data.DataPointer;

                                    int width = textureDest.Description.Width;
                                    int height = textureDest.Description.Height;


                                    //WriteableBitmap wbmap = new WriteableBitmap(width, height, 300, 300, PixelFormats.Bgra32, null);
                                    //CopyMemory(wbmap.BackBuffer, data, (uint)(width * height * 4));


                                    if (bitmap == null)
                                    {
                                        bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                                        dd = new byte[width * height * 4];
                                    }
                                    Marshal.Copy(data, dd, 0, dd.Length);

                                    textureDest.Unmap(0);

                                    for (int i = 0; i < dd.Length; i += 4)
                                    {
                                        byte tmp = dd[i];
                                        dd[i] = dd[i + 2];
                                        dd[i + 2] = tmp;
                                    }

                                    BitmapData bd = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);

                                    Marshal.Copy(dd, 0, bd.Scan0, dd.Length);
                                    //CopyMemory(bd.Scan0, data, (uint)(width * height * 4));
                                    bitmap.UnlockBits(bd);

                                    //textureDest.Unmap(0);



                                    //FileStream stream = new FileStream(@"c:\temp\new.png", FileMode.Create);
                                    //PngBitmapEncoder encoder = new PngBitmapEncoder();
                                    ////TextBlock myTextBlock = new TextBlock();
                                    ////myTextBlock.Text = "Codec Author is: " + encoder.CodecInfo.Author.ToString();
                                    //encoder.Interlace = PngInterlaceOption.On;
                                    //encoder.Frames.Add(BitmapFrame.Create(wbmap));
                                    //encoder.Save(stream);

                                    SaveFile();
                                    #endregion

                                    //Texture2D.ToFile(textureDest, ImageFileFormat.Png, @"c:\temp\dx10.png");
                                    //Texture2D.ToFile(textureDest, GetImageFileFormat(this.Request.Format), this.Request.FileName);

                                    // Make sure we free up the resolved texture if it was created
                                    if (textureResolved != null)
                                    {
                                        textureResolved.Dispose();
                                        textureResolved = null;
                                    }
                                }



                                this.DebugMessage("PresentHook: Copy BackBuffer time: " + (DateTime.Now - startTime).ToString());
                                this.DebugMessage("PresentHook: Request End");
                            }
                            finally
                            {
                                // Prevent the request from being processed a second time
                                this.Request = null;
                            }

                        }
                        #endregion

                        #region Draw the fucking overlay
                        using (Texture2D texture = Texture2D.FromSwapChain<SlimDX.Direct3D10.Texture2D>(swapChain, 0))
                        {
                            DrawOverlay(texture);
                            //DrawSprite(texture.Device);
                        }
                        #endregion

                        #region Example: Draw overlay (after screenshot so we don't capture overlay as well)
                        //if (this.ShowOverlay)
                        //{
                        //    using (Texture2D texture = Texture2D.FromSwapChain<SlimDX.Direct3D10.Texture2D>(swapChain, 0))
                        //    {
                        //        if (_lastFrame != null)
                        //        {
                        //            FontDescription fd = new SlimDX.Direct3D10.FontDescription()
                        //            {
                        //                Height = 16,
                        //                FaceName = "Times New Roman",
                        //                IsItalic = false,
                        //                Width = 0,
                        //                MipLevels = 1,
                        //                CharacterSet = SlimDX.Direct3D10.FontCharacterSet.Default,
                        //                Precision = SlimDX.Direct3D10.FontPrecision.Default,
                        //                Quality = SlimDX.Direct3D10.FontQuality.Antialiased,
                        //                PitchAndFamily = FontPitchAndFamily.Default | FontPitchAndFamily.DontCare
                        //            };

                        //            using (Font font = new Font(texture.Device, fd))
                        //            {
                        //                DrawText(font, new Vector2(100, 100), String.Format("{0}", DateTime.Now), new Color4(System.Drawing.Color.Red.R, System.Drawing.Color.Red.G, System.Drawing.Color.Red.B, System.Drawing.Color.Red.A));
                        //            }
                        //        }
                        //        _lastFrame = DateTime.Now;
                        //    }
                        //}
                        #endregion
                    }
                    catch (Exception e)
                    {
                        // If there is an error we do not want to crash the hooked application, so swallow the exception
                        this.DebugMessage(e.ToString());
                    }

                    // As always we need to call the original method, note that EasyHook has already repatched the original method
                    // so calling it here will not cause an endless recursion to this function
                    return swapChain.Present(syncInterval, flags).Code;
                }
            }
            catch (Exception ex)
            {
                DebugMessage(ex.ToString());
                return System.Runtime.InteropServices.Marshal.GetHRForException(ex);
            }
        }

        private void DrawText(SlimDX.Direct3D10.Font font, Vector2 pos, string text, Color4 color)
        {
            font.Draw(null, text, new System.Drawing.Rectangle((int)pos.X, (int)pos.Y, 0, 0), SlimDX.Direct3D10.FontDrawFlags.NoClip, color);
        }

        protected override void DrawText(Object dev, String text, String text2 = null, int alpha = 0)
        {
            Texture2D texture = (Texture2D)dev;
            SlimDX.Direct3D10.Device device = texture.Device;

            #region Save BlendState. Maybe can be done with less code?
            BlendStateDescription bsd = device.OutputMerger.BlendState.Description;
            Color4 bf = device.OutputMerger.BlendFactor;
            int bsm=device.OutputMerger.BlendSampleMask;
            int dsr = device.OutputMerger.DepthStencilReference;
            DepthStencilStateDescription dssd = device.OutputMerger.DepthStencilState.Description;
            #endregion

            if (_font == null)
            {
                _sprite = new Sprite(device, 256);
                _font = new SlimDX.Direct3D10.Font(device, _fd);

            }

            _sprite.Begin(SpriteFlags.None);
            _font.Draw(null,
                text,
                new System.Drawing.Rectangle(0, 0, texture.Description.Width, texture.Description.Height),
                FontDrawFlags.Top | FontDrawFlags.Right | FontDrawFlags.NoClip,
                System.Drawing.Color.Red);
            _sprite.End();


            #region Restore Blend State
            device.OutputMerger.BlendState = BlendState.FromDescription(device, bsd);
            device.OutputMerger.BlendSampleMask = bsm;
            device.OutputMerger.BlendFactor = bf;
            device.OutputMerger.DepthStencilReference = dsr;
            device.OutputMerger.DepthStencilState = DepthStencilState.FromDescription(device, dssd);
            #endregion
        }

        ImageFileFormat GetImageFileFormat(String format)
        {
            switch (format)
            {
                case "Jpeg": return ImageFileFormat.Jpg;
                case "Png": return ImageFileFormat.Png;
                case "Bmp": return ImageFileFormat.Bmp;
            }
            return ImageFileFormat.Jpg;
        }

        Sprite _sprite;
        Texture2D _sTexture;
        ShaderResourceView _srv;
        SpriteInstance[] _si = new SpriteInstance[1];
        DateTime _lastSprite = DateTime.Now;
        PixelShader pixelShader;
        String shader = "sampler2D g_samSrcColor;float4 PShader(float4 position : SV_POSITION) : SV_Target{return tex2D(g_samSrcColor,position.xy);}";
        //String shader2 = "sampler2D g_samSrcColor;float4 MyShader( float4 Tex : TEXCOORD0 ) : COLOR0{float4 Color;Color = tex2D( g_samSrcColor, Tex.xy);return Color;}";
        private void DrawSprite(SlimDX.Direct3D10.Device device)
        {
            if (_sprite == null)
            {
                _sprite = new Sprite(device, 1);
                _sTexture = Texture2D.FromFile(
                    device,
                    @"C:\temp\privet.png");
                
                _srv=new ShaderResourceView(device, _sTexture);
                _si[0] = new SpriteInstance(_srv, new Vector2(0, 0), new Vector2(1,1));
                //_sprite.ProjectionTransform = Matrix.OrthoOffCenterLH(0, 800, -600, 0, -10, 10);
                _sprite.ViewTransform = Matrix.Transformation2D(Vector2.Zero, 0.0f, new Vector2(2.0f, 2.0f), Vector2.Zero, 0.0f, Vector2.Zero);


                using (var bytecode = ShaderBytecode.Compile(shader, "PShader", "ps_4_0", ShaderFlags.EnableBackwardsCompatibility, EffectFlags.None))
                    pixelShader = new PixelShader(device, bytecode);

                
            }
            device.PixelShader.Set(pixelShader);
            //if ((DateTime.Now - _lastSprite).TotalMilliseconds > 0)
            //{
            _sprite.Begin(SpriteFlags.None);
            _sprite.DrawImmediate(_si);
            _sprite.End();
            _lastSprite = DateTime.Now;
            //}
        }
    }
}
