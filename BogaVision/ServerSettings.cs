using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace BogaVision
{
    public class ServerSettings
    {
        /// <summary>
        /// Network Port
        /// </summary>
        public int Port { get; set; }   

        /// <summary>
        /// Number of milliseconds between screen shots
        /// </summary>
        public int Delay { get; set; }

        /// <summary>
        /// Ignore windows that contain this text 
        /// Prevents streaming connection info from teamviewer
        /// </summary>
        public List<string> IgnoreWindows { get; set; }

        public static void SaveSettings(ServerSettings data, string FileName)
        {

            try
            {
                XmlSerializer xs = new XmlSerializer(typeof(ServerSettings));

                using (var sww = new FileStream(FileName, FileMode.OpenOrCreate))
                {
                    xs.Serialize(sww, data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

        }

        public static ServerSettings LoadSettings(string FileName)
        {
            try
            {
                XmlSerializer xs = new XmlSerializer(typeof(ServerSettings));

                using (var sww = new FileStream(FileName, FileMode.Open))
                {
                    return (ServerSettings)xs.Deserialize(sww);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading {0} - {1}", FileName, ex.Message);
            }

            //Return default values if file could not be found
            return new ServerSettings()
            {
                Port = 6969,
                Delay = 50,
                IgnoreWindows = new List<string>() { "teamviewer", "zoho" }
            };
        }

    }



}
