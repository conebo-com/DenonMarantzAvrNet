using System.Windows;
using System.Windows.Controls;

namespace DenonMarantzAvrNet.TesterGui
{
    public sealed class ReceiverProfileDialog : Window
    {
        private readonly bool _requiresReceiverType;
        private readonly StackPanel _stcMain;
        private readonly StackPanel _stcButtons;
        private readonly TextBlock _txtInfo;
        private readonly ComboBox _cmbProfile;
        private readonly TextBlock _lblReceiverType;
        private readonly TextBox _txtReceiverType;
        private readonly Button _btnAccept;
        private readonly Button _btnCancel;

        public ReceiverProfileDialog(Window owner, string? detectedProfileId, string? reportedModelName)
        {
            _requiresReceiverType = string.IsNullOrWhiteSpace(reportedModelName);

            Owner = owner;
            Title = Loc.Get("Receiver profile");
            Width = 340;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _txtInfo = new TextBlock
            {
                Text = Loc.Get("Profile to use for the operations of this receiver:"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };

            _cmbProfile = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            // Automatic detection must always be selectable, including unknown and legacy models.
            // Overrides are intended only for explicit compatibility testing.
            var automaticId = detectedProfileId ?? "unknown";
            _cmbProfile.Items.Add(new ComboBoxItem
            {
                Content = "Auto (" + automaticId + ")",
                Tag = automaticId
            });
            _cmbProfile.Items.Add(new ComboBoxItem { Content = "AVC-X6800H", Tag = "avc-x6800h" });
            _cmbProfile.Items.Add(new ComboBoxItem { Content = "AVC-X6700H", Tag = "avc-x6700h" });
            _cmbProfile.Items.Add(new ComboBoxItem { Content = "Legacy (GoForm, generisch)", Tag = "legacy-goform" });
            _cmbProfile.Items.Add(new ComboBoxItem { Content = "Unknown / generisch", Tag = "unknown" });

            _lblReceiverType = new TextBlock
            {
                Text = Loc.Get("Type of your receiver:"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2),
                Visibility = _requiresReceiverType ? Visibility.Visible : Visibility.Collapsed
            };

            _txtReceiverType = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                Visibility = _requiresReceiverType ? Visibility.Visible : Visibility.Collapsed
            };

            _btnAccept = new Button
            {
                Content = Loc.Get("Accept"),
                Width = 80,
                IsDefault = true,
                IsEnabled = false,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _btnAccept.Click += BtnAccept_Click;

            _btnCancel = new Button
            {
                Content = Loc.Get("Cancel"),
                Width = 80,
                IsCancel = true
            };
            _btnCancel.Click += BtnCancel_Click;

            _stcButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _stcButtons.Children.Add(_btnAccept);
            _stcButtons.Children.Add(_btnCancel);

            _stcMain = new StackPanel
            {
                Margin = new Thickness(12)
            };
            _stcMain.Children.Add(_txtInfo);
            _stcMain.Children.Add(_cmbProfile);
            _stcMain.Children.Add(_lblReceiverType);
            _stcMain.Children.Add(_txtReceiverType);
            _stcMain.Children.Add(_stcButtons);

            Content = _stcMain;

            _cmbProfile.SelectionChanged += CmbProfile_SelectionChanged;
            _txtReceiverType.TextChanged += TxtReceiverType_TextChanged;

            var detectedIndex = 0; // Automatic identification is the safe default.
            var profileInfoText = _txtInfo.Text;
            if (_requiresReceiverType)
            {
                _txtInfo.Text =
                    Loc.Get("The receiver did not report a type name. Please select the most similar type from the list and enter the type of your receiver below.") +
                    Environment.NewLine + Environment.NewLine + profileInfoText;
            }

            _cmbProfile.SelectedIndex = detectedIndex;
        }

        /// <summary>Gets the profile ID chosen with Accept, otherwise <see langword="null"/>.</summary>
        public string? SelectedProfileId { get; private set; }

        /// <summary>
        /// Gets the receiver type entered by the user with Accept. It is only requested when the receiver
        /// reported no type name; otherwise <see langword="null"/>.
        /// </summary>
        public string? EnteredReceiverType { get; private set; }

        private void UpdateAcceptState()
        {
            var profileSelected = (_cmbProfile.SelectedItem as ComboBoxItem)?.Tag is string;
            var receiverTypeEntered = !_requiresReceiverType || _txtReceiverType.Text.Trim().Length > 0;
            _btnAccept.IsEnabled = profileSelected && receiverTypeEntered;
        }

        private void CmbProfile_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
        {
            UpdateAcceptState();
        }

        private void TxtReceiverType_TextChanged(object sender, TextChangedEventArgs eventArgs)
        {
            UpdateAcceptState();
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs eventArgs)
        {
            SelectedProfileId = (_cmbProfile.SelectedItem as ComboBoxItem)?.Tag as string;
            EnteredReceiverType = _requiresReceiverType ? _txtReceiverType.Text.Trim() : null;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs eventArgs)
        {
            SelectedProfileId = null;
            EnteredReceiverType = null;
            DialogResult = false;
        }
    }
}