using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace ScreenFX.UI.Views
{
    public partial class HotkeyCaptureDialog : Window
    {
        public string CapturedHotkey { get; private set; } = "";
        private Key _lastKey = Key.None;
        private ModifierKeys _lastMods = ModifierKeys.None;

        public HotkeyCaptureDialog()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewKeyUp += OnPreviewKeyUp;
            BtnCancel.Click += (s,e)=> DialogResult = false;
            BtnOk.Click += (s,e)=> { CapturedHotkey = DisplayText.Text; DialogResult = true; };
            Loaded += (s,e)=> Keyboard.Focus(this);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
                return; // only modifiers, wait for main key

            _lastKey = key;
            _lastMods = Keyboard.Modifiers;

            // Filter invalid
            if (key == Key.Escape) { DialogResult = false; return; }

            var parts = new List<string>();
            if (_lastMods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (_lastMods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (_lastMods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (_lastMods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            string keyName = key.ToString();
            // Normalize F keys
            if (key >= Key.F1 && key <= Key.F24) keyName = key.ToString();
            else if (key == Key.Space) keyName = "Space";
            else if (key.ToString().Length == 1) keyName = key.ToString().ToUpper();

            parts.Add(keyName);
            string display = string.Join("+", parts);
            DisplayText.Text = display.Replace("Control", "Ctrl");

            // Validation
            if (parts.Count < 2)
            {
                ErrorText.Text = "Добавьте модификатор (Ctrl, Alt, Shift).";
                BtnOk.IsEnabled = false;
            }
            else if (_lastMods == ModifierKeys.None)
            {
                ErrorText.Text = "Нужен хотя бы один модификатор.";
                BtnOk.IsEnabled = false;
            }
            else
            {
                // Check conflicts with system (like Alt+F4 etc) warn
                if (display.Contains("Alt+F4") || display.Contains("Ctrl+Alt+Del"))
                {
                    ErrorText.Text = "Системное сочетание, выберите другое.";
                    BtnOk.IsEnabled = false;
                }
                else
                {
                    ErrorText.Text = "";
                    BtnOk.IsEnabled = true;
                }
            }
        }

        private void OnPreviewKeyUp(object sender, KeyEventArgs e) { }
    }
}
