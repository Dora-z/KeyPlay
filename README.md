# KeyPlay

KeyPlay is a small Android local music player designed for keypad-first phones such as the Qin F21 Pro. It can be used without touch input after the Android audio permission is granted.

## Key controls

| Key | Action |
| --- | --- |
| `2` / D-pad up | Move selection up, or raise music volume on the now-playing page |
| `8` / D-pad down | Move selection down, or lower music volume on the now-playing page |
| `5` / OK / headset | Play or pause the selected track |
| `4` / D-pad left | Previous track |
| `6` / D-pad right | Next track |
| `*` | Seek back 10 seconds |
| `#` | Seek forward 10 seconds |
| `1` | Rescan the Android media library |
| `3` | Toggle the now-playing page |
| `7` | Toggle shuffle mode |
| `0` | Toggle help |
| Back | Close help or exit |
| Volume keys | Change music volume |

## Notes

- The app reads songs from Android's media library, so put audio files in common folders such as `Music` or `Download`.
- Android's runtime permission dialog may still require the system UI's own confirmation flow. After permission is granted, normal playback can be done with only hardware keys.
- This project uses .NET for Android and targets `net10.0-android`.

## Build

Install the Android SDK, then run:

```powershell
dotnet build
```
