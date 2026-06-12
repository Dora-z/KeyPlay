using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Text;
using Android.Views;
using Android.Widget;
using AudioStream = Android.Media.Stream;
using ViewOrientation = Android.Widget.Orientation;
using Uri = Android.Net.Uri;

namespace KeyPlay;

[Activity(
    Label = "@string/app_name",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.Portrait)]
public class MainActivity : Activity, MediaPlayer.IOnPreparedListener, MediaPlayer.IOnCompletionListener, MediaPlayer.IOnErrorListener
{
    private const int RequestReadAudio = 1001;
    private const int SeekSmallMs = 10_000;
    private const int MediaNotificationId = 2001;
    private const string MediaChannelId = "keyplay_media";
    private const string ActionMediaCommand = "com.companyname.KeyPlay.MEDIA_COMMAND";
    private const string ExtraMediaCommand = "media_command";
    private const string CommandPrevious = "previous";
    private const string CommandToggle = "toggle";
    private const string CommandNext = "next";
    private const string CommandStop = "stop";
    private const string PermissionReadMediaAudio = "android.permission.READ_MEDIA_AUDIO";
    private const string PermissionReadExternalStorage = "android.permission.READ_EXTERNAL_STORAGE";

    private readonly List<Track> _tracks = new();
    private readonly List<int> _shuffleHistory = new();
    private readonly Random _random = new();

    private TextView? _nowTitle;
    private TextView? _nowMeta;
    private TextView? _timeText;
    private TextView? _statusText;
    private TextView? _hintText;
    private TextView? _helpText;
    private TextView? _emptyText;
    private ProgressBar? _progress;
    private LinearLayout? _nowPanel;
    private LinearLayout? _rootView;
    private ListView? _list;
    private PlayerPageLayout? _playerPage;
    private ImageView? _coverView;
    private TextView? _playerTitle;
    private TextView? _playerArtist;
    private TextView? _playerCurrentTimeText;
    private TextView? _playerDurationText;
    private ProgressBar? _playerProgress;
    private TrackAdapter? _adapter;
    private MediaPlayer? _player;
    private Timer? _progressTimer;
    private MediaSession? _mediaSession;
    private MediaSessionCallback? _mediaSessionCallback;
    private MediaCommandReceiver? _mediaCommandReceiver;
    private NotificationManager? _notificationManager;

    private int _selectedIndex;
    private int _playingIndex = -1;
    private bool _playerReady;
    private bool _helpVisible;
    private bool _playerPageVisible;
    private bool _shuffleEnabled;
    private bool _isPreparing;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        ActionBar?.Hide();
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        VolumeControlStream = AudioStream.Music;
        ApplyImmersiveMode();

        InitializeMediaControls();
        SetContentView(BuildContentView());
        ApplyImmersiveMode();
        _progressTimer = new Timer(_ => RunOnUiThread(UpdateProgress), null, 500, 500);
        HandleMediaIntent(Intent);

