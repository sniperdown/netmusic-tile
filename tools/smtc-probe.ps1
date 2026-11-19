$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
$mgrType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime]
$m = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
  $_.Name -eq "AsTask" -and $_.GetParameters().Count -eq 1 -and
  $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
if ($null -eq $m) { "SETUP FAILED: AsTask lookup returned null"; exit 9 }
$sw = [Diagnostics.Stopwatch]::StartNew()
$op = $mgrType::RequestAsync()
$t = $m.MakeGenericMethod($mgrType).Invoke($null, @($op))
$done = $t.Wait(15000)
$sw.Stop()
"elapsed_ms=" + $sw.ElapsedMilliseconds
if (-not $done) { "TIMEOUT: RequestAsync did not complete in 15s"; exit 3 }
if ($t.IsFaulted) { "FAULTED: " + $t.Exception.GetBaseException().Message; exit 4 }
$mgr = $t.Result
$ss = @($mgr.GetSessions())
"OK sessions=" + $ss.Count
foreach ($s in $ss) { "  " + $s.SourceAppUserModelId + "  " + $s.GetPlaybackInfo().PlaybackStatus }
