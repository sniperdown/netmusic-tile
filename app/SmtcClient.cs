using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace NetMusicTile
{
    /// <summary>
    /// Bridge for awaiting WinRT async operations on .NET 6.
    ///
    /// "await someIAsyncOperation" works on .NET Framework (System.Runtime.WindowsRuntime
    /// supplies GetAwaiter) but NOT with the .NET 6 CsWinRT projection, which has no
    /// GetAwaiter at all. Bridging through IAsyncInfo.Completed works on both.
    /// </summary>
    internal static class WinRt
    {
        public static Task<T> AsTask<T>(this IAsyncOperation<T> op)
        {
            var tcs = new TaskCompletionSource<T>();
            op.Completed = (info, status) =>
            {
                try
                {
                    if (status == AsyncStatus.Completed) tcs.TrySetResult(info.GetResults());
                    else if (status == AsyncStatus.Canceled) tcs.TrySetCanceled();
                    else tcs.TrySetException(info.ErrorCode);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };
            return tcs.Task;
        }
    }

    /// <summary>One snapshot of what the player is doing.</summary>
    internal sealed class NowPlaying
    {
        public bool HasSession;
        public string AppId;
        public string Title;
        public string Artist;
        public string Album;
        public string Status;
        public bool CanNext;
        public bool CanPrev;
        /// <summary>"title|artist" — identifies the track for artwork caching.</summary>
        public string TrackKey;
        /// <summary>True when the caller's accepted artwork does not match this track.</summary>
        public bool CoverWanted;
        public byte[] CoverBytes;     // null when the session has no artwork, or when
                                      // no read was attempted because art was accepted
        public string Error;
    }

    /// <summary>
    /// Reads and controls Windows' media session (SMTC).
    ///
    /// This replaced two earlier approaches that both failed on the real machine:
    /// scraping the player's window title (worked, but only a guess) and synthetic
    /// media keys / WM_APPCOMMAND (PostMessage returned false, SendInput returned
    /// false — something on the machine rejects cross-process synthetic input).
    /// SMTC is the supported interface and needs neither of those.
    /// </summary>
    internal sealed class SmtcClient
    {
        private readonly string _playerAppId;

        public SmtcClient(string playerAppId)
        {
            _playerAppId = playerAppId;
        }

        // Every WinRT call here talks to the shell's media service. That service can
        // stop answering entirely -- observed on the real machine: RequestAsync()
        // never completed, from two independent processes (this app and a
        // PowerShell probe), 15 s and counting. Without a limit, one wedged call
        // freezes this client forever: the artwork/title poll never returns (so the
        // panel stops updating) and a button press produces no action AND no log
        // line, because the log is written after the call returns.
        //
        // So every cross-process call gets a deadline. A timeout surfaces as an
        // exception, which the callers already handle: the poll logs it and the
        // button logs "rejected".
        private const int ManagerTimeoutMs = 3000;
        private const int MediaTimeoutMs = 2500;
        private const int CommandTimeoutMs = 2000;

        private static T AwaitLimit<T>(Task<T> task, int ms, string what)
        {
            if (!task.Wait(ms))
                throw new TimeoutException(what + " did not respond within " + ms
                    + " ms (the shell media service may be wedged)");
            return task.Result;
        }

        private static GlobalSystemMediaTransportControlsSessionManager _mgr;
        private static DateTime _mgrAt = DateTime.MinValue;

        /// <summary>
        /// How long a cached manager is trusted.
        ///
        /// Kept because re-requesting the manager on every poll is a lot of work to
        /// hand the shell: at one poll a second that is ~86,000 RequestAsync calls a
        /// day, and the manager object already tracks its session list itself. A
        /// periodic refresh still picks up players that start later; a null result
        /// forces one immediately (see PickSession).
        /// </summary>
        private const int ManagerRefreshMs = 30000;

        private GlobalSystemMediaTransportControlsSessionManager Manager(bool forceRefresh)
        {
            bool stale = _mgr == null || forceRefresh ||
                         (DateTime.UtcNow - _mgrAt).TotalMilliseconds > ManagerRefreshMs;
            if (stale)
            {
                _mgr = AwaitLimit(
                    GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(),
                    ManagerTimeoutMs, "SessionManager.RequestAsync");
                _mgrAt = DateTime.UtcNow;
            }
            return _mgr;
        }

        private GlobalSystemMediaTransportControlsSession PickFrom(
            GlobalSystemMediaTransportControlsSessionManager mgr)
        {
            GlobalSystemMediaTransportControlsSession fallback = null;
            foreach (var s in mgr.GetSessions())
            {
                var id = s.SourceAppUserModelId;
                if (id == null) continue;
                if (id.IndexOf(_playerAppId, StringComparison.OrdinalIgnoreCase) >= 0) return s;
                if (fallback == null) fallback = s;
            }
            return mgr.GetCurrentSession() ?? fallback;
        }

        /// <summary>Pick the player session, forcing a rediscovery if the cached manager sees nothing.</summary>
        private GlobalSystemMediaTransportControlsSession PickSession()
        {
            var s = PickFrom(Manager(false));
            if (s == null) s = PickFrom(Manager(true));
            return s;
        }

        /// <summary>
        /// Read the current state. Never throws.
        ///
        /// <paramref name="artTrackKey"/> is the track whose artwork the caller has
        /// already ACCEPTED and is showing. The thumbnail is re-read while the
        /// current track differs from that key -- which is both the cheap path
        /// (no re-read once accepted) and the retry path.
        ///
        /// The retry matters: NetEase updates the SMTC title/artist before its
        /// thumbnail. Measured in the field, the first poll after a track change
        /// returns the NEW title with the PREVIOUS track's artwork, so a single
        /// read per change pins the wrong cover for the whole song.
        /// </summary>
        public NowPlaying Read(string artTrackKey)
        {
            var np = new NowPlaying();
            try
            {
                var s = PickSession();
                if (s == null)
                {
                    np.HasSession = false;
                    return np;
                }

                np.HasSession = true;
                np.AppId = s.SourceAppUserModelId;

                try
                {
                    var pb = s.GetPlaybackInfo();
                    np.Status = pb.PlaybackStatus.ToString();
                    np.CanNext = pb.Controls.IsNextEnabled;
                    np.CanPrev = pb.Controls.IsPreviousEnabled;
                }
                catch { }

                try
                {
                    var p = AwaitLimit(s.TryGetMediaPropertiesAsync().AsTask(),
                                       MediaTimeoutMs, "TryGetMediaPropertiesAsync");
                    np.Title = p.Title;
                    np.Artist = p.Artist;
                    np.Album = p.AlbumTitle;
                    np.TrackKey = (p.Title ?? "") + "|" + (p.Artist ?? "");

                    np.CoverWanted = np.TrackKey != artTrackKey;
                    if (np.CoverWanted && p.Thumbnail != null)
                        np.CoverBytes = ReadAllBytes(p.Thumbnail);
                }
                catch (Exception ex)
                {
                    np.Error = ex.Message;
                }
            }
            catch (Exception ex)
            {
                np.Error = ex.GetType().Name + ": " + ex.Message;
            }
            return np;
        }

        private static byte[] ReadAllBytes(IRandomAccessStreamReference reference)
        {
            try
            {
                IRandomAccessStreamWithContentType stream = AwaitLimit(
                    reference.OpenReadAsync().AsTask(), MediaTimeoutMs, "Thumbnail.OpenReadAsync");

                var reader = new DataReader(stream);
                uint loaded = AwaitLimit(reader.LoadAsync((uint)stream.Size).AsTask(),
                                         MediaTimeoutMs, "DataReader.LoadAsync");
                var bytes = new byte[loaded];
                reader.ReadBytes(bytes);
                return bytes.Length > 0 ? bytes : null;
            }
            catch
            {
                return null;
            }
        }

        public bool Next() { return Invoke("next", s => s.TrySkipNextAsync()); }
        public bool Prev() { return Invoke("prev", s => s.TrySkipPreviousAsync()); }
        public bool Play() { return Invoke("play", s => s.TryPlayAsync()); }
        public bool Pause() { return Invoke("pause", s => s.TryPauseAsync()); }

        /// <summary>
        /// Play/pause.
        ///
        /// Uses TryTogglePlayPauseAsync first: the player decides whether Play or
        /// Pause is currently available, and this single call covers both. The
        /// explicit fallback matters because a player that reports
        /// "canPlay=false" (NetEase does while a track is paused at certain points)
        /// makes TryPlayAsync return false even though toggling works.
        /// </summary>
        public bool Toggle()
        {
            if (Invoke("toggle", s => s.TryTogglePlayPauseAsync())) return true;

            try
            {
                var s = PickSession();
                if (s == null) return false;
                // Compare on the string form: the enum lives in a different
                // namespace than the session class and is easy to get wrong.
                bool playing = s.GetPlaybackInfo().PlaybackStatus.ToString() == "Playing";
                return playing ? Pause() : Play();
            }
            catch (Exception ex)
            {
                Log.W("toggle fallback failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Issue one transport command, logging *why* it failed.
        ///
        /// The old version collapsed three very different outcomes into "rejected",
        /// which made a timeout (service not answering) indistinguishable from the
        /// player refusing the action (unsupported) and from there being no session
        /// at all. That ambiguity cost real debugging time.
        /// </summary>
        private bool Invoke(string what,
            Func<GlobalSystemMediaTransportControlsSession,
                 Windows.Foundation.IAsyncOperation<bool>> action)
        {
            try
            {
                var s = PickSession();
                if (s == null)
                {
                    Log.W(what + ": no player session to send it to");
                    return false;
                }

                bool accepted = AwaitLimit(action(s).AsTask(), CommandTimeoutMs, what + " command");
                if (!accepted)
                    Log.W(what + ": the player refused it (SMTC returned false)");
                return accepted;
            }
            catch (TimeoutException tex)
            {
                Log.W(what + ": " + tex.Message);
                return false;
            }
            catch (Exception ex)
            {
                Log.W(what + ": " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }
    }
}
