namespace PowerFlow.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            if (args.Contains("--verify-package", StringComparer.OrdinalIgnoreCase))
            {
                InstallerEngine.VerifyEmbeddedPayload();
                return 0;
            }

            if (args.Contains("--uninstall-stage2", StringComparer.OrdinalIgnoreCase))
            {
                var removeUserData = args.Contains("--remove-user-data", StringComparer.OrdinalIgnoreCase);
                var quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);
                InstallerEngine.UninstallAsync(removeUserData).GetAwaiter().GetResult();
                if (!quiet)
                {
                    MessageBox.Show("PowerFlow has been uninstalled. Your PowerFlow settings were preserved unless you explicitly chose to remove them.", "PowerFlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                InstallerEngine.ScheduleSelfDeleteIfTemporary();
                return 0;
            }

            if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            {
                var removeUserData = args.Contains("--remove-user-data", StringComparer.OrdinalIgnoreCase);
                var quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);
                return InstallerEngine.BeginUninstall(removeUserData, quiet);
            }

            if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
            {
                var quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);
                var createDesktopShortcut = !args.Contains("--no-desktop-shortcut", StringComparer.OrdinalIgnoreCase);
                var launchAfterInstall = !args.Contains("--no-launch", StringComparer.OrdinalIgnoreCase);
                InstallerEngine.InstallAsync(createDesktopShortcut, launchAfterInstall).GetAwaiter().GetResult();
                if (!quiet)
                {
                    MessageBox.Show("PowerFlow is installed and ready to use.", "PowerFlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }

            Application.Run(new SetupForm());
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PowerFlow Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
