using System;
using System.Windows;

namespace ScreenFX
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Respect system reduce motion
            // Enable per-monitor DPI awareness already via manifest
            base.OnStartup(e);

            // Global exception handling
            DispatcherUnhandledException += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled: {args.Exception}");
                System.Windows.MessageBox.Show($"Произошла ошибка: {args.Exception.Message}\n\nФильтры будут временно отключены.", "ScreenFX", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"Domain unhandled: {args.ExceptionObject}");
            };
        }
    }
}
