using System.Media;
using System.Reflection;

namespace DumpToTxt.App;

internal static class CompletionSound
{
    private const string ResourceName = "DumpToTxt.App.dumped.wav";
    private static readonly Lazy<SoundPlayer?> Player = new(CreatePlayer);

    public static void Play(bool enabled)
    {
        if (!enabled) return;
        try { Player.Value?.PlaySync(); }
        catch { /* A sound-device problem must never fail a completed dump. */ }
    }

    private static SoundPlayer? CreatePlayer()
    {
        try
        {
            Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null) return null;
            var player = new SoundPlayer(stream);
            player.Load();
            return player;
        }
        catch
        {
            return null;
        }
    }
}
