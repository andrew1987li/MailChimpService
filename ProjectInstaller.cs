using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;
using System.Threading.Tasks;

namespace MailChimpSyncService
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : System.Configuration.Install.Installer
    {
        public ProjectInstaller()
        {
            InitializeComponent();
            BeforeInstall += new InstallEventHandler(BeforeInstallEventHandler);
        }

        private void BeforeInstallEventHandler(object sender, InstallEventArgs e)
        {

            if (!System.Diagnostics.EventLog.SourceExists("MailChimpSyncService"))
            {
                System.Diagnostics.EventLog.CreateEventSource(
                    "MailChimpSyncService", "MailChimpSync_Default");
            }
        }

        private void serviceInstaller1_AfterInstall(object sender, InstallEventArgs e)
        {

        }

        private void serviceInstaller1_BeforeInstall()
        {
            
        }

        private void serviceProcessInstaller1_AfterInstall(object sender, InstallEventArgs e)
        {

        }
    }
}
