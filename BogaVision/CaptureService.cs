using System;
using System.IO;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Composition.WindowsRuntimeHelpers;
using SharpDX;
using SharpDX.Direct3D11;
using Windows.Storage.Streams;
using SharpDX.WIC;
using SharpDX.DXGI;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using System.Text;

namespace BogaVision
{
    public class CaptureService
    {
        private SizeInt32 lastwindowsize;                   //Remembers the size of the window from the last frame capture - use this to figureout if the size changed
        private GraphicsCaptureItem captureitem;
        private Direct3D11CaptureFramePool framepool;
        private GraphicsCaptureSession capturesession;
        private SharpDX.Direct3D11.Device d3dDevice;
        private IDirect3DDevice device;
        private ImagingFactory factory;
        private DateTime LastTimeIGotAFrame { get; set; }


        private SharpDX.Direct3D11.Multithread _multithread;
        private Object _lockOnMe = new Object(); //Prevent cross thread memory violations with this one simple trick


        public InMemoryRandomAccessStream CurrentFrame { get; set; } = null;

        public void StopCapture()
        {
            try
            {

                //Dispose and clear out anything we created for the capture
                capturesession?.Dispose(); capturesession = null;
                framepool?.Dispose(); framepool = null;
                device?.Dispose(); device = null;
                d3dDevice?.Dispose(); d3dDevice = null;
                captureitem = null;
                CurrentFrame?.Dispose();
                //_multithread?.Dispose(); 
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public bool NotGettingFrames => (DateTime.Now - LastTimeIGotAFrame).TotalSeconds > 1.5;

        //Use this to start capturing a new window
        public void Start(IntPtr HWND)
        {
            try
            {
                //Invalid window handle
                if (HWND == IntPtr.Zero) return;

                ////Can't capture from this window
                //if (!WindowEnumerationHelper.IsWindowValidForCapture(HWND))
                //{
                //    return;
                //}

                var windowitem = CaptureHelper.CreateItemForWindow(HWND);

                if (windowitem == null)
                {
                    Console.WriteLine($"Could not create CaptureItem for : {HWND}");
                    return;
                }

                LastTimeIGotAFrame = DateTime.Now;

                StartCapturing(windowitem);

            }
            catch (Exception ex)
            {
                //TODO Log this error?
                Console.WriteLine($"Error (Start) : {ex.Message}");
            }
        }

        private void StartCapturing(GraphicsCaptureItem item)
        {
            try
            {
                // Stop the previous capture if we had one
                StopCapture();

                Console.WriteLine($"Capturing Window: {item.DisplayName}");

                captureitem = item;
                lastwindowsize = captureitem.Size;
                factory = new ImagingFactory();
                device = Direct3D11Helper.CreateDevice();
                d3dDevice = Direct3D11Helper.CreateSharpDXDevice(device);
                _multithread = d3dDevice.QueryInterface<SharpDX.Direct3D11.Multithread>();
                _multithread.SetMultithreadProtected(true);

                framepool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                   device,
                   DirectXPixelFormat.B8G8R8A8UIntNormalized,
                   2,
                   captureitem.Size);

                //Setup a handler for when a new frame is ready
                framepool.FrameArrived += ProcessFrame;

                captureitem.Closed += (s, a) =>
                {
                    StopCapture();
                };
                if (framepool != null && captureitem != null)
                {
                    capturesession = framepool.CreateCaptureSession(captureitem);

                    capturesession.StartCapture();
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in (StartCapturing) : {ex.Message}");
            }
        }

        private void ProcessFrame(Direct3D11CaptureFramePool fp, object args)
        {
#if DEBUG
            Console.Write("#");
#endif
            try
            {
                using (var frame = fp.TryGetNextFrame())
                {

                    bool needsReset = false;
                    bool recreateDevice = false;

                    if ((frame.ContentSize.Width != lastwindowsize.Width) ||
                        (frame.ContentSize.Height != lastwindowsize.Height))
                    {
                        needsReset = true;
                        lastwindowsize = frame.ContentSize;
                    }

                    try
                    {
                        using (var multithreadLock = new MultithreadLock(_multithread))
                        using (var t = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface))
                        {
                            GetJpgFrame(t);
                        }
                    }
                    catch
                    {
                        needsReset = true;
                        recreateDevice = true;
                    }

                    if (needsReset)
                    {
                        ResetFramePool(frame.ContentSize, recreateDevice);
                    }
                }
            }
            catch { }

        }


        private void GetJpgFrame(Texture2D texture)
        {
            if (texture == null || d3dDevice == null) return;

            Texture2D copy = null;
            Bitmap bmp = null;

            try
            {

                // Create texture copy
                copy = new Texture2D(d3dDevice, new Texture2DDescription
                {
                    Width = texture.Description.Width,
                    Height = texture.Description.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = texture.Description.Format,
                    Usage = ResourceUsage.Staging,
                    SampleDescription = new SampleDescription(1, 0),
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read,
                    OptionFlags = ResourceOptionFlags.None
                });

                // Copy data
                d3dDevice.ImmediateContext.CopyResource(texture, copy);

                var dataBox = d3dDevice.ImmediateContext.MapSubresource(copy, 0, 0, MapMode.Read, MapFlags.None, out DataStream stream);
                var rect = new DataRectangle
                {
                    DataPointer = stream.DataPointer,
                    Pitch = dataBox.RowPitch
                };

                var format = PixelFormat.Format32bppPBGRA;

                bmp = new Bitmap(factory, copy.Description.Width, copy.Description.Height, format, rect);


                lock (_lockOnMe)
                {
                    CurrentFrame?.Dispose(); CurrentFrame = null;
                    CurrentFrame = new InMemoryRandomAccessStream();
                }

                if (CurrentFrame == null) return;

                lock (CurrentFrame)
                {
                    var ms = CurrentFrame.AsStream(); // Do not dispose here
                    using (var wic = new WICStream(factory, ms))
                    using (var encoder = new JpegBitmapEncoder(factory, wic))
                    using (var frame = new BitmapFrameEncode(encoder))
                    {

                        frame.Initialize();
                        frame.SetSize(bmp.Size.Width, bmp.Size.Height);
                        frame.SetPixelFormat(ref format);
                        frame.WriteSource(bmp);
                        frame.Commit();
                        encoder.Commit();
                    }

                }

                d3dDevice?.ImmediateContext.UnmapSubresource(copy, 0);

                LastTimeIGotAFrame = DateTime.Now;
            }
            catch (Exception ex)
            {

                Console.WriteLine($"Failed to GetBitMap : {ex.Message}");
            }
            finally
            {
                if (copy?.IsDisposed == false) copy?.Dispose();
                if (bmp?.IsDisposed == false) bmp?.Dispose();
            }
        }

        private void ResetFramePool(SizeInt32 size, bool recreateDevice)
        {
            if (framepool == null) return;

            do
            {
                try
                {
                    lastwindowsize = size;

                    if (recreateDevice)
                    {
                        //_multithread?.Dispose(); _multithread = null;
                        device?.Dispose(); device = null;
                        d3dDevice.Dispose(); d3dDevice = null;

                        device = Direct3D11Helper.CreateDevice();
                        d3dDevice = Direct3D11Helper.CreateSharpDXDevice(device);

                        //_multithread = d3dDevice.QueryInterface<SharpDX.Direct3D11.Multithread>();
                        //_multithread.SetMultithreadProtected(true);
                    }

                    framepool.Recreate(
                        device,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        size);
                }
                catch
                {
                    device = null;
                    recreateDevice = true;
                }
            } while (device == null);
        }


        #region Multithread Protection

        class MultithreadLock : IDisposable
        {
            public MultithreadLock(SharpDX.Direct3D11.Multithread multithread)
            {
                _multithread = multithread;
                _multithread?.Enter();
            }

            public void Dispose()
            {
                _multithread?.Leave();
                _multithread = null;
            }

            private SharpDX.Direct3D11.Multithread _multithread;
        }

        #endregion


        #region Write Image To Mjpeg Stream
        public bool GiveMeData(Stream stream)
        {
            try
            {
                lock (_lockOnMe)
                {
                    if (CurrentFrame == null) return false;

                    if (CurrentFrame != null)
                    {
                        lock (CurrentFrame)
                        {

                            stream.WriteTimeout = 60000;

                            var ms = CurrentFrame.AsStreamForRead();

                            if (ms.Length <= 100) return false;

                            StringBuilder sb = new StringBuilder();

                            sb.AppendLine();
                            sb.AppendLine("--boundary");
                            sb.AppendLine("Content-Type: image/jpeg");
                            sb.AppendLine($"Content-Length: {ms.Length}");
                            sb.AppendLine();

                            Write(stream, sb.ToString());

                            ms.Seek(0, SeekOrigin.Begin);
                            ms.CopyTo(stream);

                            Write(stream, "\r\n");

                            stream.Flush();

                            return true;
                        }
                    }
                }
            }
            catch
            {

                return false;
            }

            return true;
        }
        private void Write(Stream stream, string text)
        {
            byte[] data = Encoding.ASCII.GetBytes(text);
            stream.Write(data, 0, data.Length);
        }
        #endregion


    }
}
