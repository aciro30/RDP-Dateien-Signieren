using System.Text;

namespace RdpSignTool.WinForms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (TryRunAdministrativeCommand(args, out var commandExitCode))
        {
            return commandExitCode;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowFatalError("UI-Thread", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            ShowFatalError("AppDomain", args.ExceptionObject as Exception ?? new Exception("Unbekannter Fehler."));
        };

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception exception)
        {
            ShowFatalError("Main", exception);
            return 1;
        }
    }

    private static bool TryRunAdministrativeCommand(string[] args, out int exitCode)
    {
        exitCode = 0;

        if (args.Length != 2 || !string.Equals(args[0], "--registry-warning-suppression", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var service = new RdpSigningService();
            if (string.Equals(args[1], "enable", StringComparison.OrdinalIgnoreCase))
            {
                service.EnableRedirectionWarningSuppression();
                return true;
            }

            if (string.Equals(args[1], "disable", StringComparison.OrdinalIgnoreCase))
            {
                service.DisableRedirectionWarningSuppression();
                return true;
            }

            throw new InvalidOperationException($"Unbekannte Registry-Aktion: {args[1]}");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Die Registry-Aktion konnte nicht ausgeführt werden.\n\n{exception.Message}",
                "Registry-Fehler",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            exitCode = 1;
            return true;
        }
    }

    private static void ShowFatalError(string source, Exception exception)
    {
        try
        {
            var appDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RdpSignTool");
            Directory.CreateDirectory(appDirectory);

            var logPath = Path.Combine(appDirectory, "startup-error.log");
            var builder = new StringBuilder();
            builder.AppendLine($"Zeit: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"Quelle: {source}");
            builder.AppendLine(exception.ToString());
            builder.AppendLine(new string('-', 80));

            File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);

            MessageBox.Show(
                $"Die Anwendung konnte nicht gestartet werden.\n\nDetails wurden hier gespeichert:\n{logPath}\n\n{exception.Message}",
                "Startfehler",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
        }
    }
}
