using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NetMusicTile
{
    /// <summary>
    /// The panel.
    ///
    /// Design decisions that were all forced by measurement on the real machine:
    ///   * SMTC (Windows' media API) is the source of truth for song, artist and
    ///     cover, and the way playback commands are issued. Window-title scraping
    ///     and synthetic key input both failed or were unreliable here.
    ///   * WinRT calls run on a worker thread; they block, and blocking the UI
    ///     thread would freeze the panel.
    ///   * Geometry is applied with SetWindowPos in physical pixels. WPF's own
    ///     Left/Top/Width/Height are scaled by the monitor DPI (1.5 here).
    ///   * Right-click on the panel only SUSPENDS the panel; quitting is a tray
    ///     menu action, so a stray click can never kill the program.
    /// </summary>
    public partial class MainWindow : Window
    {
        private Config _cfg;
        private SmtcClient _smtc;

        private IntPtr _hwnd;
        private bool _clickThrough;
        private bool _menuOpen;
        private bool _panelShown;

        /// <summary>When false the panel stays hidden even while the menu is open.</summary>
        private bool _autoShow = true;

        private DispatcherTimer _menuTimer;
        private DispatcherTimer _songTimer;
        private DispatcherTimer _signalTimer;

        private DateTime _cfgWriteTime = DateTime.MinValue;
        private int _appliedX = int.MinValue, _appliedY = int.MinValue;
        private int _appliedW = int.MinValue, _appliedH = int.MinValue;

        private bool _songReadBusy;
        private NowPlaying _last;
        /// <summary>Track whose metadata the panel is currently showing.</summary>
        private string _lastTrackKey;

        /// <summary>
        /// Track whose artwork has been ACCEPTED and is on screen.
        ///
        /// Kept separate from _lastTrackKey because "the metadata changed" and "the
        /// artwork arrived" are different moments: NetEase updates the SMTC title
        /// first and the thumbnail about a poll later. While these two differ, the
        /// panel keeps re-reading the thumbnail (see MaxArtTries).
        /// </summary>
        private string _artTrackKey;

        /// <summary>Hash of the artwork currently on screen, used to spot a stale read.</summary>
        private int _artHash;

        /// <summary>Attempts spent on the current track's artwork.</summary>
        private int _artTries;

        /// <summary>
        /// How many polls to keep retrying before settling.
        ///
        /// Three is enough for the observed one-poll SMTC lag while keeping the
        /// worst case (a track with no artwork at all) short: the previous cover is
        /// held for at most that many seconds before falling back to the placeholder.
        /// </summary>
        private const int MaxArtTries = 3;

        /// <summary>Last SMTC error reported, so a wedged service is logged once, not every second.</summary>
        private string _lastError;

        /// <summary>True while real artwork is on screen (not the placeholder).</summary>
        private bool _coverShowing;

        private bool _rawMenuOpen;
        private DateTime _rawOpenSince = DateTime.UtcNow;
        private int _tickCount;

        public MainWindow()
        {
            // Hidden from the very first moment: WPF windows are Visible by default,
            // which painted the panel for an instant at startup.
            Visibility = Visibility.Hidden;

            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            PreviewMouseRightButtonDown += OnRightButtonDown;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                _cfg = Config.Load();
                _cfgWriteTime = FileWriteTimeSafe(Config.Path);
                _smtc = new SmtcClient(_cfg.PlayerAppId);

                int ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
                ex |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
                Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, ex);

                HwndSource.FromHwnd(_hwnd).AddHook(WndProcHook);

                ApplyRect(force: true);
                ShowPanel(false);
                TryCreateTrayIcon();

                Log.W(string.Format("window hwnd=0x{0:X} dpi={1}",
                    _hwnd.ToInt64(), Win32.GetDpiForWindow(_hwnd)));

                _menuTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_cfg.PollMenuMs) };
                _menuTimer.Tick += OnMenuTick;
                _menuTimer.Start();

                _songTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_cfg.PollSongMs) };
                _songTimer.Tick += OnSongTick;
                _songTimer.Start();

                // Picks up "the user double-clicked the exe again" requests.
                _signalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _signalTimer.Tick += OnSignalTick;
                _signalTimer.Start();

                StartSongRead();
            }
            catch (Exception ex)
            {
                Log.Ex("OnSourceInitialized", ex);
            }
        }

        // ------------------------------------------------------------------
        // window procedure
        // ------------------------------------------------------------------

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                if (msg == Win32.WM_MOUSEACTIVATE)
                {
                    // Without this, clicking a button activates the panel and the
                    // Start menu closes on focus loss.
                    handled = true;
                    return new IntPtr(Win32.MA_NOACTIVATE);
                }

                if (msg == Win32.WM_KEYDOWN && wParam.ToInt32() == Win32.VK_ESCAPE)
                {
                    Log.W("Esc -> suspending panel");
                    SuspendPanel();
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                Log.Ex("WndProcHook", ex);
            }
            return IntPtr.Zero;
        }

        // ------------------------------------------------------------------
        // tray
        //
        // Interaction contract:
        //   right-click on the PANEL  -> suspend (panel stops appearing; program lives)
        //   double-click TRAY icon    -> resume and show now
        //   right-click TRAY -> Quit  -> the only way to end the process
        // ------------------------------------------------------------------

        private System.Windows.Forms.NotifyIcon _tray;

        private void TryCreateTrayIcon()
        {
            try
            {
                var menu = new System.Windows.Forms.ContextMenuStrip();

                var miShow = new System.Windows.Forms.ToolStripMenuItem("Show now");
                miShow.Click += (s, e) => ResumePanel(showNow: true);

                var miFollow = new System.Windows.Forms.ToolStripMenuItem("Follow the Start menu");
                miFollow.Click += (s, e) => ResumePanel(showNow: false);

                var miQuit = new System.Windows.Forms.ToolStripMenuItem("Quit");
                miQuit.Click += (s, e) =>
                {
                    Log.W("tray: quit");
                    Application.Current.Shutdown();
                };

                menu.Items.Add(miShow);
                menu.Items.Add(miFollow);
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add(miQuit);

                _tray = new System.Windows.Forms.NotifyIcon
                {
                    Icon = LoadTrayIcon(),
                    Text = "netmusic-tile  (double-click: toggle panel; right-click: menu)",
                    Visible = true,
                    ContextMenuStrip = menu
                };
                _tray.DoubleClick += (s, e) =>
                {
                    if (_autoShow) SuspendPanel();
                    else ResumePanel(showNow: true);
                };

                Log.W("tray icon ready: " + (_tray.Icon == null ? "NO ICON" : "ok")
                    + " visible=" + _tray.Visible
                    + " (if it never appears: this copy was launched from inside the"
                    + " DSH workspace, where the shell refuses tray registration."
                    + " Install it with install.cmd instead.)");

                // A new tray icon is easy to miss, and on Windows 11 it may start
                // out inside the overflow flyout. Say hello once so the user knows
                // where to look and how to quit.
                try
                {
                    _tray.BalloonTipTitle = "netmusic-tile 已启动";
                    _tray.BalloonTipText = "面板随开始菜单出现。右键面板=收起，再双击一次 exe=恢复，Ctrl+右键面板=退出。";
                    _tray.ShowBalloonTip(4000);
                }
                catch { }
            }
            catch (Exception ex)
            {
                Log.Ex("TryCreateTrayIcon", ex);
            }
        }

        /// <summary>
        /// The tray image comes from app.ico next to the exe, so it can be replaced
        /// without a rebuild. Falls back to the stock icon so a missing file can
        /// never leave the user with no way to quit the program.
        /// </summary>
        private static System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(path))
                {
                    // the small size is what the tray actually draws
                    var ico = new System.Drawing.Icon(path, 16, 16);
                    if (ico != null) return ico;
                }
                Log.W("app.ico not found next to the exe; using the stock icon");
            }
            catch (Exception ex)
            {
                Log.Ex("LoadTrayIcon", ex);
            }
            return System.Drawing.SystemIcons.Application;
        }

        private void SuspendPanel()
        {
            _autoShow = false;
            Log.W("panel SUSPENDED - program still running; launch the exe again to resume"
                + " (or Ctrl+right-click the panel to quit)");
            if (_menuOpen) ShowPanel(false);
        }

        private void ResumePanel(bool showNow)
        {
            _autoShow = true;
            Log.W("panel RESUMED (showNow=" + showNow + ")");
            if (showNow)
            {
                ShowPanel(true);
                UpdateSongSafe();
            }
        }
        // ------------------------------------------------------------------
        // menu tracking
        // ------------------------------------------------------------------

        private void OnMenuTick(object sender, EventArgs e)
        {
            try
            {
                ReloadConfigIfChanged();
                ApplyRect(force: false);

                IntPtr menu = Win32.FindWindowW(_cfg.MenuWindowClass, _cfg.MenuWindowTitle);
                if (menu == IntPtr.Zero)
                {
                    if (_menuOpen)
                    {
                        _menuOpen = false;
                        ShowPanel(false);
                    }
                    return;
                }

                Win32.RECT r;
                if (!Win32.GetWindowRect(menu, out r)) return;

                // The reliable signal is visibility, not position: while closed this
                // window keeps its on-screen rect (left=0) and only clears WS_VISIBLE.
                bool visible = Win32.IsWindowVisible(menu);
                bool cloaked = Win32.IsCloaked(menu);
                Win32.RECT onMon;
                bool onMonitor = Win32.RectOnMonitor(menu, out onMon);
                bool open = visible && !cloaked && onMonitor;

                _tickCount++;
                if (_tickCount % 25 == 0)
                {
                    Log.W(string.Format("MENU visible={0} cloaked={1} onMonitor={2} => {3}",
                        visible, cloaked, onMonitor, open ? "OPEN" : "closed"));
                }

                if (open != _rawMenuOpen)
                {
                    _rawMenuOpen = open;
                    _rawOpenSince = DateTime.UtcNow;
                }

                if (_rawMenuOpen != _menuOpen)
                {
                    int need = _rawMenuOpen ? _cfg.MenuOpenDebounceMs : _cfg.MenuCloseDebounceMs;
                    if ((DateTime.UtcNow - _rawOpenSince).TotalMilliseconds >= need)
                    {
                        _menuOpen = _rawMenuOpen;
                        Log.W("Start menu " + (_menuOpen ? "OPENED" : "CLOSED"));
                        if (_menuOpen)
                        {
                            if (_autoShow) ShowPanel(true);
                        }
                        else
                        {
                            ShowPanel(false);
                        }
                    }
                }

                if (_panelShown)
                {
                    StickToTop();
                    UpdateClickThrough();
                }
            }
            catch (Exception ex)
            {
                Log.Ex("OnMenuTick", ex);
            }
        }

        private void ShowPanel(bool show)
        {
            if (show == _panelShown) return;
            _panelShown = show;

            if (show)
            {
                ApplyRect(force: true);
                Show();
                Win32.ShowWindow(_hwnd, Win32.SW_SHOWNOACTIVATE);
                StickToTop();
            }
            else
            {
                Hide();
                Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
                _clickThrough = false;
            }
        }

        private void StickToTop()
        {
            if (_hwnd == IntPtr.Zero) return;
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }

        private void ApplyRect(bool force)
        {
            if (_hwnd == IntPtr.Zero || _cfg == null) return;
            if (!force && _cfg.X == _appliedX && _cfg.Y == _appliedY &&
                _cfg.W == _appliedW && _cfg.H == _appliedH) return;

            double scale = 1.0;
            try
            {
                uint dpi = Win32.GetDpiForWindow(_hwnd);
                if (dpi > 0) scale = dpi / 96.0;
            }
            catch { }
            Width = _cfg.W / scale;
            Height = _cfg.H / scale;

            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, _cfg.X, _cfg.Y, _cfg.W, _cfg.H,
                Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);

            _appliedX = _cfg.X; _appliedY = _cfg.Y; _appliedW = _cfg.W; _appliedH = _cfg.H;
            Log.W(string.Format("rect {0},{1} {2}x{3}", _cfg.X, _cfg.Y, _cfg.W, _cfg.H));
        }

        private void UpdateClickThrough()
        {
            Win32.POINT p;
            Win32.RECT r;
            if (!Win32.GetCursorPos(out p) || !Win32.GetWindowRect(_hwnd, out r)) return;

            bool inside = p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
            bool through = !inside;
            if (through == _clickThrough) return;

            int ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
            if (through) ex |= Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED;
            else ex &= ~(Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED);
            Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, ex);
            _clickThrough = through;
        }

        private static DateTime FileWriteTimeSafe(string path)
        {
            try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        private void ReloadConfigIfChanged()
        {
            DateTime t = FileWriteTimeSafe(Config.Path);
            if (t == DateTime.MinValue || t == _cfgWriteTime) return;
            _cfgWriteTime = t;
            Log.W("config.json changed -> reloading");
            _cfg = Config.Load();
            _appliedW = int.MinValue;
        }

        // ------------------------------------------------------------------
        // now playing (via SMTC)
        // ------------------------------------------------------------------

        private void OnSongTick(object sender, EventArgs e)
        {
            UpdateSongSafe();
        }

        private void UpdateSongSafe()
        {
            try { UpdateSong(); }
            catch (Exception ex) { Log.Ex("UpdateSong", ex); }
        }

        private void StartSongRead()
        {
            UpdateSongSafe();
        }

        /// <summary>
        /// Poll SMTC on a worker thread and marshal the result to the UI.
        ///
        /// The WinRT calls block until the media service answers, which can take
        /// tens of milliseconds; doing that on the UI thread would stutter the panel.
        /// </summary>
        private void UpdateSong()
        {
            if (_songReadBusy) return;
            _songReadBusy = true;

            System.Threading.Tasks.Task.Run(() =>
            {
                // read the field on the worker: it is written only from the UI
                // thread in ApplyNowPlaying. Passing the ACCEPTED artwork's track
                // (not the displayed metadata's) is what makes SmtcClient re-read
                // the thumbnail while a new track's art has not arrived yet.
                string acceptedArt = _artTrackKey;

                NowPlaying np = null;
                try { np = _smtc.Read(acceptedArt); }
                catch (Exception ex) { Log.Ex("SmtcClient.Read", ex); }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _songReadBusy = false;
                    try { ApplyNowPlaying(np); }
                    catch (Exception ex) { Log.Ex("ApplyNowPlaying", ex); }
                }));
            });
        }

        private void ApplyNowPlaying(NowPlaying np)
        {
            if (np == null)
            {
                TxtSong.Text = "音乐信息不可用";
                TxtArtist.Text = "";
                TxtStatus.Text = "SMTC 读取失败";
                return;
            }

            // Surface a dead media service instead of going quiet. Before the
            // per-call deadlines were added, a wedged SMTC simply stopped the
            // panel from updating and swallowed every button press silently.
            if (!string.IsNullOrEmpty(np.Error) && np.Error != _lastError)
            {
                _lastError = np.Error;
                Log.W("SMTC read failed: " + np.Error);
            }
            else if (string.IsNullOrEmpty(np.Error))
            {
                _lastError = null;
            }

            _last = np;

            string song = np.Title;
            string artist = np.Artist;

            TxtSong.Text = string.IsNullOrEmpty(song) ? "未在播放" : song;
            TxtArtist.Text = artist ?? "";

            string statusText;
            switch (np.Status)
            {
                case "Playing": statusText = "▶ 播放中"; break;
                case "Paused": statusText = "⏸ 已暂停"; break;
                case "Stopped": statusText = "■ 已停止"; break;
                default: statusText = np.Status ?? "?"; break;
            }

            // make an unresponsive media service visible on the tile itself
            if (!string.IsNullOrEmpty(np.Error))
                statusText += "  (SMTC 无响应)";

            TxtStatus.Text = statusText;

            // play/pause button reflects the state
            BtnPlay.Content = (np.Status == "Playing") ? "⏸" : "▶";

            // Cover handling.
            //
            // Two layers: CoverBackdrop carries the fallback colour, CoverImageLayer
            // carries real artwork. (An earlier version drew artwork onto
            // CoverBackdrop as well, and guarded the fallback with the same
            // "artwork changed?" test, so one poll after a cover appeared the
            // fallback overwrote it -- the artwork flashed for about a second.)
            //
            // Acceptance is deliberately separate from arrival. NetEase publishes
            // the new title BEFORE the new thumbnail, so the first read after a
            // track change yields the new song's name with the PREVIOUS song's
            // artwork. Committing that read pinned the wrong cover for the whole
            // song, because the track key had already moved on and no further read
            // was attempted. So: keep whatever is on screen, keep asking, and only
            // accept artwork that differs from what is already shown -- or give up
            // after MaxArtTries so a track with no artwork still settles.
            if (np.TrackKey != _lastTrackKey)
            {
                _lastTrackKey = np.TrackKey;
                _artTries = 0;
            }

            bool artPending = np.TrackKey != _artTrackKey;
            if (artPending)
            {
                _artTries++;

                bool haveBytes = np.CoverBytes != null && np.CoverBytes.Length > 0;
                if (haveBytes)
                {
                    int hash = HashBytes(np.CoverBytes);
                    bool looksNew = hash != _artHash;

                    if (looksNew || _artTries >= MaxArtTries)
                    {
                        if (TrySetCover(np.CoverBytes))
                        {
                            _artHash = hash;
                            _coverShowing = true;
                        }
                        else
                        {
                            _coverShowing = false;      // undecodable: settle anyway
                        }
                        _artTrackKey = np.TrackKey;
                        Log.W(string.Format("art accepted {0}B for '{1}'{2}",
                            np.CoverBytes.Length, np.TrackKey,
                            looksNew ? "" : " (same as previous after " + _artTries + " tries)"));
                    }
                    else
                    {
                        // identical to the cover already on screen: almost certainly
                        // the previous track's artwork. Hold, and retry next poll.
                        Log.W(string.Format("art stale {0}B (try {1}/{2}) for '{3}' - retrying",
                            np.CoverBytes.Length, _artTries, MaxArtTries, np.TrackKey));
                    }
                }
                else if (_artTries >= MaxArtTries)
                {
                    // nothing published for this track: settle on the placeholder
                    _artTrackKey = np.TrackKey;
                    _coverShowing = false;
                    Log.W("art none for '" + np.TrackKey + "' - placeholder");
                }
            }

            if (!_coverShowing)
            {
                CoverImageLayer.Visibility = Visibility.Collapsed;
                CoverImageLayer.Fill = null;
                CoverLetter.Visibility = Visibility.Visible;
                CoverLetter.Text = string.IsNullOrEmpty(song) ? "♪" : FirstVisibleChar(song);
                CoverBackdrop.Fill = string.IsNullOrEmpty(song)
                    ? new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E))
                    : new SolidColorBrush(ColorFromName(song));
            }
        }

        /// <summary>
        /// Put real artwork on the backdrop layer. Returns false on failure.
        ///
        /// The artwork goes on as an ImageBrush fill rather than into an Image
        /// element, because a Rectangle clips its own fill to RadiusX/RadiusY --
        /// that is what keeps the panel's rounded corners.
        /// </summary>
        private bool TrySetCover(byte[] bytes)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();

                CoverImageLayer.Fill = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                CoverImageLayer.Visibility = Visibility.Visible;
                CoverLetter.Visibility = Visibility.Collapsed;
                // base colour behind the art: only visible through any transparency
                CoverBackdrop.Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E));
                return true;
            }
            catch (Exception ex)
            {
                Log.Ex("TrySetCover", ex);
                return false;
            }
        }

        /// <summary>
        /// Cheap content hash of an artwork blob (FNV-1a).
        ///
        /// Used only to answer "is this the same picture I am already showing?".
        /// The compressed length alone is nearly always enough, but a hash also
        /// catches the case where two different covers happen to encode to the
        /// same size.
        /// </summary>
        private static int HashBytes(byte[] data)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (byte b in data) h = (h ^ b) * 16777619;
                return h;
            }
        }

        private static string FirstVisibleChar(string s)
        {
            foreach (char c in s)
                if (!char.IsWhiteSpace(c)) return c.ToString().ToUpperInvariant();
            return "♪";
        }

        private static Color ColorFromName(string name)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in name) h = h * 31 + c;
                return FromHsv(Math.Abs(h % 360), 0.45, 0.42);
            }
        }

        private static Color FromHsv(double h, double s, double v)
        {
            int i = (int)Math.Floor(h / 60.0) % 6;
            double f = h / 60.0 - Math.Floor(h / 60.0);
            double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            double r, g, b;
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        // ------------------------------------------------------------------
        // transport
        // ------------------------------------------------------------------

        private void OnPrev(object sender, RoutedEventArgs e) { Send("prev", () => _smtc.Prev()); }
        private void OnPlay(object sender, RoutedEventArgs e) { Send("toggle", () => _smtc.Toggle()); }
        private void OnNext(object sender, RoutedEventArgs e) { Send("next", () => _smtc.Next()); }

        private void Send(string name, Func<bool> action)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                bool ok = false;
                try { ok = action(); }
                catch (Exception ex) { Log.Ex("Send " + name, ex); }

                Log.W("command " + name + " -> " + (ok ? "accepted" : "rejected"));

                // give the player a moment, then refresh
                System.Threading.Thread.Sleep(400);
                Dispatcher.BeginInvoke(new Action(UpdateSongSafe));
            });
        }

        private void OnRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ctrl+right-click is the deliberate quit; a bare right-click must never
            // end the program by accident, so it only suspends.
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                Log.W("Ctrl+right-click -> quitting");
                Application.Current.Shutdown();
                return;
            }

            SuspendPanel();
        }

        // ------------------------------------------------------------------
        // "double-clicked the exe again" channel
        //
        // The tray icon cannot be relied on here (the shell refuses tray
        // registration from this unsigned process - see App.xaml.cs), so relaunching
        // the exe is the dependable way to bring a suspended panel back.
        // ------------------------------------------------------------------

        private void OnSignalTick(object sender, EventArgs e)
        {
            try
            {
                string path = App.SignalPath;
                if (!File.Exists(path)) return;

                string text = "";
                try { text = File.ReadAllText(path).Trim(); } catch { }
                try { File.Delete(path); } catch { }

                if (text.Length == 0) return;
                Log.W("signal from second launch: " + text);

                if (text == "toggle")
                {
                    if (_autoShow) SuspendPanel();
                    else ResumePanel(showNow: true);
                }
            }
            catch (Exception ex)
            {
                Log.Ex("OnSignalTick", ex);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            }
            catch { }
            Log.W("closed");
            base.OnClosed(e);
        }
    }
}
