using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
            string fswPath = ConfigurationManager.AppSettings["FileSystemWatcherFolder"].ToString();
            string fswFilter = ConfigurationManager.AppSettings["FileSystemWatcherFilter"].ToString();

            TelemetryClient tc = new TelemetryClient();
            var props = new Dictionary<string, string> { { "test", "test" } };
            tc.TrackEvent("ConsoleAppStart", props, null);

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

        private void LogFileOnCreated(object source, FileSystemEventArgs e)
        {            
            Microsoft.ApplicationInsights.TelemetryClient tc = new TelemetryClient();
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
