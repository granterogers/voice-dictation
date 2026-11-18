using System;
using System.Threading;

// ============================================================================
// Speech idle detection
//
// Extracted from voice-dictation-program.cs so it can be unit tested. Free of
// WinForms, NAudio and network dependencies: the test project compiles this exact
// source file rather than referencing the app.
// ============================================================================

// Decides when the speaker has finished, by watching whether the live transcription
// is still yielding NEW WORDS — not whether the microphone is quiet. Ambient noise
// (a fan, air conditioning, traffic) keeps audio energy above any usable silence
// threshold indefinitely, which is why energy-based stopping never fired in practice.
//
// Written to from the live-preview background tasks and read from the pipeline loop,
// so every field is accessed through Interlocked/Volatile.
internal sealed class SpeechIdleTracker
{
    private long _lastWordsTicks;
    private int _wordCount;
    private int _anyPreview;

    public SpeechIdleTracker() => Touch();

    public void Touch() => Interlocked.Exchange(ref _lastWordsTicks, DateTime.UtcNow.Ticks);

    // True once at least one live transcription has come back. Until then there is no
    // word signal at all and the caller must fall back to audio energy.
    public bool HasWordSignal => Volatile.Read(ref _anyPreview) != 0;

    public int WordCount => Volatile.Read(ref _wordCount);

    public double SecondsSinceNewWords =>
        (DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastWordsTicks), DateTimeKind.Utc)).TotalSeconds;

    // Compares word COUNT rather than the string: Whisper re-transcribes the whole
    // clip each pass and often rewords earlier text slightly, so an exact-match check
    // would read ordinary rewording as fresh speech and never time out.
    public bool NoteTranscript(string text)
    {
        Volatile.Write(ref _anyPreview, 1);

        int n = CountWords(text);
        if (n <= Volatile.Read(ref _wordCount)) return false;

        Volatile.Write(ref _wordCount, n);
        Touch();
        return true;
    }

    private static int CountWords(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        int n = 0;
        bool inWord = false;
        foreach (var c in s)
        {
            bool isWordChar = char.IsLetterOrDigit(c) || c == '\'';
            if (isWordChar && !inWord) { n++; inWord = true; }
            else if (!isWordChar) inWord = false;
        }
        return n;
    }
}
