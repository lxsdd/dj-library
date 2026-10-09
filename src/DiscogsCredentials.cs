using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace DJLibrary
{
    internal static class DiscogsCredentials
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DJ Library Discogs token v1");

        public static string TokenPath
        {
            get { return Path.Combine(SettingsManager.SettingsDirectory, "discogs-token.bin"); }
        }

        public static bool EnvironmentTokenActive
        {
            get { return !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DJLIBRARY_DISCOGS_TOKEN")); }
        }

        public static string LoadToken()
        {
            string environment = Environment.GetEnvironmentVariable("DJLIBRARY_DISCOGS_TOKEN");
            if (!String.IsNullOrWhiteSpace(environment)) return environment.Trim();
            try
            {
                if (!File.Exists(TokenPath)) return "";
                byte[] encrypted = File.ReadAllBytes(TokenPath);
                byte[] clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(clear).Trim();
            }
            catch { return ""; }
        }

        public static bool IsConfigured
        {
            get { return !String.IsNullOrWhiteSpace(LoadToken()); }
        }

        public static void SaveToken(string token)
        {
            if (String.IsNullOrWhiteSpace(token)) throw new ArgumentException("Discogs token must not be empty.");
            Directory.CreateDirectory(SettingsManager.SettingsDirectory);
            byte[] clear = Encoding.UTF8.GetBytes(token.Trim());
            byte[] encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            string temp = TokenPath + ".tmp";
            File.WriteAllBytes(temp, encrypted);
            if (File.Exists(TokenPath)) File.Delete(TokenPath);
            File.Move(temp, TokenPath);
        }

        public static void DeleteToken()
        {
            try { if (File.Exists(TokenPath)) File.Delete(TokenPath); }
            catch { }
        }
    }

    internal sealed class DiscogsSettingsDialog : Window
    {
        private readonly PasswordBox _token;
        private readonly TextBlock _status;

        private DiscogsSettingsDialog(Window owner)
        {
            Owner = owner;
            Title = "Discogs-Zugang";
            GridRuntimeSupport.ApplyWindowIcon(this);
            Width = 620;
            Height = 330;
            MinWidth = 540;
            MinHeight = 290;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowGeometrySettings.Attach(this, "discogs.settings");

            DockPanel root = new DockPanel { Margin = new Thickness(14) };
            Content = root;

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            Button open = new Button { Content = "Open Token Page", MinWidth = 120, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
            open.Click += delegate
            {
                try { Process.Start("https://www.discogs.com/settings/developers"); }
                catch { }
            };
            buttons.Children.Add(open);

            Button remove = new Button { Content = "Gespeicherten Token entfernen", MinWidth = 165, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
            remove.Click += delegate
            {
                DiscogsCredentials.DeleteToken();
                _token.Clear();
                RefreshStatus();
            };
            buttons.Children.Add(remove);

            Button save = new Button { Content = "Save", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
            save.Click += delegate
            {
                if (!String.IsNullOrWhiteSpace(_token.Password)) DiscogsCredentials.SaveToken(_token.Password);
                DialogResult = true;
            };
            buttons.Children.Add(save);

            Button cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Padding = new Thickness(8, 4, 8, 4) };
            buttons.Children.Add(cancel);

            StackPanel content = new StackPanel();
            root.Children.Add(content);
            content.Children.Add(new TextBlock
            {
                Text = "Independent Discogs database search requires an authenticated API request. Enter a personal API token here. The token is not stored in settings.xml; it is protected with Windows DPAPI for the current Windows user.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            content.Children.Add(new TextBlock { Text = "Personal Discogs API Token:", Margin = new Thickness(0, 0, 0, 4) });
            _token = new PasswordBox { MinWidth = 420, Margin = new Thickness(0, 0, 0, 10) };
            content.Children.Add(_token);
            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            content.Children.Add(_status);
            content.Children.Add(new TextBlock
            {
                Text = "Alternatively, set DJLIBRARY_DISCOGS_TOKEN as an environment variable; it takes precedence over the locally stored token.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (DiscogsCredentials.EnvironmentTokenActive)
                _status.Text = "Status: token from DJLIBRARY_DISCOGS_TOKEN is active.";
            else if (DiscogsCredentials.IsConfigured)
                _status.Text = "Status: personal token is stored encrypted. The field intentionally remains blank; enter a new token only to replace it.";
            else
                _status.Text = "Status: no token configured. MusicBrainz-linked Discogs releases can still be read, but independent Discogs search requires a token.";
        }

        public static void Show(Window owner)
        {
            new DiscogsSettingsDialog(owner).ShowDialog();
        }
    }
}
