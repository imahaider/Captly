# Privacy

Captly turns speech into captions **on your own PC**. Nothing you watch or hear is sent anywhere.

## What stays on your PC

| What | Where |
|---|---|
| Audio | Only in memory, for the second or so it takes to caption. It is never saved. |
| Settings | `%APPDATA%\Captly\settings.json` |
| Speech models | `%LOCALAPPDATA%\Captly\models` |
| Logs | `%LOCALAPPDATA%\Captly\logs`. They hold app names, video titles and errors, but no captions or audio. Captly keeps the most recent ones. |
| Transcripts | `Documents\Captly`, or the folder you choose. Turn them off in Settings › Transcripts. |

## When Captly goes online

Captly makes only two kinds of connection, and neither sends anything about what you watch:

1. **Downloading a speech model,** from `download.moonshine.ai`, during setup or when you choose another model in Settings. Each file is checked against a fingerprint built into Captly.
2. **Checking for updates,** by asking GitHub for the newest release of Captly (`api.github.com/repos/imahaider/Captly/releases/latest`), once when Captly starts, and when you press **Check Now**. Turn it off in Settings › General. Captly only tells you an update exists; it never downloads or installs one by itself.

There is no telemetry, no account and no advertising.
