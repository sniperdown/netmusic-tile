using System;
using System.IO;
using System.Text;

namespace NetMusicTile
{
    /// <summary>
    /// Application entry point.
    ///
    /// The tray icon is BEST EFFORT, not a guarantee: Shell_NotifyIcon can be refused
    /// with ERROR_ACCESS_DENIED when the process is launched from a location the shell
    /// restricts (verified on the development machine -- the identical binary registers
    /// fine when installed to a normal folder and fails with err=5 when run from a
    /// restricted working directory). So the control surface that always works is:
    ///
    ///     launch the exe again     -> toggle the panel (resume / suspend)
    ///     right-click the panel    -> suspend
    ///     Ctrl + right-click       -> quit
    ///
    /// Command line:
    ///     netmusic-tile.exe                 run the panel
    ///     netmusic-tile.exe --calibrate     open the position/size picker
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private System.Threading.Mutex _singleInstance;

        /// <summary>
        /// Where a second launch leaves its request for the running instance.
        /// A file rather than a named pipe: named pipes are blocked in some sandbox
        /// configurations, and this is a two-line protocol.
        /// </summary>
        internal static string SignalPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "signal.txt");

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);

            // --calibrate: the position picker. It runs BEFORE the single-instance
            // guard on purpose -- the normal way to use it is to launch it while the
            // panel is already running, and the guard would otherwise just ask that
            // panel to toggle and exit.
            if (e.Args.Length > 0 &&
                string.Equals(e.Args[0], "--calibrate", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var cal = new CalibrateWindow();
                    MainWindow = cal;
                    cal.Show();
                }
                catch (Exception ex)
                {
                    Log.Ex("App.OnStartup calibrate", ex);
                    Shutdown();
                }
                return;
            }

            // --set X,Y,W,H: write the geometry and exit. For scripts, for pasting a
            // known-good rectangle, and for verifying the save path without a mouse.
            if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--set", StringComparison.OrdinalIgnoreCase))
            {
                int sx, sy, sw, sh;
                if (TryParseGeometry(e.Args[1], out sx, out sy, out sw, out sh))
                {
                    Config.SavePosition(sx, sy, sw, sh);
                    Environment.Exit(0);
                    return;
                }

                Log.W("--set expects X,Y,W,H (for example --set 1995,1465,303,303)");
                Environment.Exit(2);
                return;
            }

            bool createdNew;
            _singleInstance = new System.Threading.Mutex(true, @"Local\netmusic-tile-single", out createdNew);
            if (!createdNew)
            {
                // The user double-clicked while the panel was already running.
                // That used to just exit, which made a suspended panel impossible to
                // bring back now that the tray icon cannot be relied on.
                RequestToggle("second launch");

                // Leave at once. Shutdown() alone would keep this process alive long
                // enough to show up as a second instance in Task Manager, which
                // looks like the guard failed.
                Environment.Exit(0);
                return;
            }

            try
            {
                var w = new MainWindow();
                MainWindow = w;
                // Shown then hidden so the handle and the DPI context exist before
                // the panel is first positioned; the constructor sets
                // Visibility.Hidden so nothing flashes meanwhile.
                w.Show();
                w.Hide();
            }
            catch (Exception ex)
            {
                Log.Ex("App.OnStartup", ex);
                Shutdown();
            }
        }

        /// <summary>Parse "X,Y,W,H" (commas, spaces or an x between numbers all work).</summary>
        private static bool TryParseGeometry(string s, out int x, out int y, out int w, out int h)
        {
            x = y = w = h = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;

            string[] parts = s.Split(new[] { ',', ' ', 'x', 'X' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return false;

            if (!int.TryParse(parts[0], out x)) return false;
            if (!int.TryParse(parts[1], out y)) return false;
            if (!int.TryParse(parts[2], out w)) return false;
            if (!int.TryParse(parts[3], out h)) return false;

            return w >= 1 && h >= 1;
        }

        /// <summary>Leave a one-word request where the running instance will see it.</summary>
        private static void RequestToggle(string why)
        {
            try
            {
                File.WriteAllText(SignalPath, "toggle", new UTF8Encoding(false));
                Log.W("second launch seen (" + why + ") -> asked the running instance to toggle");
            }
            catch (Exception ex)
            {
                Log.Ex("RequestToggle", ex);
            }
        }
    }
}
