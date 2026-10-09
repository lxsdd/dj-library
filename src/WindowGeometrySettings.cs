using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Xml.Serialization;

namespace DJLibrary
{
    public sealed class NamedWindowGeometry
    {
        public string Key = "";
        public double Left;
        public double Top;
        public double Width;
        public double Height;
        public bool Maximized;
    }

    internal static class WindowGeometrySettings
    {
        private const uint MonitorDefaultToNearest = 2;

        private sealed class RuntimeState
        {
            public Rect NormalBounds = Rect.Empty;
            public bool HasNormalBounds;
            public bool LastMaximized;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public int Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        public static bool Attach(Window window, string key)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentException("A stable window geometry key is required.", "key");

            AppSettings settings = SettingsManager.Load();
            NamedWindowGeometry geometry = Find(settings, key);
            bool restored = Apply(window, geometry);
            RuntimeState runtime = CreateRuntimeState(window, geometry);

            window.LocationChanged += delegate { Observe(window, runtime); };
            window.SizeChanged += delegate { Observe(window, runtime); };
            window.StateChanged += delegate { Observe(window, runtime); };

            // WPF can normalize presentation state while tearing a native window down.
            // Persist from Closing, while the tracked Normal/Maximized state and restore
            // bounds are still authoritative. Closed is deliberately not used here.
            window.Closing += delegate
            {
                SettingsManager.Update(delegate(AppSettings latest)
                {
                    Capture(latest, key, runtime);
                });
            };
            return restored;
        }

        public static bool Restore(Window window, string key)
        {
            AppSettings settings = SettingsManager.Load();
            return Apply(window, Find(settings, key));
        }

        private static bool Apply(Window window, NamedWindowGeometry geometry)
        {
            if (window == null || geometry == null || !IsFiniteGeometry(geometry)) return false;

            Rect wanted = new Rect(geometry.Left, geometry.Top, geometry.Width, geometry.Height);
            Rect work = GetNearestWorkArea(wanted);
            Rect safe = NormalizeForWorkArea(wanted, work, window.MinWidth, window.MinHeight);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.WindowState = WindowState.Normal;
            window.Left = safe.Left;
            window.Top = safe.Top;
            window.Width = safe.Width;
            window.Height = safe.Height;

            if (geometry.Maximized)
            {
                // Set immediately and once more when the HWND is created. The second write
                // makes restored maximization deterministic for owned/modal WPF windows.
                window.WindowState = WindowState.Maximized;
                EventHandler initialized = null;
                initialized = delegate
                {
                    window.SourceInitialized -= initialized;
                    if (window.WindowState != WindowState.Minimized)
                        window.WindowState = WindowState.Maximized;
                };
                window.SourceInitialized += initialized;
            }
            return true;
        }

        private static RuntimeState CreateRuntimeState(Window window, NamedWindowGeometry geometry)
        {
            RuntimeState state = new RuntimeState();
            if (geometry != null && IsFiniteGeometry(geometry))
            {
                state.NormalBounds = new Rect(geometry.Left, geometry.Top, geometry.Width, geometry.Height);
                state.HasNormalBounds = true;
                state.LastMaximized = geometry.Maximized;
            }
            else
            {
                Rect current = CurrentBounds(window);
                if (IsFiniteBounds(current))
                {
                    state.NormalBounds = current;
                    state.HasNormalBounds = true;
                }
                state.LastMaximized = window.WindowState == WindowState.Maximized;
            }
            return state;
        }

        private static void Observe(Window window, RuntimeState state)
        {
            if (window == null || state == null) return;
            WindowState current = window.WindowState;
            if (current == WindowState.Minimized) return;

            state.LastMaximized = current == WindowState.Maximized;
            Rect bounds = current == WindowState.Normal ? CurrentBounds(window) : window.RestoreBounds;
            if (IsFiniteBounds(bounds))
            {
                state.NormalBounds = bounds;
                state.HasNormalBounds = true;
            }
        }

        public static void Save(Window window, string key)
        {
            if (window == null || String.IsNullOrWhiteSpace(key)) return;
            SettingsManager.Update(delegate(AppSettings latest)
            {
                Capture(latest, key, window);
            });
        }

        internal static void Capture(AppSettings settings, string key, Window window)
        {
            if (settings == null || window == null || String.IsNullOrWhiteSpace(key)) return;
            Rect bounds = window.WindowState == WindowState.Normal ? CurrentBounds(window) : window.RestoreBounds;
            if (!IsFiniteBounds(bounds)) bounds = CurrentBounds(window);
            Capture(settings, key, bounds, window.WindowState == WindowState.Maximized);
        }

        private static void Capture(AppSettings settings, string key, RuntimeState state)
        {
            if (settings == null || state == null || !state.HasNormalBounds) return;
            Capture(settings, key, state.NormalBounds, state.LastMaximized);
        }

        private static void Capture(AppSettings settings, string key, Rect bounds, bool maximized)
        {
            if (settings == null || String.IsNullOrWhiteSpace(key) || !IsFiniteBounds(bounds)) return;
            if (settings.WindowGeometries == null) settings.WindowGeometries = new List<NamedWindowGeometry>();

            NamedWindowGeometry geometry = Find(settings, key);
            if (geometry == null)
            {
                geometry = new NamedWindowGeometry { Key = key };
                settings.WindowGeometries.Add(geometry);
            }
            geometry.Left = bounds.Left;
            geometry.Top = bounds.Top;
            geometry.Width = bounds.Width;
            geometry.Height = bounds.Height;
            geometry.Maximized = maximized;
        }

