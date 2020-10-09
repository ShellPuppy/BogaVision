using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using FrameServer;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text;


namespace BogaServer
{


    public class ImageStreamingServer
    {
        #region win apis

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetWindowTextLength(IntPtr hWnd);
        #endregion

        private ServerSettings Settings { get; set; }

        private ConcurrentDictionary<Guid, Socket> ClientConnections { get; set; }

        private CaptureService ScreenCaptureService { get; set; }

        private Timer WindowWatcherTimer { get; set; } = null;
        private Timer ConnectionWatcherTimer { get; set; } = null;

        private bool CurrentlyCapturing { get; set; } = false;
        private IntPtr CurrentForegroundWindow { get; set; } = IntPtr.Zero;

        private bool AnyClients => ClientConnections?.Values.Any(s => s.Connected) == true;

        public ImageStreamingServer()
        {
            CurrentForegroundWindow = GetForegroundWindow();
        }

        public void StartTheShow()
        {
            //Load settings
            Settings = ServerSettings.LoadSettings("ServerSettings.xml");

            ClientConnections = new ConcurrentDictionary<Guid, Socket>();

            var servertask = Task.Factory.StartNew(() => this.ServerThread(), TaskCreationOptions.LongRunning);

            WindowWatcherTimer = new Timer(200);
            WindowWatcherTimer.Elapsed += WindowWatcherTimer_Elapsed;
            WindowWatcherTimer.Enabled = false;

            ConnectionWatcherTimer = new Timer(250);
            ConnectionWatcherTimer.Elapsed += ConnectionWatcherTimer_Elapsed;
            ConnectionWatcherTimer.Enabled = true;

            ConnectionWatcherTimer.Start();
        }



        public void ServerThread()
        {

            try
            {
                Socket Server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                Server.Bind(new IPEndPoint(IPAddress.Any, Settings.Port));
                Server.Listen(10);

                System.Diagnostics.Debug.WriteLine(string.Format("Server started on port {0}.", Settings.Port));


                foreach (Socket client in Server.IncommingConnections())
                    System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(ClientThread), client);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

        }

        private void ClientThread(object client)
        {

            Socket socket = (Socket)client;

            var SocketID = Guid.NewGuid();

            Console.WriteLine($"Connection Open {SocketID}");

            ClientConnections.TryAdd(SocketID, socket);

            try
            {
                using (MjpegWriter wr = new MjpegWriter(new NetworkStream(socket, true)))
                {

                    socket.LingerState = new LingerOption(true, 10);
                    socket.NoDelay = true;

                    socket.SendBufferSize = 8192;

                    socket.DontFragment = true;
                    
                    wr.WriteHeader();

                    while (socket.Connected)
                    {

                        ScreenCaptureService?.GiveMeData(wr.Stream);
                        System.Threading.Thread.Sleep(Settings.Delay); //Rate limiting
                    }

                }
            }
            catch 
            {
                //Exception is thrown when the connection is closed
            }
            finally
            {
                //Stop tracking this socket
                Console.WriteLine($"Connection Closed {SocketID}");
                if(socket == null || !socket.Connected )
                ClientConnections.TryRemove(SocketID, out Socket _);
            }
        }

        private bool ImHandlingAWatchEvent { get; set; } = false;

        private void ConnectionWatcherTimer_Elapsed(object sender, ElapsedEventArgs e)
        {

            if (ImHandlingAWatchEvent) return;

            try
            {
                ImHandlingAWatchEvent = true;
                ConnectionWatcherTimer.Enabled = false;

                if (AnyClients)
                {
                    if (CurrentlyCapturing) return; //We have clients watching and we are capturing....good!

                    //Start capturing
                    CurrentlyCapturing = true;
                    CurrentForegroundWindow = IntPtr.Zero;
                    WindowWatcherTimer.Enabled = true;
                    WindowWatcherTimer.Start();
                }
                else
                {
                    if (CurrentlyCapturing)
                    {
                        WindowWatcherTimer?.Stop();
                        WindowWatcherTimer.Enabled = false;
                        CurrentlyCapturing = false;
                        ScreenCaptureService?.StopCapture();
                        ScreenCaptureService = null;
                        CurrentForegroundWindow = IntPtr.Zero;
                    }
                }

            }
            catch { }
            finally
            {
                ImHandlingAWatchEvent = false;
                ConnectionWatcherTimer.Enabled = true;
            }
        }

        public static string GetWindowTitle(IntPtr hWnd)
        {

            try
            {
                // Allocate correct string length first
                int length = GetWindowTextLength(hWnd);
                StringBuilder sb = new StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                return sb.ToString().ToLower();
            }
            catch { }
            return string.Empty;

        }


        private bool ImHandlingAWindowEvent { get; set; } = false;
        private void WindowWatcherTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!CurrentlyCapturing || ImHandlingAWindowEvent) return;

            try
            {
                ImHandlingAWindowEvent = true;
                if (ScreenCaptureService != null && ScreenCaptureService.NotGettingFrames)
                {
                    //Force a restart
                    CurrentForegroundWindow = IntPtr.Zero;
                }
            

                var hwnd = GetForegroundWindow();
                if (hwnd != CurrentForegroundWindow && hwnd != IntPtr.Zero)
                {
                    //make sure the window text is not restricted!

                    if (Settings?.IgnoreWindows?.Any() == true)
                    {
                        var title = GetWindowTitle(hwnd);
                        if (!string.IsNullOrEmpty(title))
                        {
                            if (Settings.IgnoreWindows.Any(t => t.ToLower().Contains(title)))
                            {
                                // Don't want CHAT to see connection codes!
                                return;
                            }
                        }
                    }


                    ScreenCaptureService?.StopCapture();
                    ScreenCaptureService = new CaptureService() { EnableFrameGrabbing = true };
                    CurrentForegroundWindow = hwnd;
                    ScreenCaptureService.Start(hwnd);
                }
   

            }
            catch { }
            finally
            {
                ImHandlingAWindowEvent = false;
               // WindowWatcherTimer.Enabled = true;
            }

        }

    }

    static class SocketExtensions
    {

        public static IEnumerable<Socket> IncommingConnections(this Socket server)
        {
            while (true)
            {
                yield return server.Accept();
            }

        }

    }


}
