// Renders the REAL MainWindow.xaml to PNG, off-screen.
//
// Two jobs:
//   1. Preview the panel with sample data (layout and legibility check).
//   2. Emit the BACKDROP ALONE, with the foreground hidden -- that is the asset
//      to hand to an image generator as a style reference.
//
// It parses the actual XAML the app compiles (x:Class and Click handlers
// stripped, since there is no code-behind here), so a preview cannot drift from
// the real thing. It also catches XAML errors earlier than a build would.
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Shapes;
// Shapes also defines a Path, so the file-system one needs an alias.
using IOPath = System.IO.Path;

class PanelPreview
{
    const double Dip = 202.0;        // 303 physical px at 150% scaling

    static string _xamlPath;
    static string _coverPath;
    static string _outDir;

    [STAThread]
    static void Main(string[] argv)
    {
        // Defaults are relative to the current directory, so the tool works from
        // any checkout and no machine-specific path is baked in.
        // Usage: panelpreview [MainWindow.xaml] [cover-image] [output-dir]
        _xamlPath = argv.Length > 0 ? argv[0] : IOPath.Combine("app", "MainWindow.xaml");
        _coverPath = argv.Length > 1 ? argv[1] : IOPath.Combine("docs", "sample-cover.png");
        _outDir = argv.Length > 2 ? argv[2] : AppDomain.CurrentDomain.BaseDirectory;

        if (!File.Exists(_xamlPath)) { Console.WriteLine("missing xaml: " + _xamlPath); return; }

        // A supplied cover wins; otherwise generate one. Deliberate: these renders go
        // straight into the README, so they must not ship third-party album artwork --
        // and with this, the repository carries no binary sample image at all.
        byte[] cover;
        if (File.Exists(_coverPath))
        {
            cover = File.ReadAllBytes(_coverPath);
            Console.WriteLine("cover bytes: " + cover.Length);
        }
        else
        {
            cover = MakeSyntheticCover();
            Console.WriteLine("no cover file at " + _coverPath + "; generated a sample cover");
        }

        // Neutral sample text on purpose: this renders the screenshots that go into
        // the README, so it must not carry anyone's real library or listening history.
        const string SampleSong = "Sample Song Title";
        const string SampleArtist = "Sample Artist";

        // --- full panel, for judging layout ---
        Shot("cover-long", cover, SampleSong, SampleArtist, "▶ 播放中", "⏸", true, 3.0, false);
        Shot("no-cover", cover, "A Much Longer Song Title (feat. Someone) - Remix", "Another Artist", "⏸ 已暂停", "▶", false, 3.0, false);

        // --- backdrop only, for use as a style reference ---
        Console.WriteLine("backdrop-only renders (foreground hidden):");
        Shot("bg-cover", cover, SampleSong, SampleArtist, "", "", true, 6.0, true);
        Shot("bg-gradient", null, SampleSong, SampleArtist, "", "", false, 6.0, true, hideGlyph: true);
        Shot("bg-gradient-nosong", null, "", "", "", "", false, 6.0, true, hideGlyph: true);

        Console.WriteLine("wrote " + _outDir);
    }

    /// <summary>
    /// An abstract "album cover" stand-in: gradient plus two soft glows, rendered at
    /// 512x512 and PNG-encoded in memory.
    /// </summary>
    static byte[] MakeSyntheticCover()
    {
        const int S = 512;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var baseBrush = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#FF123B4F"),
                (Color)ColorConverter.ConvertFromString("#FF6A2C70"),
                45);
            dc.DrawRectangle(baseBrush, null, new Rect(0, 0, S, S));

            var glow1 = new RadialGradientBrush(
                (Color)ColorConverter.ConvertFromString("#66FFD166"),
                (Color)ColorConverter.ConvertFromString("#00FFD166"));
            dc.DrawEllipse(glow1, null, new Point(S * 0.30, S * 0.32), S * 0.34, S * 0.34);

