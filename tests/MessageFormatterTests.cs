using System.Collections.Generic;
using Xunit;

namespace VoiceDictation.Tests;

// Cases are drawn from real dictation output that broke in production, not invented
// examples. Each `Regression` test names the bug it locks down.
public class MessageFormatterTests
{
    private static string Format(string input) =>
        MessageFormatter.EnforceMessageStructure(input);

    // ---------------------------------------------------------------- structure

    [Fact]
    public void Greeting_and_signoff_are_split_onto_their_own_lines()
    {
        var actual = Format(
            "hi sergio thanks for reaching out.if you could kindly provide me a phone " +
            "number to reach you , i will give you a call . best wishes");

        Assert.Equal(
            "Hi Sergio,\n\n" +
            "Thanks for reaching out. If you could kindly provide me a phone number to " +
            "reach you, I will give you a call.\n\n" +
            "Best wishes.",
            actual);
    }

    [Fact]
    public void Signoff_keeps_a_trailing_name_on_its_own_line()
    {
        var actual = Format("Hi Mark, the quote is ready. Thanks, Grant");

        Assert.EndsWith("Thanks,\nGrant", actual);
        Assert.StartsWith("Hi Mark,", actual);
    }

    [Fact]
    public void A_body_sentence_beginning_with_a_signoff_word_is_not_a_signoff()
    {
        // "Thanks for looking" starts with "thanks" but is clearly not a sign-off.
        var actual = Format("Hi Dylan, thanks for looking at the drawings today.");

        Assert.DoesNotContain("Thanks.", actual);
        Assert.Contains("Thanks for looking at the drawings today.", actual);
    }

    [Fact]
    public void Regression_a_salutation_followed_by_a_non_name_is_not_capitalised()
    {
        // "Hello there" used to become "Hello There," — "there" treated as a name.
        var actual = Format("hello there, can you check the drawings");

        Assert.StartsWith("Hello there,", actual);
        Assert.DoesNotContain("Hello There", actual);
    }

    [Fact]
    public void Paragraphs_break_at_transition_words()
    {
        var actual = Format(
            "The panel yield is low. But the optimiser settings look right. " +
            "So I think we should check the saw.");

        Assert.Contains("\n\nBut the optimiser settings look right.", actual);
        Assert.Contains("\n\nSo I think we should check the saw.", actual);
    }

    [Fact]
    public void Short_text_is_left_alone_apart_from_capitalisation()
    {
        Assert.Equal("Ok", Format("ok"));
        Assert.Equal("Sounds good to me.", Format("sounds good to me."));
    }

    // ---------------------------------------------------------------- regressions

    [Fact]
    public void Regression_existing_paragraph_breaks_from_the_AI_are_preserved()
    {
        // The formatter used to split on sentence boundaries ACROSS newlines and rejoin
        // with string.Join(" "), destroying correct structure before re-splitting by its
        // own rule. Well-formatted input must survive unchanged.
        var input =
            "Hi Dylan,\n\n" +
            "Thanks for your patience while I looked over this. Yes, if you're using the " +
            "default saw optimizer, that should include perfect graining.\n\n" +
            "But if you really feel like you have to, switch from the Microvellum sawing " +
            "optimizer to the Cut Right ASCII option.\n\n" +
            "Thanks,\nGrant";

        Assert.Equal(input, Format(input));
    }

    [Fact]
    public void Regression_initialisms_are_not_split_into_sentences()
    {
        // "u.s.a." used to become "u. S. A." and then break the sentence apart.
        var actual = Format(
            "the build is green but i want to double check the u.s.a. region settings");

        Assert.Contains("u.s.a. region settings", actual);
        Assert.DoesNotContain("u. S.", actual);
    }

    [Theory]
    [InlineData("it came in at 12.5 percent over budget", "12.5 percent")]
    [InlineData("can you send it by 3 p.m. tomorrow", "3 p.m. tomorrow")]
    [InlineData("we shipped to the u.k. office", "u.k. office")]
    public void Regression_decimals_and_abbreviations_survive(string input, string expected)
    {
        Assert.Contains(expected, Format(input));
    }

