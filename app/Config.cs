using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetMusicTile
{
    /// <summary>
    /// Everything tunable lives here so the panel can be repositioned or resized
    /// without a rebuild. Values are PHYSICAL pixels — on this machine that means
    /// the 3840x2160 coordinate space, not the 150%-scaled logical one.
    /// </summary>
    internal sealed class Config
    {
        public int X { get; set; } = 1995;
        public int Y { get; set; } = 1465;
        public int W { get; set; } = 303;
        public int H { get; set; } = 303;

        public int PollMenuMs { get; set; } = 120;
        public int PollSongMs { get; set; } = 1000;

        /// <summary>Stability window; 0 on close so hiding is immediate.</summary>
        public int MenuCloseDebounceMs { get; set; } = 0;
        public int MenuOpenDebounceMs { get; set; } = 120;

        public string MenuWindowClass { get; set; } = "DV2ControlHost";
        public string MenuWindowTitle { get; set; } = "Start menu";

        /// <summary>SMTC app id of the player (substring match).</summary>
        public string PlayerAppId { get; set; } = "cloudmusic";

        public static string Path =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public static Config Load()
        {
            try
            {
                if (!File.Exists(Path))
                {
                    Log.W("config.json not found; using defaults");
                    return new Config();
                }

                var opts = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    PropertyNameCaseInsensitive = true
                };
                var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(Path), opts);
                if (cfg == null) return new Config();

                Log.W(string.Format("config: pos={0},{1} size={2}x{3} player={4}",
                    cfg.X, cfg.Y, cfg.W, cfg.H, cfg.PlayerAppId));
                return cfg;
            }
            catch (Exception ex)
            {
                Log.Ex("Config.Load", ex);
                return new Config();
            }
        }

        /// <summary>
        /// Write the four geometry fields back, leaving every other byte alone.
        ///
        /// The file is hand-annotated (it carries _readme/_units notes explaining the
        /// units and each switch), so serialising this object back over it would
        /// silently throw all of that away. A targeted replacement keeps the notes.
        /// The running panel notices the new timestamp and re-applies the geometry
        /// on its next tick, so nothing has to be restarted.
        /// </summary>
        public static void SavePosition(int x, int y, int w, int h)
        {
            try
            {
                string text = File.Exists(Path) ? File.ReadAllText(Path) : "";
                text = ReplaceNumber(text, "x", x);
                text = ReplaceNumber(text, "y", y);
                text = ReplaceNumber(text, "w", w);
                text = ReplaceNumber(text, "h", h);
                File.WriteAllText(Path, text, new UTF8Encoding(false));
                Log.W(string.Format("config saved: pos={0},{1} size={2}x{3}", x, y, w, h));
            }
            catch (Exception ex)
            {
                Log.Ex("Config.SavePosition", ex);
            }
        }

        private static string ReplaceNumber(string text, string key, int value)
        {
            var re = new Regex("\"" + key + "\"\\s*:\\s*-?\\d+", RegexOptions.IgnoreCase);
            if (re.IsMatch(text))
                return re.Replace(text, "\"" + key + "\": " + value, 1);

            // Key absent (a minimal hand-written config): insert it before the last brace.
            int brace = text.LastIndexOf('}');
            if (brace < 0) return "{\n  \"" + key + "\": " + value + "\n}";
            string head = text.Substring(0, brace).TrimEnd();
            string sep = head.EndsWith("{", StringComparison.Ordinal) ? "\n  " : ",\n  ";
            return head + sep + "\"" + key + "\": " + value + "\n" + text.Substring(brace);
        }
    }
}
