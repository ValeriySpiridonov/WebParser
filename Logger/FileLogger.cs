using System;
using System.IO;
using System.Reflection;

namespace WebParser.Logger
{
    public class FileLogger : ILogger
    {
        private readonly string _logFilePath;

        public FileLogger()
        {
            string baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(baseDirectory))
                baseDirectory = AppContext.BaseDirectory;

            _logFilePath = Path.Combine(baseDirectory, "log.txt");
        }

        public void Log(ILogMessage logMessage)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(_logFilePath, true))
                {
                    sw.WriteLine($"{DateTime.Now}: {logMessage.Message}");
                    if (logMessage.Exception != null)
                    {
                        sw.WriteLine($"Exception: {logMessage.Exception}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Не удалось записать в лог: {ex}");
            }
        }
    }
}