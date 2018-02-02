using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace logaroo_ai
{
    [RunInstaller(true)]
    public class LogFileExportInstaller : Installer
    {
        private readonly string _displayName = "Log File Export Service";
        private readonly string _serviceName = "LogFileExportService";
        private readonly string _description = "Exports log files to Application Insights";

        public LogFileExportInstaller()
        {
            var processInstaller = new ServiceProcessInstaller();
            var serviceInstaller = new ServiceInstaller();

            //set the privileges
            processInstaller.Account = ServiceAccount.LocalSystem;

            serviceInstaller.DisplayName = _displayName;
            serviceInstaller.StartType = ServiceStartMode.Automatic;

            //must be the same as what was set in Program's constructor
            serviceInstaller.ServiceName = _serviceName;
            serviceInstaller.Description = _description;

            this.Installers.Add(processInstaller);
            this.Installers.Add(serviceInstaller);
        }
    }
}
