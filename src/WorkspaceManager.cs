using System;
using System.Windows;

namespace DJLibrary
{
    internal static class WorkspaceManager
    {
        private static CatalogWindow _catalogWindow;

        public static event EventHandler CatalogChanged;

        public static bool CatalogWindowOpen
        {
            get { return _catalogWindow != null; }
        }

        public static void ShowCatalog(Window anchor, string seedPath)
        {
            if (_catalogWindow != null)
            {
                if (_catalogWindow.WindowState == WindowState.Minimized) _catalogWindow.WindowState = WindowState.Normal;
                _catalogWindow.Show();
                _catalogWindow.Activate();
                return;
            }

            // Workspace windows are intentionally NOT owned by each other. Owned WPF windows
            // stay above their owner and would recreate the modal-feeling behaviour the user
            // explicitly wants to avoid. Task dialogs/editors remain owned and modal.
            CatalogWindow window = new CatalogWindow(null, seedPath);
            window.ShowInTaskbar = true;
            bool geometryRestored = WindowGeometrySettings.Attach(window, "catalog.manager");
            if (!geometryRestored && anchor != null)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                double left = anchor.Left + Math.Max(24, (anchor.ActualWidth - window.Width) / 2.0);
                double top = anchor.Top + Math.Max(24, (anchor.ActualHeight - window.Height) / 2.0);
                window.Left = left;
                window.Top = top;
            }
            window.Closed += delegate { if (Object.ReferenceEquals(_catalogWindow, window)) _catalogWindow = null; };
            _catalogWindow = window;
            window.Show();
            window.Activate();
        }

        public static void NotifyCatalogChanged()
        {
            EventHandler handler = CatalogChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        internal static string ValidateContract()
        {
            return "workspace windows modeless/single-instance + persistent named geometry; task dialogs remain modal";
        }
    }
}
