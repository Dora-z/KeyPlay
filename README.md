# KeyPlay

## 中文

KeyPlay 是一个面向多亲 / Qin F21 Pro 等按键手机的小型 Android 本地音乐播放器。授权读取音频后，日常播放可以只靠实体按键完成。

### 功能

- 扫描 Android 媒体库中的本地音乐
- 按键优先的列表选择、播放、暂停、切歌、快进快退
- 播放页显示歌名、歌手、封面、进度和时间
- 播放页背景会根据封面生成浅色流光效果
- 支持随机播放
- 支持 Android 音乐通知栏和媒体控制
- 适配多亲 F21 Pro 的数字键和方向键操作

### 按键

| 按键 | 功能 |
| --- | --- |
| `2` / 方向上 | 列表页上移；播放页提高媒体音量 |
| `8` / 方向下 | 列表页下移；播放页降低媒体音量 |
| `5` / OK / 耳机键 | 播放或暂停当前歌曲 |
| `4` / 方向左 | 上一首 |
| `6` / 方向右 | 下一首 |
| `*` | 快退 10 秒 |
| `#` | 快进 10 秒 |
| `1` | 重新扫描音乐库 |
| `3` | 切换播放页 / 列表页 |
| `7` | 开关随机播放 |
| `0` | 开关帮助 |
| 返回键 | 关闭帮助或退出 |
| 音量键 | 调整媒体音量 |

### 构建

安装 Android SDK 和 .NET Android 工作负载后运行：

```powershell
dotnet build
```

生成的 APK 位于：

```text
bin/Debug/net10.0-android/
```

## English

KeyPlay is a small Android local music player designed for keypad-first phones such as the Qin F21 Pro. After audio permission is granted, normal playback can be controlled without touch input.

### Features

- Scans local music from Android's media library
- Keypad-first navigation, playback, pause, track switching, seeking
- Now-playing page with title, artist, album art, progress, and time
- Light flowing background generated from the album art
- Shuffle playback
- Android media notification and transport controls
- Numeric-key and D-pad support for Qin F21 Pro

### Key Controls

| Key | Action |
| --- | --- |
| `2` / D-pad up | Move selection up, or raise media volume on the now-playing page |
| `8` / D-pad down | Move selection down, or lower media volume on the now-playing page |
| `5` / OK / headset | Play or pause the current track |
| `4` / D-pad left | Previous track |
| `6` / D-pad right | Next track |
| `*` | Seek back 10 seconds |
| `#` | Seek forward 10 seconds |
| `1` | Rescan the music library |
| `3` | Toggle now-playing page / list page |
| `7` | Toggle shuffle |
| `0` | Toggle help |
| Back | Close help or exit |
| Volume keys | Change media volume |

### Build

Install the Android SDK and the .NET Android workload, then run:

```powershell
dotnet build
```

The generated APK is under:

```text
bin/Debug/net10.0-android/
```
