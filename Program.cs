using System;
using System.Windows.Forms;

namespace RansomKitV2
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                LaunchMode mode = LaunchMode.Normal;
                if (args.Length > 0)
                    mode = args[0].ToLower() switch
                    {
                        "--silent" => LaunchMode.Silent,
                        "--decrypt" => LaunchMode.ManualDecrypt,
                        _ => LaunchMode.Normal
                    };

                Application.Run(new MainForm(mode));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Startup error:\n{ex.Message}\n\n{ex.StackTrace}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal enum LaunchMode { Normal, Silent, ManualDecrypt }
}