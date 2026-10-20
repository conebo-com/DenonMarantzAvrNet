using System;
using System.Windows;

namespace DenonMarantzAvrNet.TesterGui
{
    /// <summary>Application startup and fail-fast assembly compatibility diagnostics.</summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                string? error = ReceiverAssemblyGuard.Validate();
                if (error != null)
                {
                    MessageBox.Show(error, "DenonMarantzAvrNet: DLL/API-Konflikt",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(-1);
                    return;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show("Fehler bei der Bibliothekspruefung: " + exception,
                    "DenonMarantzAvrNet: Startfehler", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

            base.OnStartup(e);
        }
    }
}
