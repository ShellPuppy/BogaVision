﻿using System.Linq;
using System.Threading.Tasks;

namespace BogaVision
{
    class Program
    {

        static void Main(string[] args)
        {
            Utils.Log($"BogaVision Started");

            //Only allow one instance of this app to run at a time
            AlreadyRunning();

            var Server = new ImageStreamingServer();

            Task.Factory.StartNew(() => Server.StartTheShow(),TaskCreationOptions.LongRunning);

            //Don't exit - let the server wait for new connections
            while (true)
            {
                System.Threading.Thread.Sleep(5000);
            }
        }


        /// <summary>
        /// Check to see if there is another process with the same name already running and close those instances
        /// </summary>
        /// <returns></returns>
        private static void AlreadyRunning()
        {
            var thisproc = System.Diagnostics.Process.GetCurrentProcess();

            try
            {
                Utils.Log($"Checking for existing instances of BogaVision");

                foreach (var pr in System.Diagnostics.Process.GetProcessesByName(thisproc.ProcessName).Where(p => p.Id != thisproc.Id))
                {
                    Utils.Log($"Closing existing instance : {pr.Id}");
                    pr.Kill();
                }
            }
            catch { }
        }

    }
}
