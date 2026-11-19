using System;
using System.IO;
using System.Text;

namespace NetMusicTile
{
    /// <summary>
    /// One log file next to the exe.
    ///
    /// Deliberately crude: this panel spends its life invisible, so the log is the
    /// only way to answer "why is it not doing what I expect". Written with an
    /// explicit UTF-8 encoder because song titles are routinely CJK.
    /// </summary>
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static readonly string Path =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "netmusic-tile.log");

        public static void W(string message)
        {
            Write("     ", message);
        }

        public static void Ex(string where, Exception ex)
        {
            Write("ERR  ", where + ": " + ex.GetType().Name + ": " + ex.Message);
        }

        private static void Write(string prefix, string message)
        {
            try
            {
                lock (Gate)
                {
                    // keep the file from growing without bound across sessions
                    try
                    {
                        var fi = new FileInfo(Path);
                        if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                            File.WriteAllText(Path, string.Empty, new UTF8Encoding(false));
                    }
                    catch { }

                    string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + prefix + message
                                  + Environment.NewLine;
                    File.AppendAllText(Path, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // logging must never be the reason the panel fails
            }
        }
    }
}
