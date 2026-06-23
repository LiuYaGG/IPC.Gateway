/*----------------------------------------------------------------
* 项目名称 ：IPC
* 项目描述 ：
* 类 名 称 ：IpcLogService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace IPC
{
    
    
    
    
    
    
    
    
    
    internal static class IpcLogService
    {
        private static readonly object SyncRoot = new object();
        private static int _retentionDays = 7;
        private static DateTime _lastCleanupDate = DateTime.MinValue;

        public static void ConfigureRetentionDays(int retentionDays)
        {
            lock (SyncRoot)
            {
                _retentionDays = ClampRetentionDays(retentionDays);
            }
        }

        public static int ClampRetentionDays(int retentionDays)
        {
            if (retentionDays < 1)
                return 7;
            if (retentionDays > 3650)
                return 3650;
            return retentionDays;
        }

        public static void WriteInfo(string message)
        {
            Write("app", "INFO", message);
        }

        public static void WriteWarning(string message)
        {
            Write("app", "WARN", message);
        }

        public static void WriteError(string message)
        {
            Write("app", "ERROR", message);
        }

        public static void WriteError(string message, Exception? exception)
        {
            Write("app", "ERROR", message + BuildExceptionText(exception));
        }

        public static void WriteAudit(string action, string target, string detail)
        {
            string message = "action=" + Safe(action) + " target=" + Safe(target) + " detail=" + Safe(detail);
            Write("audit", "AUDIT", message);
        }

        public static void Cleanup()
        {
            lock (SyncRoot)
            {
                CleanupCore();
            }
        }

        private static void Write(string category, string level, string message)
        {
            lock (SyncRoot)
            {
                CleanupCore();

                string directory = GetLogDirectoryPath();
                Directory.CreateDirectory(directory);

                string path = Path.Combine(directory, category + "-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                              " [" + level + "] " + Safe(message) + Environment.NewLine;
                File.AppendAllText(path, line, new UTF8Encoding(true));
            }
        }

        private static void CleanupCore()
        {
            DateTime today = DateTime.Today;
            if (_lastCleanupDate == today)
                return;

            _lastCleanupDate = today;
            string directory = GetLogDirectoryPath();
            if (!Directory.Exists(directory))
                return;

            DateTime threshold = today.AddDays(-_retentionDays);
            string[] files = Directory.GetFiles(directory, "*.log", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                try
                {
                    DateTime lastWriteTime = File.GetLastWriteTime(file);
                    if (lastWriteTime < threshold)
                        File.Delete(file);
                }
                catch
                {
                }
            }
        }

        private static string GetLogDirectoryPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Logs");
        }

        private static string BuildExceptionText(Exception? exception)
        {
            if (exception == null)
                return string.Empty;

            StringBuilder builder = new StringBuilder();
            Exception? current = exception;
            while (current != null)
            {
                if (builder.Length > 0)
                    builder.Append(" | ");
                builder.Append(current.GetType().Name);
                builder.Append(": ");
                builder.Append(Safe(current.Message));
                current = current.InnerException;
            }
            return " " + builder.ToString();
        }

        private static string Safe(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
