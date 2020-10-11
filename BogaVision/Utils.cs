using System;
using System.Collections.Generic;
using System.Text;

namespace BogaVision
{
    public static class Utils
    {

        public static void Log(string message, bool error = false)
        {
            
         
            
            string log = $"{DateTime.Now:H:mm:ss.fff} : {message}";
            Console.ForegroundColor = error ? ConsoleColor.Red : ConsoleColor.DarkGreen;
            Console.WriteLine(log);
#if DEBUG

#else
            //TODO write log to file?
#endif

        }

    }
}
