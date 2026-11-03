namespace Captly.Core.Shell;

/// <summary>The Settings window's view model: one model per screen.</summary>
public sealed class SettingsModel(
    GeneralPageModel general,
    SourcePageModel source,
    AppearancePageModel appearance,
    PositionPageModel position,
    SpeechPageModel speech,
    TranscriptsPageModel transcripts,
    ShortcutsPageModel shortcuts,
    AboutPageModel about)
{
    public GeneralPageModel General => general;
    public SourcePageModel Source => source;
    public AppearancePageModel Appearance => appearance;
    public PositionPageModel Position => position;
    public SpeechPageModel Speech => speech;
    public TranscriptsPageModel Transcripts => transcripts;
    public ShortcutsPageModel Shortcuts => shortcuts;
    public AboutPageModel About => about;
}