    // ------------------------------------------------- trailing commentary stripping

    private static string Strip(string input) =>
        MessageFormatter.StripTrailingCommentary(input);

    [Theory]
    // All of these are ordinary dictated sentences that the old top-down scanner
    // treated as model commentary, discarding them AND everything after them.
    [InlineData("Let me know if you need anything.")]
    [InlineData("I have attached the drawings.")]
    [InlineData("I've sent the quote over.")]
    [InlineData("This is the revised scope.")]
    [InlineData("I made a start on the millwork.")]
    public void Regression_ordinary_sentences_are_never_mistaken_for_commentary(string sentence)
    {
        var input = "Hi Mark,\n\nHere is the update.\n\n" + sentence;
        Assert.Equal(input, Strip(input));
    }

    [Fact]
    public void Regression_content_after_an_innocent_phrase_is_not_discarded()
    {
        // The exact shape of the bug: a middle paragraph beginning "I have ..." used to
        // delete everything from there to the end.
        var input =
            "Hi Mark,\n\n" +
            "Thanks for the call earlier.\n\n" +
            "I have attached the drawings.\n\n" +
            "The panel yield is still low, so we should revisit the optimiser.\n\n" +
            "Thanks,\nGrant";

        var actual = Strip(input);

        Assert.Equal(input, actual);
        Assert.Contains("revisit the optimiser", actual);
        Assert.EndsWith("Grant", actual);
    }

    [Fact]
    public void Genuine_trailing_commentary_is_removed()
    {
        var actual = Strip("Here is the update.\n\nNote: I fixed the punctuation.");
        Assert.Equal("Here is the update.", actual);
    }

    [Fact]
    public void Commentary_is_only_stripped_from_the_end()
    {
        // A line that looks like commentary but has real content after it must survive,
        // because removing it would take the real content with it.
        var input = "Note: this is part of what I dictated.\n\nAnd here is the rest.";
        Assert.Equal(input, Strip(input));
    }

    [Fact]
    public void Stripping_reports_what_it_removed()
    {
        var removed = new List<string>();
        MessageFormatter.StripTrailingCommentary(
            "Body.\n\nNote: I fixed the spacing.", removed.Add);

        Assert.Equal(new[] { "Note: I fixed the spacing." }, removed);
    }

    // ---------------------------------------------------------------- typography

    [Theory]
    [InlineData("three.testing again", "Three. Testing again")]      // missing space after '.'
    [InlineData("the  quote  is  ready", "The quote is ready")]       // doubled spaces
    [InlineData("wait , what ?", "Wait, what?")]                      // space before punctuation
    [InlineData("i think i can", "I think I can")]                    // bare lowercase i
    public void Typography_is_normalised(string input, string expected)
    {
        Assert.Equal(expected, Format(input));
    }

    [Fact]
    public void Every_sentence_start_is_capitalised()
    {
        var actual = Format("the first one. the second one. the third one.");
        Assert.Equal("The first one. The second one. The third one.", actual);
    }

    // ---------------------------------------------------------------- robustness

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\n")]
    public void Empty_and_whitespace_input_does_not_throw(string input)
    {
        var actual = Format(input);
        Assert.Equal("", actual.Trim());
    }

    [Fact]
    public void A_greeting_with_no_body_does_not_throw()
    {
        Assert.False(string.IsNullOrWhiteSpace(Format("hi sarah")));
    }

    [Fact]
    public void Output_never_has_more_than_one_blank_line_between_paragraphs()
    {
        var actual = Format("Hi Sam,\n\n\n\nHere is the thing.\n\n\n\nThanks");
        Assert.DoesNotContain("\n\n\n", actual);
    }

    [Fact]
    public void Formatting_is_idempotent()
    {
        // The pipeline may re-format already-formatted text; a second pass must be a
        // no-op, otherwise paragraph rules would compound.
        const string input =
            "hi sergio thanks for reaching out.if you could kindly provide me a phone " +
            "number to reach you , i will give you a call . best wishes";

        var once = Format(input);
        Assert.Equal(once, Format(once));
    }
}