        if (HasAudioPermission())
        {
            LoadTracks();
        }
        else
        {
            ShowPermissionRequest();
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        ApplyImmersiveMode();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            ApplyImmersiveMode();
        }
    }

    protected override void OnDestroy()
    {
        _progressTimer?.Dispose();
        ReleasePlayer();
        ReleaseMediaControls();
        base.OnDestroy();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent != null)
        {
            Intent = intent;
            HandleMediaIntent(intent);
        }
    }

    private void ApplyImmersiveMode()
    {
        ActionBar?.Hide();

        if (Window == null)
        {
            return;
        }

        Window.SetFlags(WindowManagerFlags.Fullscreen, WindowManagerFlags.Fullscreen);
        Window.SetStatusBarColor(Color.Transparent);
        Window.SetNavigationBarColor(Color.Transparent);
        Window.DecorView.SystemUiFlags =
            SystemUiFlags.Fullscreen |
            SystemUiFlags.HideNavigation |
            SystemUiFlags.ImmersiveSticky |
            SystemUiFlags.LayoutFullscreen |
            SystemUiFlags.LayoutHideNavigation |
            SystemUiFlags.LayoutStable;
    }

    private void InitializeMediaControls()
    {
        _notificationManager = (NotificationManager?)GetSystemService(NotificationService);
        CreateMediaNotificationChannel();

        _mediaSessionCallback = new MediaSessionCallback(this);
        _mediaSession = new MediaSession(this, "KeyPlay");
        _mediaSession.SetCallback(_mediaSessionCallback);
        _mediaSession.SetFlags(MediaSessionFlags.HandlesMediaButtons | MediaSessionFlags.HandlesTransportControls);

        _mediaCommandReceiver = new MediaCommandReceiver(this);
        var filter = new IntentFilter(ActionMediaCommand);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            RegisterReceiver(_mediaCommandReceiver, filter, ReceiverFlags.NotExported);
        }
        else
        {
            RegisterReceiver(_mediaCommandReceiver, filter);
        }
    }

    private void ReleaseMediaControls()
    {
        try
        {
            if (_mediaCommandReceiver != null)
            {
                UnregisterReceiver(_mediaCommandReceiver);
            }
        }
        catch
        {
            // Receiver may already be gone if the activity was torn down by the system.
        }
        finally
        {
            _mediaCommandReceiver = null;
        }

        CancelMediaNotification();
        _mediaSession?.SetCallback(null);
        _mediaSession?.Release();
        _mediaSession?.Dispose();
        _mediaSession = null;
        _mediaSessionCallback = null;
    }

    private void HandleMediaIntent(Intent? intent)
    {
        if (intent?.Action != ActionMediaCommand)
        {
            return;
        }

        HandleMediaCommand(intent.GetStringExtra(ExtraMediaCommand));
    }

    private void HandleMediaCommand(string? command)
    {
        RunOnUiThread(() =>
        {
            switch (command)
            {
                case CommandPrevious:
                    PlayRelative(-1);
                    break;

                case CommandNext:
                    PlayRelative(1);
                    break;

                case CommandToggle:
                    TogglePlayback();
                    break;

                case CommandStop:
                    PausePlayback();
                    CancelMediaNotification();
                    break;
            }
        });
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode != RequestReadAudio)
        {
            return;
        }

        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
        {
            LoadTracks();
        }
        else
        {
            SetEmptyState("没有音乐读取权限。请在系统设置里允许 KeyPlay 读取音频，然后按 1 重新扫描。");
        }
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e == null)
        {
            return base.DispatchKeyEvent(e);
        }

        if (IsPlayerKey(e.KeyCode))
        {
            if (e.Action == KeyEventActions.Down)
            {
                HandleKey(e.KeyCode);
            }

            return true;
        }

        return base.DispatchKeyEvent(e);
    }

    public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        return HandleKey(keyCode) || base.OnKeyDown(keyCode, e);
    }

    public override void OnBackPressed()
    {
        if (_helpVisible)
        {
            ToggleHelp();
            return;
        }

        if (_playerPageVisible)
        {
            SetListState();
            return;
        }

        Finish();
    }

    private bool HandleKey(Keycode keyCode)
    {
        switch (keyCode)
        {
            case Keycode.DpadUp:
            case Keycode.Num2:
                if (_playerPageVisible)
                {
                    AdjustVolume(Adjust.Raise);
                    return true;
                }

                MoveSelection(-1);
                return true;

            case Keycode.DpadDown:
            case Keycode.Num8:
                if (_playerPageVisible)
                {
                    AdjustVolume(Adjust.Lower);
                    return true;
                }

                MoveSelection(1);
                return true;

            case Keycode.DpadLeft:
            case Keycode.Num4:
            case Keycode.MediaPrevious:
                PlayRelative(-1);
                return true;

            case Keycode.DpadRight:
            case Keycode.Num6:
            case Keycode.MediaNext:
                PlayRelative(1);
                return true;

            case Keycode.DpadCenter:
            case Keycode.Enter:
            case Keycode.Num5:
            case Keycode.Space:
            case Keycode.MediaPlayPause:
            case Keycode.Headsethook:
                TogglePlayback();
                return true;

            case Keycode.Star:
                SeekBy(-SeekSmallMs);
                return true;

            case Keycode.Pound:
                SeekBy(SeekSmallMs);
                return true;

            case Keycode.Num0:
            case Keycode.Info:
                ToggleHelp();
                return true;

            case Keycode.Num1:
                LoadTracks();
                return true;

            case Keycode.Num3:
                TogglePlayerPage();
                return true;

            case Keycode.Num7:
                ToggleShuffle();
                return true;

            case Keycode.Call:
                TogglePlayback();
                return true;

            case Keycode.Endcall:
                PausePlayback();
                return true;

            case Keycode.VolumeUp:
                AdjustVolume(Adjust.Raise);
                return true;

            case Keycode.VolumeDown:
                AdjustVolume(Adjust.Lower);
                return true;

            case Keycode.Back:
                if (_helpVisible || _playerPageVisible)
                {
                    if (_helpVisible)
                    {
                        ToggleHelp();
                    }
                    else
                    {
                        SetListState();
                    }

                    return true;
                }

                return false;
        }

        return false;
    }

    private static bool IsPlayerKey(Keycode keyCode)
    {
        return keyCode is Keycode.DpadUp
            or Keycode.Num2
            or Keycode.DpadDown
            or Keycode.Num8
            or Keycode.DpadLeft
            or Keycode.Num4
            or Keycode.MediaPrevious
            or Keycode.DpadRight
            or Keycode.Num6
            or Keycode.MediaNext
            or Keycode.DpadCenter
            or Keycode.Enter
            or Keycode.Num5
            or Keycode.Space
            or Keycode.MediaPlayPause
            or Keycode.Headsethook
            or Keycode.Star
            or Keycode.Pound
            or Keycode.Num0
            or Keycode.Info
            or Keycode.Num1
            or Keycode.Num3
            or Keycode.Num7
            or Keycode.Call
            or Keycode.Endcall
            or Keycode.VolumeUp
            or Keycode.VolumeDown;
    }

    public void OnPrepared(MediaPlayer? mp)
    {
        if (mp == null)
        {
            return;
        }

        _playerReady = true;
        _isPreparing = false;
        mp.Start();
        UpdateNowPlaying();
        RefreshMediaControls();
        SetStatus("正在播放");
    }

    public void OnCompletion(MediaPlayer? mp)
    {
        if (_tracks.Count > 1)
        {
            PlayNextAuto();
            return;
        }

        _playerReady = false;
        _isPreparing = false;
        UpdateProgress();
        SetStatus("播放结束");
        UpdatePlayerPage();
        RefreshMediaControls();
    }

    public bool OnError(MediaPlayer? mp, [GeneratedEnum] MediaError what, int extra)
    {
        _playerReady = false;
        _isPreparing = false;
        SetStatus($"播放失败：{what}");
        RefreshMediaControls();
        return true;
    }

    private View BuildContentView()
    {
        var root = new LinearLayout(this)
        {
            Orientation = ViewOrientation.Vertical,
            Focusable = true,
            FocusableInTouchMode = true,
        };
        root.SetBackgroundColor(Color.Rgb(7, 18, 22));
        root.SetPadding(Dp(8), Dp(8), Dp(8), Dp(8));
        _rootView = root;

        _nowPanel = new LinearLayout(this)
        {
            Orientation = ViewOrientation.Vertical,
        };
        root.AddView(_nowPanel, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        _nowTitle = MakeText("KeyPlay", 22, Color.White, TypefaceStyle.Bold);
        _nowTitle.SetSingleLine(true);
        _nowTitle.Ellipsize = TextUtils.TruncateAt.End;
        _nowPanel.AddView(_nowTitle, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        _nowMeta = MakeText("按 1 扫描本机音乐", 14, Color.Rgb(171, 199, 205), TypefaceStyle.Normal);
        _nowMeta.SetSingleLine(true);
        _nowMeta.Ellipsize = TextUtils.TruncateAt.End;
        _nowPanel.AddView(_nowMeta, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        _progress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Max = 1000,
        };
        _nowPanel.AddView(_progress, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(14)));

        var line = new LinearLayout(this)
        {
            Orientation = ViewOrientation.Horizontal,
        };
        line.SetGravity(GravityFlags.CenterVertical);

        _timeText = MakeText("00:00 / 00:00", 13, Color.Rgb(220, 233, 235), TypefaceStyle.Normal);
        line.AddView(_timeText, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));

        _statusText = MakeText("就绪", 13, Color.Rgb(128, 220, 159), TypefaceStyle.Bold);
        _statusText.Gravity = GravityFlags.Right;
        line.AddView(_statusText, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        _nowPanel.AddView(line, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        _helpText = MakeText(BuildHelpText(), 15, Color.Rgb(232, 242, 243), TypefaceStyle.Normal);
        _helpText.SetBackgroundColor(Color.Rgb(13, 39, 45));
        _helpText.SetPadding(Dp(8), Dp(8), Dp(8), Dp(8));
        _helpText.Visibility = ViewStates.Gone;
        root.AddView(_helpText, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));

        _emptyText = MakeText("", 16, Color.Rgb(232, 242, 243), TypefaceStyle.Normal);
        _emptyText.Gravity = GravityFlags.Center;
        _emptyText.SetBackgroundColor(Color.Rgb(13, 39, 45));
        _emptyText.Visibility = ViewStates.Gone;
        root.AddView(_emptyText, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));

        _playerPage = BuildPlayerPage();
        _playerPage.Visibility = ViewStates.Gone;
        root.AddView(_playerPage, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));

        _list = new ListView(this)
        {
            ChoiceMode = ChoiceMode.Single,
            DividerHeight = 1,
            Focusable = true,
            FocusableInTouchMode = true,
        };
        _list.SetBackgroundColor(Color.Rgb(7, 18, 22));
        _list.SetSelector(Android.Resource.Color.Transparent);
        _adapter = new TrackAdapter(this);
        _list.Adapter = _adapter;
        _list.ItemClick += (_, args) =>
        {
            _selectedIndex = args.Position;
            PlaySelected();
        };
        root.AddView(_list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));

        _hintText = MakeText("2/8 选歌  5 播放/暂停  3 播放页  7 随机  4/6 切歌  0 帮助", 12, Color.Rgb(154, 181, 186), TypefaceStyle.Normal);
        _hintText.SetSingleLine(true);
        _hintText.Ellipsize = TextUtils.TruncateAt.End;
        root.AddView(_hintText, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        root.RequestFocus();
        return root;
    }

    private PlayerPageLayout BuildPlayerPage()
    {
        var page = new PlayerPageLayout(this)
        {
            Orientation = ViewOrientation.Vertical,
            Focusable = true,
            FocusableInTouchMode = true,
        };
        page.SetPadding(Dp(18), Dp(18), Dp(18), Dp(12));

        _playerTitle = MakeText("KeyPlay", 23, Color.Rgb(24, 39, 42), TypefaceStyle.Bold);
        _playerTitle.SetSingleLine(true);
        _playerTitle.Ellipsize = TextUtils.TruncateAt.End;
        page.AddView(_playerTitle, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(38)));

        _playerArtist = MakeText("未知歌手", 16, Color.Rgb(61, 82, 86), TypefaceStyle.Bold);
        _playerArtist.SetSingleLine(true);
        _playerArtist.Ellipsize = TextUtils.TruncateAt.End;
        page.AddView(_playerArtist, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(30)));

        var coverArea = new FrameLayout(this)
        {
            Focusable = false,
        };

        _coverView = new ImageView(this)
        {
            Focusable = false,
        };
        _coverView.SetAdjustViewBounds(false);
        _coverView.SetScaleType(ImageView.ScaleType.CenterCrop);
        var coverSize = GetPlayerCoverSize();
        var coverParams = new FrameLayout.LayoutParams(coverSize, coverSize, GravityFlags.Center);
        coverArea.AddView(_coverView, coverParams);
        page.AddView(coverArea, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));

        _playerProgress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Max = 1000,
        };
        var progressParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(8));
        progressParams.SetMargins(0, Dp(8), 0, Dp(2));
        page.AddView(_playerProgress, progressParams);

        var timeRow = new LinearLayout(this)
        {
            Orientation = ViewOrientation.Horizontal,
        };
        timeRow.SetGravity(GravityFlags.CenterVertical);

        _playerCurrentTimeText = MakeText("00:00", 13, Color.Rgb(44, 61, 65), TypefaceStyle.Bold);
        _playerCurrentTimeText.SetIncludeFontPadding(false);
        timeRow.AddView(_playerCurrentTimeText, new LinearLayout.LayoutParams(0, Dp(22), 1));

        _playerDurationText = MakeText("00:00", 13, Color.Rgb(44, 61, 65), TypefaceStyle.Bold);
        _playerDurationText.Gravity = GravityFlags.Right | GravityFlags.CenterVertical;
        _playerDurationText.SetIncludeFontPadding(false);
        timeRow.AddView(_playerDurationText, new LinearLayout.LayoutParams(0, Dp(22), 1));

        page.AddView(timeRow, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(22)));

        return page;
    }

    private TextView MakeText(string text, float sp, Color color, TypefaceStyle style)
    {
        var view = new TextView(this)
        {
            Text = text,
            TextSize = sp,
        };
        view.SetIncludeFontPadding(true);
        view.SetTextColor(color);
        view.SetTypeface(Typeface.Default, style);
        return view;
    }

    private string BuildHelpText()
    {
        return "按键说明\n\n"
            + "2 / 方向上：上一行\n"
            + "8 / 方向下：下一行\n"
            + "5 / OK：播放或暂停当前歌曲\n"
            + "4 / 方向左：上一首\n"
            + "6 / 方向右：下一首\n"
            + "*：快退 10 秒\n"
            + "#：快进 10 秒\n"
            + "1：重新扫描音乐库\n"
            + "3：切换播放页 / 列表页\n"
            + "7：随机播放开关\n"
            + "0：显示或隐藏本页\n"
            + "返回：退出帮助或退出应用";
    }

    private bool HasAudioPermission()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
        {
            return true;
        }

        return CheckSelfPermission(GetAudioPermission()) == Permission.Granted;
    }

    private void ShowPermissionRequest()
    {
        SetEmptyState("KeyPlay 需要读取本机音频文件。请在弹出的权限窗口中选择允许。");

        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
        {
            LoadTracks();
            return;
        }

        RequestPermissions(new[] { GetAudioPermission() }, RequestReadAudio);
    }

    private string GetAudioPermission()
    {
        return Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? PermissionReadMediaAudio
            : PermissionReadExternalStorage;
    }

    private void LoadTracks()
    {
        if (!HasAudioPermission())
        {
            ShowPermissionRequest();
            return;
        }

        _tracks.Clear();

        var projection = new[] { "_id", "title", "artist", "duration", "album_id" };
        using var cursor = ContentResolver?.Query(
            MediaStore.Audio.Media.ExternalContentUri!,
            projection,
            "is_music != 0",
            null,
            "title COLLATE NOCASE ASC");

        if (cursor != null)
        {
            while (cursor.MoveToNext())
            {
                var id = cursor.GetLong(0);
                var title = ReadCursorString(cursor, 1, "未知曲目");
                var artist = ReadCursorString(cursor, 2, "未知歌手");
                var duration = Math.Max(0, cursor.GetLong(3));
                var albumId = cursor.IsNull(4) ? -1 : cursor.GetLong(4);
                var uri = Android.Content.ContentUris.WithAppendedId(MediaStore.Audio.Media.ExternalContentUri!, id);
                _tracks.Add(new Track(title, artist, duration, uri, albumId));
            }
        }

        if (_selectedIndex >= _tracks.Count)
        {
            _selectedIndex = Math.Max(0, _tracks.Count - 1);
        }

        _adapter?.NotifyDataSetChanged();
        SyncListSelection();

        if (_tracks.Count == 0)
        {
            SetEmptyState("没有找到本地音乐。\n请把 mp3、flac、m4a 等音频放到 Music 或 Download 文件夹，然后按 1 重新扫描。");
            SetStatus("空音乐库");
        }
        else
        {
            if (_playerPageVisible)
            {
                SetPlayerPageState();
            }
            else
            {
                SetListState();
            }

            SetStatus($"找到 {_tracks.Count} 首");
            if (_playingIndex < 0)
            {
                UpdateSelectedAsNow();
            }
        }
    }

    private static string ReadCursorString(Android.Database.ICursor cursor, int column, string fallback)
    {
        return cursor.IsNull(column) ? fallback : cursor.GetString(column) ?? fallback;
    }

    private void TogglePlayback()
    {
        if (_helpVisible)
        {
            ToggleHelp();
            return;
        }

        if (_tracks.Count == 0)
        {
            LoadTracks();
            return;
        }

        if (_player == null || _playingIndex != _selectedIndex || !_playerReady)
        {
            PlaySelected();
            return;
        }

        if (_player.IsPlaying)
        {
            _player.Pause();
            SetStatus("已暂停");
        }
        else
        {
            _player.Start();
            SetStatus("正在播放");
        }

        UpdateNowPlaying();
        RefreshMediaControls();
        UpdatePlayerPage();
    }

    private void PausePlayback()
    {
        if (_playerReady && _player?.IsPlaying == true)
        {
            _player.Pause();
            SetStatus("已暂停");
            RefreshMediaControls();
            UpdatePlayerPage();
        }
    }

    private void PlaySelected()
    {
        if (_tracks.Count == 0)
        {
            LoadTracks();
            return;
        }

        _selectedIndex = WrapIndex(_selectedIndex);
        PlayTrack(_selectedIndex);
    }

    private void PlayRelative(int direction)
    {
        if (_tracks.Count == 0)
        {
            LoadTracks();
            return;
        }

        if (_shuffleEnabled)
        {
            _selectedIndex = direction < 0 ? GetPreviousShuffleIndex() : GetNextShuffleIndex();
        }
        else
        {
            _selectedIndex = WrapIndex((_playingIndex >= 0 ? _playingIndex : _selectedIndex) + direction);
        }

        SyncListSelection();
        PlayTrack(_selectedIndex);
    }

    private void PlayNextAuto()
    {
        if (_tracks.Count == 0)
        {
            return;
        }

        _selectedIndex = _shuffleEnabled
            ? GetNextShuffleIndex()
            : WrapIndex((_playingIndex >= 0 ? _playingIndex : _selectedIndex) + 1);
        SyncListSelection();
        PlayTrack(_selectedIndex);
    }

    private void PlayTrack(int index)
    {
        if (index < 0 || index >= _tracks.Count)
        {
            return;
        }

        ReleasePlayer();
        RememberShuffleHistory(index);
        _playingIndex = index;
        _playerReady = false;
        _isPreparing = true;

        var track = _tracks[index];
        _player = new MediaPlayer();
        _player.SetAudioAttributes(new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Music)!
            .Build());
        _player.SetDataSource(this, track.Uri);
        _player.SetOnPreparedListener(this);
        _player.SetOnCompletionListener(this);
        _player.SetOnErrorListener(this);
        _player.PrepareAsync();

        UpdateNowPlaying();
        SetStatus("加载中");
        RefreshMediaControls();
        UpdatePlayerPage();
        _adapter?.NotifyDataSetChanged();
    }

    private void ReleasePlayer()
    {
        if (_player == null)
        {
            return;
        }

        try
        {
            _player.SetOnPreparedListener(null);
            _player.SetOnCompletionListener(null);
            _player.SetOnErrorListener(null);
            _player.Stop();
        }
        catch
        {
            // MediaPlayer throws when Stop is called before preparation; releasing is still correct.
        }
        finally
        {
            _player.Release();
            _player.Dispose();
            _player = null;
            _playerReady = false;
            _isPreparing = false;
        }
    }

    private void SeekBy(int deltaMs)
    {
        if (!_playerReady || _player == null)
        {
            return;
        }

        var target = Math.Clamp(_player.CurrentPosition + deltaMs, 0, Math.Max(0, _player.Duration - 250));
        _player.SeekTo(target);
        UpdateProgress();
        RefreshMediaControls();
    }

    private void SeekToPosition(long positionMs)
    {
        if (!_playerReady || _player == null)
        {
            return;
        }

        var target = (int)Math.Clamp(positionMs, 0, Math.Max(0, _player.Duration - 250));
        _player.SeekTo(target);
        UpdateProgress();
        RefreshMediaControls();
    }

    private void MoveSelection(int delta)
    {
        if (_helpVisible || _playerPageVisible)
        {
            return;
        }

        if (_tracks.Count == 0)
        {
            LoadTracks();
            return;
        }

        _selectedIndex = WrapIndex(_selectedIndex + delta);
        SyncListSelection();
        UpdateSelectedAsNow();
        _adapter?.NotifyDataSetChanged();
    }

    private int GetNextShuffleIndex()
    {
        if (_tracks.Count <= 1)
        {
            return WrapIndex(_playingIndex >= 0 ? _playingIndex : _selectedIndex);
        }

        var current = _playingIndex >= 0 ? _playingIndex : _selectedIndex;
        var next = current;
        for (var i = 0; i < 8 && next == current; i++)
        {
            next = _random.Next(_tracks.Count);
        }

        return next == current ? WrapIndex(current + 1) : next;
    }

    private int GetPreviousShuffleIndex()
    {
        if (_shuffleHistory.Count > 1)
        {
            _shuffleHistory.RemoveAt(_shuffleHistory.Count - 1);
            return _shuffleHistory[^1];
        }

        return WrapIndex((_playingIndex >= 0 ? _playingIndex : _selectedIndex) - 1);
    }

    private void RememberShuffleHistory(int index)
    {
        if (!_shuffleEnabled)
        {
            _shuffleHistory.Clear();
            return;
        }

        if (_shuffleHistory.Count == 0 || _shuffleHistory[^1] != index)
        {
            _shuffleHistory.Add(index);
        }

        if (_shuffleHistory.Count > 50)
        {
            _shuffleHistory.RemoveAt(0);
        }
    }

    private void ToggleShuffle()
    {
        _shuffleEnabled = !_shuffleEnabled;
        _shuffleHistory.Clear();
        if (_shuffleEnabled && _playingIndex >= 0)
        {
            _shuffleHistory.Add(_playingIndex);
        }

        SetStatus(_shuffleEnabled ? "随机已开" : "随机已关");
        UpdatePlayerPage();
        _adapter?.NotifyDataSetChanged();
    }

    private int WrapIndex(int index)
    {
        if (_tracks.Count == 0)
        {
            return 0;
        }

        return (index % _tracks.Count + _tracks.Count) % _tracks.Count;
    }

    private void SyncListSelection()
    {
        _list?.SetSelection(_selectedIndex);
        _list?.SetItemChecked(_selectedIndex, true);
    }

    private void TogglePlayerPage()
    {
        if (_helpVisible)
        {
            ToggleHelp();
        }

        if (_tracks.Count == 0)
        {
            LoadTracks();
            return;
        }

        _playerPageVisible = !_playerPageVisible;
        if (_playerPageVisible)
        {
            SetPlayerPageState();
        }
        else
        {
            SetListState();
        }
    }

    private void ToggleHelp()
    {
        _helpVisible = !_helpVisible;
        if (_helpVisible)
        {
            _nowPanel!.Visibility = ViewStates.Gone;
            _helpText!.Visibility = ViewStates.Visible;
            _playerPage!.Visibility = ViewStates.Gone;
            _list!.Visibility = ViewStates.Gone;
            _emptyText!.Visibility = ViewStates.Gone;
            _hintText!.Visibility = ViewStates.Gone;
            SetStatus("帮助");
        }
        else if (_tracks.Count == 0)
        {
            SetEmptyState(_emptyText?.Text?.ToString() ?? "");
        }
        else
        {
            if (_playerPageVisible)
            {
                SetPlayerPageState();
            }
            else
            {
                SetListState();
            }

            SetStatus(_playerReady && _player?.IsPlaying == true ? "正在播放" : "就绪");
        }
    }

    private void SetListState()
    {
        _rootView?.SetPadding(Dp(8), Dp(8), Dp(8), Dp(8));
        _nowPanel!.Visibility = ViewStates.Visible;
        _helpText!.Visibility = ViewStates.Gone;
        _emptyText!.Visibility = ViewStates.Gone;
        _playerPage!.Visibility = ViewStates.Gone;
        _list!.Visibility = ViewStates.Visible;
        _hintText!.Visibility = ViewStates.Visible;
        _playerPageVisible = false;
        SyncListSelection();
        _list.RequestFocus();
        UpdatePlayerPage();
        ApplyImmersiveMode();
    }

    private void SetPlayerPageState()
    {
        _rootView?.SetPadding(0, 0, 0, 0);
        _nowPanel!.Visibility = ViewStates.Gone;
        _helpText!.Visibility = ViewStates.Gone;
        _emptyText!.Visibility = ViewStates.Gone;
        _list!.Visibility = ViewStates.Gone;
        _playerPage!.Visibility = ViewStates.Visible;
        _hintText!.Visibility = ViewStates.Gone;
        _playerPageVisible = true;
        _playerPage.RequestFocus();
        UpdatePlayerPage();
        ApplyImmersiveMode();
    }

    private void SetEmptyState(string message)
    {
        _helpVisible = false;
        _nowPanel!.Visibility = ViewStates.Gone;
        _helpText!.Visibility = ViewStates.Gone;
        _playerPage!.Visibility = ViewStates.Gone;
        _list!.Visibility = ViewStates.Gone;
        _hintText!.Visibility = ViewStates.Gone;
        _emptyText!.Text = message;
        _emptyText.Visibility = ViewStates.Visible;
        _emptyText.RequestFocus();
    }

    private void SetStatus(string text)
    {
        if (_statusText != null)
        {
            _statusText.Text = text;
        }

    }

    private void UpdateSelectedAsNow()
    {
        if (_tracks.Count == 0 || _playingIndex >= 0)
        {
            return;
        }

        var track = _tracks[_selectedIndex];
        _nowTitle!.Text = track.Title;
        _nowMeta!.Text = $"{track.Artist}  |  {FormatTime(track.DurationMs)}";
        _progress!.Progress = 0;
        _timeText!.Text = $"00:00 / {FormatTime(track.DurationMs)}";
        UpdatePlayerPage();
    }

    private void UpdateNowPlaying()
    {
        if (_playingIndex < 0 || _playingIndex >= _tracks.Count)
        {
            return;
        }

        var track = _tracks[_playingIndex];
        _nowTitle!.Text = track.Title;
        _nowMeta!.Text = $"{track.Artist}  |  {FormatTime(track.DurationMs)}";
        UpdateProgress();
        UpdatePlayerPage();
    }

    private void UpdateProgress()
    {
        if (_progress == null || _timeText == null)
        {
            return;
        }

        if (!_playerReady || _player == null || _playingIndex < 0 || _playingIndex >= _tracks.Count)
        {
            _progress.Progress = 0;
            if (_playerProgress != null)
            {
                _playerProgress.Progress = 0;
            }

            if (_playerCurrentTimeText != null)
            {
                _playerCurrentTimeText.Text = "00:00";
            }

            return;
        }

        var duration = Math.Max(1, _player.Duration);
        var position = Math.Clamp(_player.CurrentPosition, 0, duration);
        _progress.Progress = (int)(position * 1000L / duration);
        _timeText.Text = $"{FormatTime(position)} / {FormatTime(duration)}";
        if (_playerProgress != null)
        {
            _playerProgress.Progress = _progress.Progress;
        }

        if (_playerCurrentTimeText != null)
        {
            _playerCurrentTimeText.Text = FormatTime(position);
        }

        if (_playerDurationText != null)
        {
            _playerDurationText.Text = FormatTime(duration);
        }
    }

    private void UpdatePlayerPage()
    {
        if (_playerPage == null)
        {
            return;
        }

        var index = _playingIndex >= 0 ? _playingIndex : _selectedIndex;
        if (_tracks.Count == 0 || index < 0 || index >= _tracks.Count)
        {
            if (_playerTitle != null)
            {
                _playerTitle.Text = "未播放";
            }

            if (_playerArtist != null)
            {
                _playerArtist.Text = "未知歌手";
            }

            UpdateCover(null);
            if (_playerCurrentTimeText != null)
            {
                _playerCurrentTimeText.Text = "00:00";
            }

            if (_playerDurationText != null)
            {
                _playerDurationText.Text = "00:00";
            }

            return;
        }

        var track = _tracks[index];
        if (_playerTitle != null)
        {
            _playerTitle.Text = track.Title;
        }

        if (_playerArtist != null)
        {
            _playerArtist.Text = track.Artist;
        }

        if (!_playerReady && _playerCurrentTimeText != null)
        {
            _playerCurrentTimeText.Text = "00:00";
        }

        if (_playerDurationText != null)
        {
            _playerDurationText.Text = FormatTime(track.DurationMs);
        }

        UpdateCover(track);
    }

    private void UpdateCover(Track? track)
    {
        if (_coverView == null)
        {
            return;
        }

        var bitmap = track == null ? null : LoadAlbumArt(track.AlbumId);
        if (bitmap != null)
        {
            _coverView.SetImageBitmap(bitmap);
            _coverView.Background = null;
            ApplyCoverBackground(bitmap);
            return;
        }

        var fallbackBitmap = CreateFallbackCoverBitmap(track);
        _coverView.SetImageDrawable(new BitmapDrawable(Resources, fallbackBitmap));
        ApplyCoverBackground(fallbackBitmap);
    }

    private Bitmap? LoadAlbumArt(long albumId)
    {
        if (albumId < 0)
        {
            return null;
        }

        try
        {
            var albumArtUri = Uri.Parse("content://media/external/audio/albumart");
            if (albumArtUri == null)
            {
                return null;
            }

            var coverUri = Android.Content.ContentUris.WithAppendedId(albumArtUri, albumId);
            using var stream = ContentResolver?.OpenInputStream(coverUri);
            return stream == null ? null : BitmapFactory.DecodeStream(stream);
        }
        catch
        {
            return null;
        }
    }

    private void CreateMediaNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O || _notificationManager == null)
        {
            return;
        }

        var channel = new NotificationChannel(MediaChannelId, "KeyPlay 播放", NotificationImportance.Low)
        {
            Description = "KeyPlay 音乐播放控制",
        };
        channel.SetShowBadge(false);
        channel.SetSound(null, null);
        _notificationManager.CreateNotificationChannel(channel);
    }

    private void RefreshMediaControls()
    {
        if (_tracks.Count == 0 || _playingIndex < 0 || _playingIndex >= _tracks.Count)
        {
            CancelMediaNotification();
            return;
        }

        UpdateMediaSession();
        ShowMediaNotification();
    }

    private void UpdateMediaSession()
    {
        if (_mediaSession == null || _playingIndex < 0 || _playingIndex >= _tracks.Count)
        {
            return;
        }

        var track = _tracks[_playingIndex];
        var metadata = new MediaMetadata.Builder()
            .PutString(MediaMetadata.MetadataKeyTitle, track.Title)
            .PutString(MediaMetadata.MetadataKeyArtist, track.Artist)
            .PutLong(MediaMetadata.MetadataKeyDuration, track.DurationMs);
        var cover = LoadAlbumArt(track.AlbumId);
        if (cover != null)
        {
            metadata.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, cover);
        }

        _mediaSession.SetMetadata(metadata.Build());
        _mediaSession.SetPlaybackState(BuildPlaybackState());
        _mediaSession.Active = true;
    }

    private PlaybackState BuildPlaybackState()
    {
        var position = _playerReady && _player != null ? _player.CurrentPosition : 0L;
        var state = GetPlaybackStateCode();
        var speed = state == PlaybackStateCode.Playing ? 1f : 0f;
        var actions = PlaybackState.ActionPlay
            | PlaybackState.ActionPause
            | PlaybackState.ActionPlayPause
            | PlaybackState.ActionSkipToPrevious
            | PlaybackState.ActionSkipToNext
            | PlaybackState.ActionSeekTo
            | PlaybackState.ActionStop;

        return new PlaybackState.Builder()
            .SetActions(actions)
            .SetState(state, position, speed)
            .Build();
    }

    private PlaybackStateCode GetPlaybackStateCode()
    {
        if (_isPreparing)
        {
            return PlaybackStateCode.Buffering;
        }

        if (_playerReady && _player?.IsPlaying == true)
        {
            return PlaybackStateCode.Playing;
        }

        return _playerReady ? PlaybackStateCode.Paused : PlaybackStateCode.Stopped;
    }

    private void ShowMediaNotification()
    {
        if (_notificationManager == null || _mediaSession == null || _playingIndex < 0 || _playingIndex >= _tracks.Count)
        {
            return;
        }

        var track = _tracks[_playingIndex];
        var isPlaying = _playerReady && _player?.IsPlaying == true;
        var playPauseIcon = isPlaying ? Android.Resource.Drawable.IcMediaPause : Android.Resource.Drawable.IcMediaPlay;
        var playPauseTitle = isPlaying ? "暂停" : "播放";
        var builder = BuildNotificationBuilder()
            .SetSmallIcon(Android.Resource.Drawable.IcMediaPlay)
            .SetContentTitle(track.Title)
            .SetContentText(track.Artist)
            .SetSubText(_shuffleEnabled ? "随机播放" : "顺序播放")
            .SetContentIntent(CreateOpenAppIntent())
            .SetDeleteIntent(CreateMediaCommandIntent(CommandStop, 4))
            .SetOngoing(isPlaying || _isPreparing)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(false)
            .SetLocalOnly(true)
            .SetCategory(Notification.CategoryTransport)
            .SetPriority((int)NotificationPriority.Low)
            .SetVisibility(NotificationVisibility.Public)
            .AddAction(Android.Resource.Drawable.IcMediaPrevious, "上一首", CreateMediaCommandIntent(CommandPrevious, 1))
            .AddAction(playPauseIcon, playPauseTitle, CreateMediaCommandIntent(CommandToggle, 2))
            .AddAction(Android.Resource.Drawable.IcMediaNext, "下一首", CreateMediaCommandIntent(CommandNext, 3));

        var cover = LoadAlbumArt(track.AlbumId);
        if (cover != null)
        {
            builder.SetLargeIcon(cover);
        }

        var style = new Notification.MediaStyle()
            .SetMediaSession(_mediaSession.SessionToken)
            .SetShowActionsInCompactView(0, 1, 2);
        builder.SetStyle(style);

        _notificationManager.Notify(MediaNotificationId, builder.Build());
    }

    private Notification.Builder BuildNotificationBuilder()
    {
        return Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, MediaChannelId)
            : new Notification.Builder(this);
    }

    private PendingIntent CreateOpenAppIntent()
    {
        var intent = new Intent(this, Class);
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        return PendingIntent.GetActivity(this, 0, intent, GetPendingIntentFlags());
    }

    private PendingIntent CreateMediaCommandIntent(string command, int requestCode)
    {
        var intent = new Intent(ActionMediaCommand);
        intent.SetPackage(PackageName);
        intent.PutExtra(ExtraMediaCommand, command);
        return PendingIntent.GetBroadcast(this, requestCode, intent, GetPendingIntentFlags());
    }

    private static PendingIntentFlags GetPendingIntentFlags()
    {
        return Build.VERSION.SdkInt >= BuildVersionCodes.M
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : PendingIntentFlags.UpdateCurrent;
    }

    private void CancelMediaNotification()
    {
        _mediaSession?.SetPlaybackState(new PlaybackState.Builder()
            .SetState(PlaybackStateCode.Stopped, 0, 0)
            .Build());
        if (_mediaSession != null)
        {
            _mediaSession.Active = false;
        }

        _notificationManager?.Cancel(MediaNotificationId);
    }

    private Bitmap CreateFallbackCoverBitmap(Track? track)
    {
        var size = Math.Max(1, Dp(118));
        var bitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!);
        using var canvas = new Canvas(bitmap);
        using var paint = new Paint(PaintFlags.AntiAlias);

        paint.Color = Color.Rgb(18, 56, 64);
        canvas.DrawRect(0, 0, size, size, paint);

        paint.Color = Color.Rgb(54, 129, 132);
        canvas.DrawCircle(size * 0.78f, size * 0.22f, size * 0.34f, paint);
        paint.Color = Color.Rgb(123, 220, 159);
        canvas.DrawCircle(size * 0.18f, size * 0.86f, size * 0.28f, paint);

        paint.Color = Color.White;
        paint.TextAlign = Paint.Align.Center;
        paint.SetTypeface(Typeface.Create(Typeface.Default, TypefaceStyle.Bold));
        paint.TextSize = size * 0.42f;
        canvas.DrawText("♪", size / 2f, size * 0.52f, paint);

        paint.TextSize = size * 0.12f;
        var label = string.IsNullOrWhiteSpace(track?.Artist) ? "KeyPlay" : track!.Artist;
        if (label.Length > 12)
        {
            label = label[..12];
        }

        canvas.DrawText(label, size / 2f, size * 0.78f, paint);
        return bitmap;
    }

    private void ApplyCoverBackground(Bitmap bitmap)
    {
        _playerPage?.SetPalette(GetLightFlowPalette(bitmap));
    }

    private LightFlowPalette GetLightFlowPalette(Bitmap bitmap)
    {
        var width = Math.Max(1, bitmap.Width);
        var height = Math.Max(1, bitmap.Height);
        var stepX = Math.Max(1, width / 16);
        var stepY = Math.Max(1, height / 16);
        var bestHue = 200f;
        var bestScore = -1f;
        long red = 0;
        long green = 0;
        long blue = 0;
        var count = 0;

        for (var y = stepY / 2; y < height; y += stepY)
        {
            for (var x = stepX / 2; x < width; x += stepX)
            {
                var color = new Color(bitmap.GetPixel(x, y));
                RgbToHsl(color.R, color.G, color.B, out var hue, out var saturation, out var lightness);
                var chromaScore = saturation * (1f - Math.Abs(lightness - 0.52f));
                if (chromaScore > bestScore)
                {
                    bestHue = hue;
                    bestScore = chromaScore;
                }

                red += color.R;
                green += color.G;
                blue += color.B;
                count++;
            }
        }

        if (bestScore < 0.08f)
        {
            bestHue = count == 0
                ? 190f
                : GetNeutralHue((int)(red / count), (int)(green / count), (int)(blue / count));
        }

        var secondaryHue = (bestHue + 34f) % 360f;
        var tertiaryHue = (bestHue + 318f) % 360f;
        return new LightFlowPalette(
            HslToColor(bestHue, 0.26f, 0.80f),
            HslToColor(bestHue, 0.40f, 0.88f),
            HslToColor(secondaryHue, 0.34f, 0.90f),
            HslToColor(tertiaryHue, 0.32f, 0.86f),
            Color.Argb(170, 255, 255, 255));
    }

    private static float GetNeutralHue(int red, int green, int blue)
    {
        if (blue >= red && blue >= green)
        {
            return 205f;
        }

        if (green >= red)
        {
            return 158f;
        }

        return 28f;
    }

    private static void RgbToHsl(int red, int green, int blue, out float hue, out float saturation, out float lightness)
    {
        var r = red / 255f;
        var g = green / 255f;
        var b = blue / 255f;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        lightness = (max + min) / 2f;
        if (delta <= 0.0001f)
        {
            hue = 0f;
            saturation = 0f;
            return;
        }

        saturation = delta / (1f - Math.Abs(2f * lightness - 1f));
        if (Math.Abs(max - r) <= 0.0001f)
        {
            hue = 60f * (((g - b) / delta) % 6f);
        }
        else if (Math.Abs(max - g) <= 0.0001f)
        {
            hue = 60f * (((b - r) / delta) + 2f);
        }
        else
        {
            hue = 60f * (((r - g) / delta) + 4f);
        }

        if (hue < 0f)
        {
            hue += 360f;
        }
    }

    private static Color HslToColor(float hue, float saturation, float lightness)
    {
        var chroma = (1f - Math.Abs(2f * lightness - 1f)) * saturation;
        var x = chroma * (1f - Math.Abs((hue / 60f % 2f) - 1f));
        var m = lightness - chroma / 2f;
        var sector = (int)(hue / 60f);
        var r1 = 0f;
        var g1 = 0f;
        var b1 = 0f;

        switch (sector)
        {
            case 0:
                r1 = chroma;
                g1 = x;
                break;
            case 1:
                r1 = x;
                g1 = chroma;
                break;
            case 2:
                g1 = chroma;
                b1 = x;
                break;
            case 3:
                g1 = x;
                b1 = chroma;
                break;
            case 4:
                r1 = x;
                b1 = chroma;
                break;
            default:
                r1 = chroma;
                b1 = x;
                break;
        }

        return Color.Rgb(
            Math.Clamp((int)((r1 + m) * 255f), 0, 255),
            Math.Clamp((int)((g1 + m) * 255f), 0, 255),
            Math.Clamp((int)((b1 + m) * 255f), 0, 255));
    }

    private void AdjustVolume(Adjust direction)
    {
        var audio = (AudioManager?)GetSystemService(AudioService);
        audio?.AdjustStreamVolume(AudioStream.Music, direction, VolumeNotificationFlags.ShowUi);
    }

    private static string FormatTime(long ms)
    {
        var totalSeconds = Math.Max(0, ms / 1000);
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    private int Dp(int value)
    {
        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        return (int)(value * density + 0.5f);
    }

    private int GetPlayerCoverSize()
    {
        var widthPixels = Resources?.DisplayMetrics?.WidthPixels ?? Dp(240);
        var maxByWidth = Math.Max(Dp(180), widthPixels - Dp(36));
        return Math.Min(Dp(300), maxByWidth);
    }

    private sealed record Track(string Title, string Artist, long DurationMs, Uri Uri, long AlbumId);

    private readonly record struct LightFlowPalette(Color Surface, Color Primary, Color Secondary, Color Tertiary, Color Highlight);

    private sealed class PlayerPageLayout : LinearLayout
    {
        private readonly Paint _paint = new(PaintFlags.AntiAlias);
        private readonly RunnableAction _invalidateAction;
        private LightFlowPalette _palette = new(
            Color.Rgb(203, 220, 222),
            Color.Rgb(182, 228, 220),
            Color.Rgb(236, 212, 193),
            Color.Rgb(184, 212, 232),
            Color.Argb(170, 255, 255, 255));
        private long _lastFrameMs;
        private float _flowOffset;

        public PlayerPageLayout(Context context) : base(context)
        {
            SetWillNotDraw(false);
            _invalidateAction = new RunnableAction(() =>
            {
                var now = Java.Lang.JavaSystem.CurrentTimeMillis();
                if (_lastFrameMs == 0)
                {
                    _lastFrameMs = now;
                }

                var elapsedMs = Math.Clamp(now - _lastFrameMs, 0, 120);
                _lastFrameMs = now;
                _flowOffset = (_flowOffset + elapsedMs / 6000f) % 1f;
                Invalidate();
                PostDelayed(_invalidateAction, 32);
            });
            Post(_invalidateAction);
        }

        public void SetPalette(LightFlowPalette palette)
        {
            _palette = palette;
            Invalidate();
        }

        protected override void OnDraw(Canvas? canvas)
        {
            if (canvas == null)
            {
                base.OnDraw(canvas);
                return;
            }

            var width = Math.Max(1, Width);
            var height = Math.Max(1, Height);
            canvas.DrawColor(_palette.Surface);

            DrawGlow(canvas, width * 0.18f, height * 0.18f, width * 0.72f, _palette.Primary);
            DrawGlow(canvas, width * 0.92f, height * 0.62f, width * 0.78f, _palette.Secondary);
            DrawGlow(canvas, width * 0.18f, height * 0.94f, width * 0.58f, _palette.Tertiary);

            DrawFlowBand(canvas, width, height, _flowOffset);

            base.OnDraw(canvas);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RemoveCallbacks(_invalidateAction);
                _paint.Dispose();
            }

            base.Dispose(disposing);
        }

        private void DrawGlow(Canvas canvas, float x, float y, float radius, Color color)
        {
            _paint.SetShader(new RadialGradient(
                x,
                y,
                Math.Max(1f, radius),
                WithAlpha(color, 178),
                WithAlpha(color, 0),
                Shader.TileMode.Clamp!));
            canvas.DrawCircle(x, y, radius, _paint);
            _paint.SetShader(null);
        }

        private void DrawFlowBand(Canvas canvas, int width, int height, float offset)
        {
            var travel = width + height;
            var phase = offset;
            var alpha = (int)(MathF.Sin(phase * MathF.PI) * _palette.Highlight.A);
            if (alpha <= 0)
            {
                return;
            }

            var center = (phase * 2f - 0.5f) * travel;
            var bandWidth = Math.Max(width * 0.32f, 80f);
            var bandLength = MathF.Sqrt(width * width + height * height) * 1.4f;

            canvas.Save();
            canvas.Rotate(32f, width / 2f, height / 2f);
            _paint.SetShader(null);
            _paint.Color = WithAlpha(_palette.Highlight, alpha);
            canvas.DrawRoundRect(
                center - bandWidth / 2f,
                -height * 0.7f,
                center + bandWidth / 2f,
                -height * 0.7f + bandLength,
                bandWidth / 2f,
                bandWidth / 2f,
                _paint);
            canvas.Restore();
        }

        private static Color WithAlpha(Color color, int alpha)
        {
            return Color.Argb(alpha, color.R, color.G, color.B);
        }
    }

    private sealed class RunnableAction : Java.Lang.Object, Java.Lang.IRunnable
    {
        private readonly Action _action;

        public RunnableAction(Action action)
        {
            _action = action;
        }

        public void Run()
        {
            _action();
        }
    }

    private sealed class MediaSessionCallback : MediaSession.Callback
    {
        private readonly MainActivity _activity;

        public MediaSessionCallback(MainActivity activity)
        {
            _activity = activity;
        }

        public override void OnPlay()
        {
            _activity.RunOnUiThread(_activity.TogglePlayback);
        }

        public override void OnPause()
        {
            _activity.RunOnUiThread(_activity.PausePlayback);
        }

        public override void OnSkipToPrevious()
        {
            _activity.RunOnUiThread(() => _activity.PlayRelative(-1));
        }

        public override void OnSkipToNext()
        {
            _activity.RunOnUiThread(() => _activity.PlayRelative(1));
        }

        public override void OnStop()
        {
            _activity.RunOnUiThread(_activity.PausePlayback);
        }

        public override bool OnMediaButtonEvent(Intent mediaButtonIntent)
        {
            if (mediaButtonIntent.GetParcelableExtra(Intent.ExtraKeyEvent) is not KeyEvent keyEvent)
            {
                return base.OnMediaButtonEvent(mediaButtonIntent);
            }

            if (keyEvent.Action != KeyEventActions.Down)
            {
                return true;
            }

            switch (keyEvent.KeyCode)
            {
                case Keycode.MediaPrevious:
                    _activity.RunOnUiThread(() => _activity.PlayRelative(-1));
                    return true;

                case Keycode.MediaNext:
                    _activity.RunOnUiThread(() => _activity.PlayRelative(1));
                    return true;

                case Keycode.MediaPlay:
                case Keycode.MediaPause:
                case Keycode.MediaPlayPause:
                case Keycode.Headsethook:
                    _activity.RunOnUiThread(_activity.TogglePlayback);
                    return true;
            }

            return base.OnMediaButtonEvent(mediaButtonIntent);
        }

        public override void OnSeekTo(long pos)
        {
            _activity.RunOnUiThread(() => _activity.SeekToPosition(pos));
        }
    }

    private sealed class MediaCommandReceiver : BroadcastReceiver
    {
        private readonly MainActivity _activity;

        public MediaCommandReceiver(MainActivity activity)
        {
            _activity = activity;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == ActionMediaCommand)
            {
                _activity.HandleMediaCommand(intent.GetStringExtra(ExtraMediaCommand));
            }
        }
    }

    private sealed class TrackAdapter : BaseAdapter<Track>
    {
        private readonly MainActivity _activity;

        public TrackAdapter(MainActivity activity)
        {
            _activity = activity;
        }

        public override int Count => _activity._tracks.Count;

        public override Track this[int position] => _activity._tracks[position];

        public override long GetItemId(int position) => position;

        public override View GetView(int position, View? convertView, ViewGroup? parent)
        {
            var row = convertView as LinearLayout;
            TextView title;
            TextView meta;

            if (row == null)
            {
                row = new LinearLayout(_activity)
                {
                    Orientation = ViewOrientation.Vertical,
                    Focusable = false,
                };
                row.SetPadding(_activity.Dp(8), _activity.Dp(5), _activity.Dp(8), _activity.Dp(5));
                row.SetMinimumHeight(_activity.Dp(54));

                title = _activity.MakeText("", 17, Color.White, TypefaceStyle.Bold);
                title.SetSingleLine(true);
                title.Ellipsize = TextUtils.TruncateAt.End;
                title.Id = 1;
                row.AddView(title, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

                meta = _activity.MakeText("", 12, Color.Rgb(174, 198, 202), TypefaceStyle.Normal);
                meta.SetSingleLine(true);
                meta.Ellipsize = TextUtils.TruncateAt.End;
                meta.Id = 2;
                row.AddView(meta, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            }
            else
            {
                title = row.FindViewById<TextView>(1)!;
                meta = row.FindViewById<TextView>(2)!;
            }

            var track = this[position];
            var selected = position == _activity._selectedIndex;
            var playing = position == _activity._playingIndex;
            title.Text = $"{(selected ? "> " : "  ")}{(playing ? "* " : "")}{track.Title}";
            meta.Text = $"{track.Artist}  |  {FormatTime(track.DurationMs)}";

            if (selected)
            {
                row.SetBackgroundColor(Color.Rgb(28, 83, 95));
            }
            else if (playing)
            {
                row.SetBackgroundColor(Color.Rgb(18, 56, 43));
            }
            else
            {
                row.SetBackgroundColor(Color.Rgb(7, 18, 22));
            }

            return row;
        }
    }
}
