using System.Runtime.InteropServices;
using System.Windows;

namespace DenonMarantzAvrNet.TesterGui
{
    public partial class JsonPreviewWindow : Window
    {
        public JsonPreviewWindow(Window owner, string json)
        {
            InitializeComponent();
            Owner = owner;
            TxtJson.Text = json;
            Title = Loc.Get("Check report before sending");
            TxtHint.Text = Loc.Format("This is the report for the compatibility check of your receiver. Please read it and make sure you are happy with its content.\n1. Click 'Copy to clipboard'.\n2. Paste the report into an e-mail to {0} and send it yourself.\nNothing is sent automatically.", MainWindow.EMail);
            BtnCopy.Content = Loc.Get("Copy to clipboard");
            BtnClose.Content = Loc.Get("Close");
            TxtMailRequest.Text = Loc.Format("Please send this report to us by e-mail: {0}", MainWindow.EMail) +
                Environment.NewLine + Loc.Get("This supports the open source project. Thank you very much!");
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs eventArgs)
        {
            try
            {
                Clipboard.SetText(TxtJson.Text);
                TxtCopyStatus.Text = Loc.Get("Copied to the clipboard.");
            }
            catch (ExternalException exception)
            {
                TxtCopyStatus.Text = string.Empty;
                MessageBox.Show(
                    this,
                    Loc.Format("The clipboard is currently not available: {0}", exception.Message) + Environment.NewLine +
                    Loc.Get("Please try again, or select the text and copy it with Ctrl+C."),
                    Loc.Get("Copy to clipboard"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs eventArgs)
        {
            Close();
        }
    }
}