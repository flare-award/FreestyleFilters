using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace ScreenFX
{
    public partial class App : System.Windows.Application
    {
        private static string GetLogDir()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenFX", "logs");
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch { return Path.GetTempPath(); }
        }

        private static string WriteCrashLog(Exception ex)
        {
            try
            {
                string dir = GetLogDir();
                string path = Path.Combine(dir, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                var sb = new StringBuilder();
                sb.AppendLine($"ScreenFX crash {DateTime.Now:O}");
                sb.AppendLine($"OS: {Environment.OSVersion} 64-bit:{Environment.Is64BitOperatingSystem} 64-bit process:{Environment.Is64BitProcess}");
                sb.AppendLine($".NET: {Environment.Version}  WPF: {typeof(System.Windows.Window).Assembly.FullName}");
                sb.AppendLine($"Culture: {System.Globalization.CultureInfo.CurrentCulture.Name} / {System.Globalization.CultureInfo.CurrentUICulture.Name}");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine(ex.ToString());
                if (ex is System.Windows.Markup.XamlParseException xaml)
                {
                    sb.AppendLine("--- XAML Parse Inner ---");
                    sb.AppendLine(xaml.InnerException?.ToString() ?? "no inner");
                }
                // Unwrap all inners
                var inner = ex.InnerException;
                int depth = 0;
                while (inner != null && depth < 10)
                {
                    sb.AppendLine($"--- Inner #{depth} ---");
                    sb.AppendLine(inner.ToString());
                    inner = inner.InnerException;
                    depth++;
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch { return ""; }
        }

        private static string FormatExceptionShort(Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine(ex.GetType().Name + ": " + ex.Message);
            var inner = ex.InnerException;
            int d = 0;
            while (inner != null && d < 5)
            {
                sb.AppendLine($"  -> {inner.GetType().Name}: {inner.Message}");
                inner = inner.InnerException;
                d++;
            }
            return sb.ToString();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // WinForms HighDPI must be set before any WinForms control is created (before base.OnStartup which may create window)
            try { System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2); } catch {}

            base.OnStartup(e);

            // Global handlers BEFORE any window is created
            DispatcherUnhandledException += OnDispatcherUnhandled;
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    if (args.ExceptionObject is Exception ex) WriteCrashLog(ex);
                    Debug.WriteLine($"Domain unhandled: {args.ExceptionObject}");
                } catch {}
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                try { WriteCrashLog(args.Exception); } catch {}
                args.SetObserved();
            };

            // Try to create main window manually (instead of StartupUri) so we can catch XAML parse errors
            try
            {
                var wnd = new ScreenFX.UI.Views.ControlPanelWindow();
                MainWindow = wnd;
                wnd.Show();
            }
            catch (Exception ex)
            {
                string logPath = WriteCrashLog(ex);
                string shortMsg = FormatExceptionShort(ex);
                string msg = "Не удалось загрузить главное окно ScreenFX.\n\n" +
                             shortMsg + "\n\n" +
                             (string.IsNullOrEmpty(logPath) ? "" : $"Подробный лог сохранён в:\n{logPath}\n\n") +
                             "Пожалуйста, отправьте этот файл разработчику.\n\n" +
                             "Возможные причины:\n" +
                             "• Повреждённая установка — попробуйте переустановить\n" +
                             "• Отсутствует .NET Desktop Runtime (при portable-версии)\n" +
                             "• Конфликт темы Windows / масштаба экрана\n\n" +
                             "Приложение будет закрыто.";
                try
                {
                    System.Windows.MessageBox.Show(msg, "ScreenFX — критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                } catch {}
                try { System.Windows.Forms.MessageBox.Show(msg, "ScreenFX — критическая ошибка", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error); } catch {}
                try { Clipboard.SetText(shortMsg + "\n\nLog: " + logPath + "\n\n" + ex.ToString()); } catch {}
                Shutdown(1);
            }
        }

        private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs args)
        {
            try
            {
                string logPath = WriteCrashLog(args.Exception);
                string shortMsg = FormatExceptionShort(args.Exception);
                Debug.WriteLine($"Unhandled: {args.Exception}");

                // Don't show the generic "filters disabled" for XAML parse errors — they are not filter-related
                bool isXaml = args.Exception is System.Windows.Markup.XamlParseException || args.Exception.InnerException is System.Windows.Markup.XamlParseException;
                string title = isXaml ? "ScreenFX — ошибка интерфейса" : "ScreenFX";
                string intro = isXaml
                    ? "Произошла ошибка при загрузке интерфейса.\n\n"
                    : $"Произошла ошибка: {args.Exception.Message}\n\n";

                string msg = intro + shortMsg + "\n\n" +
                             (isXaml ? "" : "Фильтры будут временно отключены.\n\n") +
                             (string.IsNullOrEmpty(logPath) ? "" : $"Лог сохранён в:\n{logPath}\n(откройте папку и отправьте файл)\n");

                System.Windows.MessageBox.Show(msg, title, MessageBoxButton.OK, isXaml ? MessageBoxImage.Error : MessageBoxImage.Warning);
                if (!string.IsNullOrEmpty(logPath))
                {
                    try { Process.Start("explorer.exe", $"/select,\"{logPath}\""); } catch {}
                }
                args.Handled = true;
            }
            catch
            {
                args.Handled = true;
            }
        }
    }
}
