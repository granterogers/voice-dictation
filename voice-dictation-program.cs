// ============================================================================
// VoiceDictation — Win+\ to record, auto-stops on silence, transcribes & polishes
// C# / .NET 8 / Windows Forms / NAudio
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;

namespace VoiceDictation;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "VoiceDictation_SingleInstance", out bool isNew);
        if (!isNew) return;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApp());
    }
}

// ============================================================================
// Settings
// ============================================================================
class AppSettings
{
    public string SoundStart { get; set; } = "Speech On.wav";
    public string SoundDone { get; set; } = "Speech Off.wav";
    public string SoundError { get; set; } = "Windows Foreground.wav";
    public bool SoundsEnabled { get; set; } = true;
    public float OverlayFontSize { get; set; } = 10.5f;
    public int OverlayBgR { get; set; } = 24;
    public int OverlayBgG { get; set; } = 24;
    public int OverlayBgB { get; set; } = 28;
    public int OverlayOpacity { get; set; } = 95;
    public int TextColorR { get; set; } = 230;
    public int TextColorG { get; set; } = 230;
    public int TextColorB { get; set; } = 230;
    public int StatusColorR { get; set; } = 120;
    public int StatusColorG { get; set; } = 180;
    public int StatusColorB { get; set; } = 255;
    public int InputDeviceIndex { get; set; } = -1;
    public string PolishPrompt { get; set; } =
        "You are a dictation cleanup assistant. Fix punctuation, capitalization, and sentence structure. Do light rephrasing for clarity but keep the user's tone and meaning exactly. Output ONLY the cleaned text, nothing else.";

    public static readonly string MediaDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    public Color OverlayBgColor => Color.FromArgb(OverlayBgR, OverlayBgG, OverlayBgB);
    public Color TextColor => Color.FromArgb(TextColorR, TextColorG, TextColorB);
    public Color StatusColor => Color.FromArgb(StatusColorR, StatusColorG, StatusColorB);
    public string SoundStartPath => Path.Combine(MediaDir, SoundStart);
    public string SoundDonePath => Path.Combine(MediaDir, SoundDone);
    public string SoundErrorPath => Path.Combine(MediaDir, SoundError);

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");
    private static string SettingsPathAlt => Path.Combine(Directory.GetCurrentDirectory(), "settings.json");

    public static AppSettings Load()
    {
        foreach (var p in new[] { SettingsPath, SettingsPathAlt })
        {
            if (File.Exists(p))
            {
                try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p)) ?? new(); }
                catch { }
            }
        }
        return new();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            try { File.WriteAllText(SettingsPath, json); }
            catch { File.WriteAllText(SettingsPathAlt, json); }
        }
        catch { }
    }
}