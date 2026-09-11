using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DatabaseGenerator
{
    public class Logger
    {
        private static object _multiThreadLock = new object();
        private static StreamWriter _streamWriter;
        private static DateTime _previousDT = DateTime.UtcNow;

        public static void Init(string fileName)
        {
            lock (_multiThreadLock)
            {
                _streamWriter?.Dispose();
                _streamWriter = new StreamWriter(fileName) { AutoFlush = true };
                _previousDT = DateTime.UtcNow;
            }
        }

        public static void Close()
        {
            lock (_multiThreadLock)
            {
                _streamWriter?.Dispose();
                _streamWriter = null;
            }
        }

        public static void Info(string msg)
        {
            lock (_multiThreadLock)
            {
                double elapsed = DateTime.UtcNow.Subtract(_previousDT).TotalSeconds;
                string txt = $"{DateTime.UtcNow.ToString("yyyyMMdd HH:mm:ss")} | {elapsed.ToString("0.00")} > {msg}";
                Console.WriteLine(txt);
                _streamWriter.WriteLine(txt);
                _streamWriter.Flush();
                _previousDT = DateTime.UtcNow;
            }
        }
    }
}
