using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace MailChimpSyncService
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main(string[] args)
        {

            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new MailChimpSync()
            };

            if (Environment.UserInteractive)
            {
                var service1 = new MailChimpSync();
                service1.TestStartupAndStop(args);
            }
            else
            {
                ServiceBase.Run(ServicesToRun);
            }
           
        }
    }
}
