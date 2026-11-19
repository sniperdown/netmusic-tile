using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NetMusicTile
{
    /// <summary>
    /// The position/size picker.
    ///
    /// Why this exists instead of "just edit config.json": the panel is positioned in
    /// PHYSICAL pixels and is only on screen while the Start menu is open. Finding the
    /// right numbers by hand therefore costs a whole round trip per attempt -- edit the
    /// JSON, restart or wait for the reload, open Start, look, guess again.
    ///
    /// This window IS that rectangle: drag it to move, drag the corner to resize, and
    /// the numbers update live. Saving writes only x/y/w/h into config.json, and the
    /// running panel re-reads the file on its own (it watches the timestamp), so the
    /// panel moves immediately and nothing has to be restarted.
    ///
    ///   netmusic-tile.exe --calibrate
    /// </summary>
    public partial class CalibrateWindow : Window
    {
        private int _x, _y, _w, _h;

        public CalibrateWindow()
        {
            InitializeComponent();

            var cfg = Config.Load();
            _x = cfg.X; _y = cfg.Y; _w = cfg.W; _h = cfg.H;

            Loaded += OnLoaded;
            PreviewKeyDown += OnKey;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Apply only once the window has a composition target, so the DPI query
            // below is meaningful and the first placement is exact.
            Apply(_x, _y, _w, _h);

            // Logged on purpose: without a line here, "the picker did not open" and
            // "it opened and was closed" look identical in the log.
            Log.W(string.Format("calibrate: open at {0},{1} {2}x{3} (dpi {4:0.##})",
                _x, _y, _w, _h, Dpi));

            Activate();
        }

        /// <summary>Device pixels per WPF device-independent unit (1.5 at 150%).</summary>
        private double Dpi
        {
            get
            {
                var src = PresentationSource.FromVisual(this);
                if (src != null && src.CompositionTarget != null)
                    return src.CompositionTarget.TransformToDevice.M11;
                return 1.0;
            }
        }

        /// <summary>Place the frame at a physical-pixel rectangle and refresh the label.</summary>
        private void Apply(int x, int y, int w, int h)
        {
            double s = Dpi;
            _x = x; _y = y; _w = w; _h = h;

            Left = x / s;
            Top = y / s;
            Width = w / s;
            Height = h / s;

            ShowNumbers();
        }

        private void ShowNumbers()
        {
            TxtPos.Text = string.Format("{0}, {1}", _x, _y);
            TxtSize.Text = string.Format("{0} × {1}", _w, _h);
        }

        /// <summary>Read the window back after a drag (Left/Top are DIP; store physical).</summary>
        private void ReadBackPosition()
        {
            double s = Dpi;
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;
            _x = (int)Math.Round(Left * s);
            _y = (int)Math.Round(Top * s);
            ShowNumbers();
        }

        /// <summary>
        /// Drag anywhere on the frame EXCEPT on a control.
        ///
        /// Without this check, pressing a button would bubble up here, DragMove would
        /// swallow the gesture, and Save would look like it does nothing.
        /// </summary>
        private void OnFrameDrag(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractive(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch { }
            ReadBackPosition();
        }

        private static bool IsInteractive(DependencyObject d)
        {
            for (var v = d; v != null; v = VisualTreeHelper.GetParent(v))
            {
                if (v is ButtonBase) return true;
                if (v is Thumb) return true;
            }
            return false;
        }

        /// <summary>
        /// Resize by the corner grip. Kept square: the panel's whole layout is built
        /// for a square, so a free-form size would only produce a broken-looking tile.
        /// </summary>
        private void OnGripDrag(object sender, DragDeltaEventArgs e)
        {
            double s = Dpi;
            int delta = (int)Math.Round(Math.Max(e.HorizontalChange, e.VerticalChange) * s);
            int n = Math.Max(120, Math.Min(900, _w + delta));
            Apply(_x, _y, n, n);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            switch (e.Key)
            {
                case Key.Escape: Close(); e.Handled = true; return;
                case Key.Left: Apply(_x - step, _y, _w, _h); e.Handled = true; return;
                case Key.Right: Apply(_x + step, _y, _w, _h); e.Handled = true; return;
                case Key.Up: Apply(_x, _y - step, _w, _h); e.Handled = true; return;
                case Key.Down: Apply(_x, _y + step, _w, _h); e.Handled = true; return;
                case Key.Enter: OnSave(this, null); e.Handled = true; return;
            }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            Config.SavePosition(_x, _y, _w, _h);
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
