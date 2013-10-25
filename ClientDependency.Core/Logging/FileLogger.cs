namespace ClientDependency.Core.Logging
{
    using System;
    using System.IO;

    class FileLogger : ILogger
    {
        private StreamWriter logWriter;
        
        public void Init(string logFilePath)
        {
            this.logWriter = new StreamWriter(logFilePath, true) { AutoFlush = true };
        }

        public void Debug(string msg)
        {
            this.Write("DEBUG", msg);
        }

        public void Info(string msg)
        {
            this.Write("INFO", msg);
        }

        public void Warn(string msg)
        {
            this.Write("WARN", msg);
        }

        public void Error(string msg, Exception ex)
        {
            this.Write("ERROR", string.Format("{0} : {1}\n{2}", msg, ex.Message, ex.StackTrace));
        }

        public void Fatal(string msg, Exception ex)
        {
            this.Write("FATAL", string.Format("{0} : {1}\n{2}", msg, ex.Message, ex.StackTrace));
        }

        private void Write(string level, string msg)
        {
            this.logWriter.WriteLine("{0} - {1} - {2}", DateTime.Now.ToString("ddMMyyyy hh:mm:ss"), level, msg);
        }
    }
}
