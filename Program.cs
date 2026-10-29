using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using iFusion.Views;

namespace iFusion
{
    internal static class Program
    {
        private static readonly string MutexName = "iFusion_WorkOrders";

        [STAThread]
        static void Main()
        {
            using (Mutex mutex = new Mutex(false, MutexName, out bool createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Esta aplicación ya esta abierta", "Aplicación en ejecución", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SimulatorLook.Apply(); // misma paleta que WOW Scales 3
                Application.Run(new frmMain());
            }
        }
    }
}