            var glow2 = new RadialGradientBrush(
                (Color)ColorConverter.ConvertFromString("#5500D1B2"),
                (Color)ColorConverter.ConvertFromString("#0000D1B2"));
            dc.DrawEllipse(glow2, null, new Point(S * 0.76, S * 0.72), S * 0.40, S * 0.40);
        }

        var rtb = new RenderTargetBitmap(S, S, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var ms = new MemoryStream())
        {
            enc.Save(ms);
            return ms.ToArray();
        }
    }

    static void Shot(string name, byte[] cover, string song, string artist, string status,
                     string playGlyph, bool showCover, double scale, bool backdropOnly,
                     bool hideGlyph = false)
    {
        try
        {
            string raw = File.ReadAllText(_xamlPath, Encoding.UTF8);
            raw = Regex.Replace(raw, @"\s+x:Class=""[^""]*""", "");
            raw = Regex.Replace(raw, @"\s+Click=""[^""]*""", "");

            var win = (Window)XamlReader.Parse(raw);
            var root = (FrameworkElement)win.Content;
            win.Content = null;   // detach: it is about to get a new parent

            var songEl = (TextBlock)win.FindName("TxtSong");
            var artistEl = (TextBlock)win.FindName("TxtArtist");
            var statusEl = (TextBlock)win.FindName("TxtStatus");
            var playEl = (Button)win.FindName("BtnPlay");
            var letter = (TextBlock)win.FindName("CoverLetter");
            var backdrop = (Rectangle)win.FindName("CoverBackdrop");
            var artLayer = (Rectangle)win.FindName("CoverImageLayer");

            if (songEl != null) songEl.Text = string.IsNullOrEmpty(song) ? "未在播放" : song;
            if (artistEl != null) artistEl.Text = artist ?? "";
            if (statusEl != null) statusEl.Text = status;
            if (playEl != null) playEl.Content = playGlyph;

            if (showCover && cover != null && artLayer != null)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(cover);
                bmp.EndInit();
                bmp.Freeze();
                artLayer.Fill = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                artLayer.Visibility = Visibility.Visible;
                if (backdrop != null) backdrop.Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E));
                if (letter != null) letter.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (artLayer != null) artLayer.Visibility = Visibility.Collapsed;
                if (backdrop != null)
                    backdrop.Fill = new SolidColorBrush(string.IsNullOrEmpty(song)
                        ? Color.FromRgb(0x2A, 0x2A, 0x2E)
                        : Color.FromRgb(0x4A, 0x30, 0xBE));
                if (letter != null)
                {
                    letter.Visibility = Visibility.Visible;
                    letter.Text = string.IsNullOrEmpty(song) ? "♪" : song.Substring(0, 1).ToUpperInvariant();
                }
            }

            if (backdropOnly)
            {
                // hide the whole foreground block: the song/artist panel's parent
                // is the Grid that also holds the buttons and the status line
                if (songEl != null && songEl.Parent is FrameworkElement sp && sp.Parent is UIElement fg)
                    fg.Visibility = Visibility.Collapsed;
            }

            // The big fallback glyph is part of the backdrop design, so it stays by
            // default -- but as a style reference for an image generator it reads as
            // random bars, so the pure-gradient shot drops it.
            if (hideGlyph && letter != null) letter.Visibility = Visibility.Collapsed;

            var host = new Grid
            {
                Width = Dip,
                Height = Dip,
                Background = new SolidColorBrush(Color.FromRgb(0x8A, 0x4A, 0x5A))
            };
            host.Children.Add(root);

            host.Measure(new Size(Dip, Dip));
            host.Arrange(new Rect(0, 0, Dip, Dip));
            host.UpdateLayout();

            int px = (int)Math.Round(Dip * scale);
            var rtb = new RenderTargetBitmap(px, px, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(host);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            string outPath = IOPath.Combine(_outDir, "panel-" + name + ".png");
            using (var fs = File.Create(outPath)) enc.Save(fs);
            Console.WriteLine("  " + name + " -> " + px + "x" + px + "  " + outPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  " + name + " FAILED: " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}