        private static Rect CurrentBounds(Window window)
        {
            if (window == null) return Rect.Empty;
            double left = window.Left, top = window.Top, width = window.Width, height = window.Height;
            if (!IsFinite(left) || !IsFinite(top) || !IsFinite(width) || !IsFinite(height) || width < 1 || height < 1)
                return Rect.Empty;
            return new Rect(left, top, width, height);
        }

        private static NamedWindowGeometry Find(AppSettings settings, string key)
        {
            if (settings == null || settings.WindowGeometries == null) return null;
            return settings.WindowGeometries.FirstOrDefault(delegate(NamedWindowGeometry item)
            {
                return item != null && String.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static bool IsFiniteGeometry(NamedWindowGeometry geometry)
        {
            if (geometry == null || geometry.Width < 120 || geometry.Height < 90) return false;
            return IsFinite(geometry.Left) && IsFinite(geometry.Top) && IsFinite(geometry.Width) && IsFinite(geometry.Height);
        }

        private static bool IsFiniteBounds(Rect bounds)
        {
            return bounds != Rect.Empty && bounds.Width >= 120 && bounds.Height >= 90 &&
                IsFinite(bounds.Left) && IsFinite(bounds.Top) && IsFinite(bounds.Width) && IsFinite(bounds.Height);
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static Rect GetNearestWorkArea(Rect wanted)
        {
            NativeRect native = new NativeRect
            {
                Left = (int)Math.Floor(wanted.Left),
                Top = (int)Math.Floor(wanted.Top),
                Right = (int)Math.Ceiling(wanted.Right),
                Bottom = (int)Math.Ceiling(wanted.Bottom)
            };
            IntPtr monitor = MonitorFromRect(ref native, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                MonitorInfo info = new MonitorInfo();
                info.Size = Marshal.SizeOf(typeof(MonitorInfo));
                if (GetMonitorInfo(monitor, ref info))
                    return new Rect(info.Work.Left, info.Work.Top,
                        Math.Max(1, info.Work.Right - info.Work.Left),
                        Math.Max(1, info.Work.Bottom - info.Work.Top));
            }
            return SystemParameters.WorkArea;
        }

        internal static Rect NormalizeForWorkArea(Rect wanted, Rect work, double minimumWidth, double minimumHeight)
        {
            if (work.Width <= 0 || work.Height <= 0) return wanted;
            double minWidth = Math.Max(120, minimumWidth);
            double minHeight = Math.Max(90, minimumHeight);
            double width = Math.Min(work.Width, Math.Max(minWidth, wanted.Width));
            double height = Math.Min(work.Height, Math.Max(minHeight, wanted.Height));
            double left = Math.Max(work.Left, Math.Min(wanted.Left, work.Right - width));
            double top = Math.Max(work.Top, Math.Min(wanted.Top, work.Bottom - height));
            return new Rect(left, top, width, height);
        }

        internal static string ValidateContract()
        {
            Rect safe = NormalizeForWorkArea(new Rect(5000, 4000, 1400, 900), new Rect(0, 0, 1920, 1080), 900, 600);
            if (safe.Left < 0 || safe.Top < 0 || safe.Right > 1920.01 || safe.Bottom > 1080.01)
                throw new InvalidDataException("Window geometry off-screen recovery failed.");

            AppSettings original = new AppSettings();
            Window source = new Window { Left = 220, Top = 140, Width = 1110, Height = 720, WindowStartupLocation = WindowStartupLocation.Manual };
            Capture(original, "selftest.window", source);

            RuntimeState maximized = new RuntimeState
            {
                NormalBounds = new Rect(245, 165, 1120, 730),
                HasNormalBounds = true,
                LastMaximized = true
            };
            Capture(original, "selftest.maximized", maximized);

            XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
            AppSettings roundTrip;
            using (MemoryStream memory = new MemoryStream())
            {
                serializer.Serialize(memory, original);
                memory.Position = 0;
                roundTrip = serializer.Deserialize(memory) as AppSettings;
            }
            NamedWindowGeometry saved = Find(roundTrip, "selftest.window");
            if (saved == null || Math.Abs(saved.Left - 220) > 0.1 || Math.Abs(saved.Top - 140) > 0.1 ||
                Math.Abs(saved.Width - 1110) > 0.1 || Math.Abs(saved.Height - 720) > 0.1)
                throw new InvalidDataException("Named window geometry XML round-trip failed.");

            NamedWindowGeometry savedMax = Find(roundTrip, "selftest.maximized");
            if (savedMax == null || !savedMax.Maximized || Math.Abs(savedMax.Left - 245) > 0.1 ||
                Math.Abs(savedMax.Top - 165) > 0.1 || Math.Abs(savedMax.Width - 1120) > 0.1 || Math.Abs(savedMax.Height - 730) > 0.1)
                throw new InvalidDataException("Maximized window close-state round-trip failed.");

            Window restoredMax = new Window { MinWidth = 700, MinHeight = 500 };
            if (!Apply(restoredMax, savedMax) || restoredMax.WindowState != WindowState.Maximized)
                throw new InvalidDataException("Remembered Maximized state was not restored.");

            string merge = SettingsManager.ValidateStaleSnapshotMergeContract();
            return "named window geometry: Normal + Maximized close-state round-trip + minimized preserves last non-minimized state + nearest-monitor recovery + " + merge;
        }
    }
}
