using System;
using System.Text;
using System.IO;
using System.Drawing;


namespace BogaVision
{

    /// <summary>
    /// Provides a stream writer that can be used to write images as MJPEG 
    /// or (Motion JPEG) to any stream.
    /// </summary>
    public class MjpegWriter : IDisposable
    {

        private static byte[] CRLF = new byte[] { 13, 10 };
        private static byte[] EmptyLine = new byte[] { 13, 10, 13, 10 };

        //private string _Boundary;

        public MjpegWriter(Stream stream) : this(stream, "--boundary")
        {

        }

        public MjpegWriter(Stream stream, string boundary)
        {

            this.Stream = stream;
            this.Boundary = boundary;
        }

        public string Boundary { get; private set; }
        public Stream Stream { get; private set; }

        public void WriteHeader()
        {
            Write($"HTTP/1.1 200 OK\r\nContent-Type: multipart/x-mixed-replace; boundary={this.Boundary}\r\n");
            this.Stream.Flush();
        }


        public void Write(Stream imageStream)
        {

            StringBuilder sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine(this.Boundary);
            sb.AppendLine("Content-Type: image/jpeg");
            sb.AppendLine($"Content-Length: {imageStream.Length}");
            sb.AppendLine();

            Write(sb.ToString());

            imageStream.Seek(0, SeekOrigin.Begin);
            imageStream.CopyTo(this.Stream);


            Write("\r\n");

            this.Stream.Flush();

        }


        public void WriteJPG(byte[] data)
        {

            StringBuilder sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine(this.Boundary);
            sb.AppendLine("Content-Type: image/jpeg");
            sb.AppendLine($"Content-Length: {data.Length}");
            sb.AppendLine();

            Write(sb.ToString());

            this.Stream.Write(data, 0, data.Length);


            Write("\r\n");

            this.Stream.Flush();

        }


        private void Write(byte[] data)
        {
            this.Stream.Write(data, 0, data.Length);
        }

        private void Write(string text)
        {
            byte[] data = BytesOf(text);
            this.Stream.Write(data, 0, data.Length);
        }

        private static byte[] BytesOf(string text) => Encoding.ASCII.GetBytes(text);

        public string ReadRequest(int length)
        {

            byte[] data = new byte[length];
            int count = this.Stream.Read(data, 0, data.Length);

            if (count != 0)
                return Encoding.ASCII.GetString(data, 0, count);

            return null;
        }

        #region IDisposable Members

        public void Dispose()
        {

            try
            {

                if (this.Stream != null)
                    this.Stream.Dispose();

            }
            finally
            {
                this.Stream = null;
            }
        }

        #endregion
    }
}
