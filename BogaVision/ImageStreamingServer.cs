using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text;


namespace BogaVision
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


        public void StartTheShow()
        {
            //Load settings
            Utils.Log("Loading settings");
            Settings = ServerSettings.LoadSettings("ServerSettings.xml");

            ClientConnections = new ConcurrentDictionary<Guid, Socket>();

            WindowWatcherTimer = new Timer(200);
            WindowWatcherTimer.Elapsed += WindowWatcherTimer_Elapsed;
            WindowWatcherTimer.Enabled = false;

            ConnectionWatcherTimer = new Timer(250);
            ConnectionWatcherTimer.Elapsed += ConnectionWatcherTimer_Elapsed;
            ConnectionWatcherTimer.Enabled = true;

            ConnectionWatcherTimer.Start();

            //Start watching for connection in a new thread
            Task.Factory.StartNew(() => this.ServerThread(), TaskCreationOptions.LongRunning);
        }


        public void ServerThread()
        {

            try
            {
                Socket Server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                Server.Bind(new IPEndPoint(IPAddress.Any, Settings.Port));
                Server.Listen(10);

                Utils.Log($"Server started on port {Settings.Port}");
                Utils.Log($"Waiting for connections");

                foreach (Socket client in Server.IncommingConnections())
                    System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(ClientThread), client);

            }
            catch (Exception ex)
            {
                Utils.Log($"Error in server thread : {ex.Message}",true);
            }

        }

        private void ClientThread(object client)
        {

            Socket socket = (Socket)client;

            var SocketID = Guid.NewGuid();

            Utils.Log($"Connection created");

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
                Utils.Log($"Connection Closed");

                if (socket == null || !socket.Connected)
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

                    Utils.Log("Start the capturing service and watch for active windows");

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
                        Utils.Log("No active connections, stop capture service and wait...");

                        WindowWatcherTimer?.Stop();
                        WindowWatcherTimer.Enabled = false;
                        CurrentlyCapturing = false;
                        ScreenCaptureService?.StopCapture();
                        ScreenCaptureService = null;
                        CurrentForegroundWindow = IntPtr.Zero;
                        BadHWNDs?.Clear();
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

        private HashSet<IntPtr> BadHWNDs { get; set; } = new HashSet<IntPtr>();
        private bool WindowWatcherWorking { get; set; } = false;
        private void WindowWatcherTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!CurrentlyCapturing) return;
            if (WindowWatcherWorking) return;


            try
            {
                //Diable the timer while its processing (so we don't get multiple triggers)
                WindowWatcherWorking = true;

                //What for issues with the screen capture service
                if (ScreenCaptureService?.NotGettingFrames == true)
                {
                    //Force a restart
                    CurrentForegroundWindow = IntPtr.Zero;
                }

                var hwnd = GetForegroundWindow();

                if (hwnd != CurrentForegroundWindow && hwnd != IntPtr.Zero && !BadHWNDs.Contains(hwnd))
                {
                    if (WindowEnumerationHelper.IsWindowValidForCapture(hwnd))
                    {
                        //make sure the window text is not restricted!

                        if (Settings?.IgnoreWindows?.Any() == true)
                        {
                            var title = GetWindowTitle(hwnd);
                            if (!string.IsNullOrEmpty(title))
                            {
                                if (Settings.IgnoreWindows.Any(t => title.ToLower().Contains(t.ToLower())))
                                {
                                    // Don't want CHAT to see connection codes!
                                    Utils.Log($"Oops we need to ignore this window : {title}");

                                    return;
                                }
                            }
                        }


                        ScreenCaptureService?.StopCapture();
                        ScreenCaptureService = new CaptureService();
                        CurrentForegroundWindow = hwnd;
                        bool itworked = ScreenCaptureService.Start(hwnd);
                        if(itworked == false && !BadHWNDs.Contains(hwnd))
                        {
                            BadHWNDs.Add(hwnd);
                        }
                    }
                }

            }
            catch { }
            finally
            {
                //Re-enable the timer
                WindowWatcherWorking = false;
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
