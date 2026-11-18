using System.Threading;
using Xunit;

namespace VoiceDictation.Tests;

// The tracker decides when to cut the speaker off, so the cases that matter are the
// ones where it must NOT fire: ambient noise, and Whisper rewording earlier audio.
public class SpeechIdleTrackerTests
{
    [Fact]
    public void There_is_no_word_signal_until_the_first_transcript_arrives()
    {
        var t = new SpeechIdleTracker();

        // Until live transcription returns something, the caller must fall back to
        // audio energy rather than assume silence.
        Assert.False(t.HasWordSignal);
        Assert.Equal(0, t.WordCount);

        t.NoteTranscript("hello");
        Assert.True(t.HasWordSignal);
    }

    [Fact]
    public void New_words_are_reported_and_reset_the_idle_clock()
    {
        var t = new SpeechIdleTracker();

        Assert.True(t.NoteTranscript("one two three"));
        Assert.Equal(3, t.WordCount);

        Assert.True(t.NoteTranscript("one two three four five"));
        Assert.Equal(5, t.WordCount);
        Assert.True(t.SecondsSinceNewWords < 1.0);
    }

    [Fact]
    public void Regression_rewording_without_new_words_does_not_count_as_speech()
    {
        // Whisper re-transcribes the whole clip each pass and often rewords earlier
        // audio. Comparing strings would read that as fresh speech and never time out,
        // which is the behaviour being replaced.
        var t = new SpeechIdleTracker();
        t.NoteTranscript("I am the very model of a modern major general");

        Assert.False(t.NoteTranscript("I am the very model of a modern Major-General"));
        Assert.False(t.NoteTranscript("I am the very model of a modern major general."));
    }

    [Fact]
    public void A_shorter_transcript_does_not_count_as_speech()
    {
        var t = new SpeechIdleTracker();
        t.NoteTranscript("one two three four");

        Assert.False(t.NoteTranscript("one two"));
        Assert.Equal(4, t.WordCount);   // high-water mark is kept
    }

    [Fact]
    public void An_empty_or_whitespace_transcript_counts_as_no_words()
    {
        var t = new SpeechIdleTracker();
        Assert.False(t.NoteTranscript(""));
        Assert.False(t.NoteTranscript("   "));
        Assert.False(t.NoteTranscript(null));
        Assert.Equal(0, t.WordCount);
    }

    [Theory]
    [InlineData("hello there", 2)]
    [InlineData("it's a test", 3)]              // apostrophe stays inside one word
    [InlineData("one, two; three.", 3)]         // punctuation is not a word
    [InlineData("  spaced   out  ", 2)]
    [InlineData("Room 101 now", 3)]             // digits count
    [InlineData("...", 0)]
    public void Words_are_counted_the_way_a_speaker_would_count_them(string text, int expected)
    {
        var t = new SpeechIdleTracker();
        t.NoteTranscript(text);
        Assert.Equal(expected, t.WordCount);
    }

    [Fact]
    public void The_idle_clock_advances_while_no_new_words_arrive()
    {
        var t = new SpeechIdleTracker();
        t.NoteTranscript("one two");

        Thread.Sleep(150);
        // Silence, then a transcript carrying no new words: the clock must keep running.
        t.NoteTranscript("one two");

        Assert.True(t.SecondsSinceNewWords >= 0.1,
            $"expected the idle clock to keep running, got {t.SecondsSinceNewWords:0.000}s");
    }

    [Fact]
    public void Fresh_words_after_a_pause_restart_the_clock()
    {
        var t = new SpeechIdleTracker();
        t.NoteTranscript("one two");
        Thread.Sleep(150);

        Assert.True(t.NoteTranscript("one two three"));
        Assert.True(t.SecondsSinceNewWords < 0.1,
            $"expected the clock to reset, got {t.SecondsSinceNewWords:0.000}s");
    }
}
