using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace logaroo_ai
{
    public partial class LogFileExportService : ServiceBase
    {
        private static readonly Regex threadNumberRegex = new Regex(@"\[[0-9]+]");
        private long currentLineNumber = 0;

        public LogFileExportService()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            TelemetryConfiguration.Active.InstrumentationKey = ConfigurationManager.AppSettings["aiKey"].ToString();
            //TODO: multiple path support
            string fswPath = ConfigurationManager.AppSettings["FileSystemWatcherFolder"].ToString();
            //TODO: multiple filter support
            string fswFilter = ConfigurationManager.AppSettings["FileSystemWatcherFilter"].ToString();

            TelemetryClient tc = new TelemetryClient();
            var props = new Dictionary<string, string>
            {
                { "FileSystemWatcherFolder", fswPath },
                { "FileSystemWatcherFilter", fswFilter },
            };

            tc.TrackEvent("OnStart: LogFileExportService has been activated.", props, null);

            FileSystemWatcher fsw = new FileSystemWatcher();
            fsw.Path = fswPath;
            fsw.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            fsw.Filter = fswFilter;
            fsw.Changed += new FileSystemEventHandler(LogFileOnChanged);
            fsw.Renamed += new RenamedEventHandler(LogFileOnRenamed);
            fsw.EnableRaisingEvents = true;
        }

        protected override void OnStop()
        {

        }

        /// <summary>
        /// Parsing logic which consumes entire file and writes it to application insights.  Tightly coupled to default log4net file format.
        /// </summary>
        /// <param name="source">File System Watcher publishing event</param>
        /// <param name="e">The event containing whichever NotifyFilter data present on configured File System Watcher.</param>
        /// <example>
        /// MyLogFile.log
        /// 2017-11-17 07:49:29,768 [40] INFO MyTestService [MyTestService] - Stopping queue handler. [Thread ID: 16]
        /// 2017-11-17 07:49:29,768 [40] INFO MyTestService [MyTestService] - Stopping queue handler. [Thread ID: 9]
        /// 2017-11-21 13:37:46,780 [32] INFO MyTestService [MyTestService] - UniqueID: 0000000-0000-0000-0000-000000000000
        ///        Mode    : SendStart
        ///        Message : UniqueID : 0000000-0000-0000-0000-000000000000
        ///        Path     : FormatName:DIRECT=OS:MyServer\MyQueue
        ///        AdminPath: 
        ///        Label    : 002184724
        ///        -- Data -- 
        ///        NameSpace.MyProject.MyClass
        ///        -- Args --
        ///        
        /// 2017-11-21 13:37:46,733 [32] INFO  MyTestService [MyTestService] - Requesting test from Primary source. [MyTest: 1234]
        /// 
        /// Telemetries Produced:
        /// { 
        ///  Message: "2017-11-17 07:49:29,768 [40] INFO MyTestService [MyTestService] - Stopping queue handler. [Thread ID: 16]",
        ///  SeverityLevel: 1
        /// }
        /// { 
        ///  Message: "2017-11-17 07:49:29,768 [40] INFO MyTestService [MyTestService] - Stopping queue handler. [Thread ID: 9]",
        ///  SeverityLevel: 1
        /// }
        /// { 
        ///  Message: "2017-11-21 13:37:46,780 [32] INFO MyTestService [MyTestService] - UniqueID: 0000000-0000-0000-0000-000000000000
        ///        Mode    : SendStart
        ///        Message : UniqueID : 0000000-0000-0000-0000-000000000000
        ///        Path     : FormatName:DIRECT=OS:MyServer\MyQueue
        ///        AdminPath: 
        ///        Label    : 002184724
        ///        -- Data -- 
        ///        NameSpace.MyProject.MyClass
        ///        -- Args --",
        ///  SeverityLevel: 1
        /// }
        /// { 
        ///  Message: "2017-11-21 13:37:46,733 [32] INFO  MyTestService [MyTestService] - Requesting test from Primary source. [MyTest: 1234]",
        ///  SeverityLevel: 1
        /// }
        /// 
        /// </example>
        /// 
        private void LogFileOnChanged(object source, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Deleted || 
                e.ChangeType == WatcherChangeTypes.Renamed ||
                !File.Exists(e.FullPath))
            {
                ResetFile();
            }
            else
            {
                TelemetryClient tc = new TelemetryClient();
                StringBuilder sb = new StringBuilder();
                SeverityLevel currentSevLevel = SeverityLevel.Verbose;

                try
                {
                    using (var fileStream = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.ReadWrite))
                    using (var reader = new StreamReader(fileStream))
                    {
                        for (int i = 0; i < currentLineNumber && !reader.EndOfStream; i++)
                        {
                            //progress to currentLine
                            reader.ReadLine();
                        }

                        while (!reader.EndOfStream)
                        {
                            string currentLine = reader.ReadLine();
                            currentLineNumber++;

                            try
                            {
                                var splits = currentLine.Split(' ');

                                // if third split matches thread number regex it's most likely the first line of a log message
                                if (sb.Length > 0 && splits.Length >= 3 && threadNumberRegex.IsMatch(splits[2]))
                                {
                                    //send sb                        
                                    tc.TrackTrace(sb.ToString(), currentSevLevel);
                                    sb.Clear();
                                }

                                // if third split matches thread number regex the fourth split is sev level
                                if (splits.Length >= 4 && threadNumberRegex.IsMatch(splits[2]))
                                {
                                    GetSeverityLevel(splits[3]);
                                }

                                sb.AppendLine(currentLine);
                            }
                            catch (Exception ex)
                            {
                                tc.TrackException(ex, new Dictionary<string, string>()
                            {
                                { "Source", "Log Parsing Logic (Inner)" },
                                { "Log Line", currentLine ?? "UNSPECIFIED" },
                                { "Log File Path", e?.FullPath ?? "UNKNOWN" }
                            });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    tc.TrackException(ex, new Dictionary<string, string>()
                {
                    { "Source", "Log Parsing Logic (Outer)" },
                    { "Log File Path", e?.FullPath ?? "UNKNOWN" }
                });
                }
            }            
        }

        private void LogFileOnRenamed(object source, RenamedEventArgs e)
        {
            ResetFile();
        }

        private SeverityLevel GetSeverityLevel(string sevLevel)
        {
            switch (sevLevel)
            {
                case "INFO":
                    return SeverityLevel.Information;
                    break;
                case "WARNING":
                    return SeverityLevel.Warning;
                    break;
                case "CRITICAL":
                    return SeverityLevel.Critical;
                    break;
                case "ERROR":
                    return SeverityLevel.Error;
                    break;
                default:
                    return SeverityLevel.Verbose;
                    break;
            }
        }

        private void ResetFile()
        {
            currentLineNumber = 0;
        }
    }
}
