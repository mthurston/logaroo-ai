using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;

namespace logaroo_ai
{
    public partial class LogFileExportService : ServiceBase
    {
        private static readonly Regex threadNumberRegex = new Regex(@"\[[0-9]+]");

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
            fsw.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            fsw.Filter = fswFilter;
            fsw.Created += new FileSystemEventHandler(LogFileOnCreated);
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
        private void LogFileOnCreated(object source, FileSystemEventArgs e)
        {
            TelemetryClient tc = new TelemetryClient();
            var lines = File.ReadAllLines($"{e.FullPath}.1").ToList();

            StringBuilder sb = new StringBuilder();
            SeverityLevel currentSevLevel = SeverityLevel.Verbose;

            for (int i = 0; i < lines.Count; i++)
            {
                try
                {
                    var splits = lines[i].Split(' ');

                    // if third split matches thread number regex it's most likely the first line of a log message
                    if (sb.Length > 0 && splits.Length >= 3 && threadNumberRegex.IsMatch(splits[2]))
                    {
                        //TODO: add timestamp to trace telemetry

                        //send sb                        
                        tc.TrackTrace(sb.ToString(), currentSevLevel);

                        sb.Clear();
                    }

                    // if third split matches thread number regex the fourth split is sev level
                    if (splits.Length >= 4 && threadNumberRegex.IsMatch(splits[2]))
                    {
                        switch (splits[3])
                        {
                            case "INFO":
                                currentSevLevel = SeverityLevel.Information;
                                break;
                            case "WARNING":
                                currentSevLevel = SeverityLevel.Warning;
                                break;
                            case "CRITICAL":
                                currentSevLevel = SeverityLevel.Critical;
                                break;
                            case "ERROR":
                                currentSevLevel = SeverityLevel.Error;
                                break;
                            default:
                                currentSevLevel = SeverityLevel.Verbose;
                                break;
                        }
                    }

                    sb.AppendLine(lines[i]);
                }
                catch (Exception ex)
                {
                    tc.TrackException(ex, new Dictionary<string, string>()
                    {
                        { "Source", "Log Parsing Logic" },
                        { "Log Line Number", i.ToString() ?? "UNKNOWN"},
                        { "Log File Path", e?.FullPath ?? "UNKNOWN" }
                    });
                }
            }
        }
    }
}
