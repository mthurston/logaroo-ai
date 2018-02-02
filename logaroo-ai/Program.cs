using System.Reflection;
using System.ServiceProcess;

namespace logaroo_ai
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.  Which will self-install based upon arguments. See also a permalink to SO post where this approach is derived from.
        /// </summary>
        /// <example>
        /// 
        /// </example>
        /// 
        /// <see cref="https://stackoverflow.com/a/4961380/4846648"/>
        static void Main(string[] args)
        {
            //self-installation. 
            if (args.Length > 0)
            {
                //Install service
                if (args[0].Trim().ToLower() == "/i")
                {
                    System.Configuration.Install.ManagedInstallerClass.InstallHelper(new string[] { "/i", Assembly.GetExecutingAssembly().Location });
                }

                //Uninstall service                 
                else if (args[0].Trim().ToLower() == "/u")
                {
                    System.Configuration.Install.ManagedInstallerClass.InstallHelper(new string[] { "/u", Assembly.GetExecutingAssembly().Location });
                }
            }
            else
            {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new LogFileExportService()
                };
                ServiceBase.Run(ServicesToRun);
            }
        }
    }
}
