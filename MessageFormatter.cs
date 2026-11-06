using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// ============================================================================
// Dictation text formatting
//
// Extracted from voice-dictation-program.cs so it can be unit tested. This file
// is deliberately free of WinForms, NAudio and network dependencies: the test
// project compiles this exact source file rather than referencing the app, so
// there is only ever one copy of the logic.
//
// See tests/VoiceDictation.Tests.
// ============================================================================

internal static class MessageFormatter
{
    internal static readonly string[] SignOffPhrases =
    {
        "best wishes", "best regards", "kind regards", "warm regards", "all the best",
        "many thanks", "thank you so much", "thanks so much", "thank you", "thanks",
        "sincerely", "cheers", "talk soon", "take care", "regards"
    };

    // Phrases that only ever appear as the model talking ABOUT its edit. Deliberately
    // conservative: a stray "Note:" surviving is cosmetic, whereas deleting a sentence
    // the user actually dictated is data loss.
    internal static readonly string[] CommentaryPrefixes =
    {
        "note:", "(note", "note that i", "the above text", "the text above",
        "i cleaned", "i have cleaned", "i fixed", "i have fixed", "i corrected",
        "i have corrected", "changes made", "here is the cleaned", "here's the cleaned",
        "cleaned text:", "corrected text:",
    };

    // Remove meta-commentary from the END only.
    //
    // This previously scanned from the TOP and `break`-ed on the first match, discarding
    // everything after it — and the match list included "i have", "i've", "this is" and
    // "let me know", all ordinary ways to begin a dictated sentence. Since each paragraph
    // sits on its own line, a paragraph starting "Let me know if you need anything"
    // silently deleted the remainder of the message, and the longer the dictation the
    // more paragraphs there were to trip it.
    //
    // onStripped (optional) reports each removed line, so the app can log it without
    // this file taking a dependency on the logger.
    internal static string StripTrailingCommentary(string result, Action<string> onStripped = null)
    {
        if (string.IsNullOrEmpty(result)) return result ?? "";

        var lines = result.Replace("\r\n", "\n").Split('\n').ToList();

        while (lines.Count > 0)
        {
            var last = lines[^1].TrimStart().ToLowerInvariant();
            if (last.Length == 0) { lines.RemoveAt(lines.Count - 1); continue; }
            if (CommentaryPrefixes.Any(p => last.StartsWith(p, StringComparison.Ordinal)))
            {
                onStripped?.Invoke(lines[^1].Trim());
                lines.RemoveAt(lines.Count - 1);
                continue;
            }
            break;
        }

        return string.Join("\n", lines).TrimEnd();
    }

    // Words that can follow a salutation but are not names, so must not be capitalised.
    internal static readonly string[] NonNameGreetingWords = { "there", "again", "all" };

    internal static readonly string[] TransitionWords =
    {
        "but", "so", "however", "then", "also", "thereafter", "additionally", "meanwhile"
    };

    // Deterministic typography cleanup. The AI pass is inconsistent about spacing and
    // capitalization no matter how the prompt is worded (see HANDOVER.md s8), so these
    // fixes are applied in code rather than asked for.
    internal static string NormalizeTypography(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // Collapse runs of spaces/tabs, and strip trailing space on each line.
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" +$", "", RegexOptions.Multiline);

        // No space BEFORE punctuation; exactly one space AFTER it.
        text = Regex.Replace(text, @"\s+([,.!?;:])", "$1");
        text = Regex.Replace(text, @"([,;:])(?=[A-Za-z0-9])", "$1 ");
        // Sentence punctuation. The lookbehind skips initialisms - a period whose letter
        // is itself preceded by a word boundary ("u.s.a.", "a.m.") is not a sentence end.
        text = Regex.Replace(text, @"(?<!\b[A-Za-z])([.!?])(?=[A-Za-z])", "$1 ");

        // Dictation routinely yields a bare lowercase "i".
        text = Regex.Replace(text, @"(?<![A-Za-z'])i(?![A-Za-z'])", "I");

        // Capitalize the first letter of the text, of every line, and of every sentence.
        text = Regex.Replace(text, @"^[a-z]", m => m.Value.ToUpperInvariant());
        text = Regex.Replace(text, @"(?<=\n)([ \t]*)([a-z])",
            m => m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));
        text = Regex.Replace(text, @"(?<!\b[A-Za-z])([.!?]\s+)([a-z])",
            m => m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));

        // Never more than one blank line between paragraphs.
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    // Deterministic fallback formatting so greeting/body/sign-off structure and paragraph
    // breaks always land, regardless of whether the AI cleanup pass applied them.
    internal static string EnforceMessageStructure(string text)
    {
        text = NormalizeTypography(text);
        if (text.Length == 0) return text;

        string greetingLine = null;
        var greetingMatch = Regex.Match(text, @"^(Hi|Hey|Hello|Dear|Greetings)\s+([A-Za-z][A-Za-z'-]{0,30})\s*[,.!]?\s*", RegexOptions.IgnoreCase);
        if (greetingMatch.Success)
        {
            var salutation = greetingMatch.Groups[1].Value;
            var name = greetingMatch.Groups[2].Value;
            // "Hello there" is a greeting, but "there" is not a name — capitalising it
            // would produce "Hello There,". Leave such words lower case.
            var addressee = NonNameGreetingWords.Contains(name, StringComparer.OrdinalIgnoreCase)
                ? name.ToLowerInvariant()
                : $"{char.ToUpper(name[0])}{name.Substring(1)}";
            greetingLine = $"{char.ToUpper(salutation[0])}{salutation.Substring(1).ToLower()} {addressee},";
            text = text.Substring(greetingMatch.Length).TrimStart();
            if (text.Length > 0 && char.IsLower(text[0]))
                text = char.ToUpper(text[0]) + text.Substring(1);
        }

        // Peel the sign-off off the tail BEFORE paragraphing, so it never gets glued
        // onto the last body paragraph.
        string signOffLine = null;
        // The name may follow on a new line ("Thanks,\nGrant") or inline after a comma
        // ("Thanks, Grant") — dictation produces both — so the separator is [,\s]+.
        // Group 1 is still required to equal a known sign-off phrase exactly, which is
        // what stops "Thanks for looking at the drawings" being treated as a sign-off.
        var tailMatch = Regex.Match(text, @"(?:^|(?<=[.!?])\s+|\n)\s*([A-Za-z][A-Za-z ]*?)[,.!]?(?:[,\s]+([A-Z][A-Za-z'-]{1,20}(?: [A-Z][A-Za-z'-]{1,20})?))?\s*[.!]?\s*$");
        if (tailMatch.Success)
        {
            var candidate = tailMatch.Groups[1].Value.Trim();
            foreach (var phrase in SignOffPhrases)
            {
                if (!candidate.Equals(phrase, StringComparison.OrdinalIgnoreCase)) continue;
                var head = $"{char.ToUpper(candidate[0])}{candidate.Substring(1).ToLower()}";
                var name = tailMatch.Groups[2].Success ? tailMatch.Groups[2].Value.Trim() : "";
                signOffLine = name.Length > 0 ? $"{head},\n{name}" : $"{head}.";
                text = text.Substring(0, tailMatch.Index).TrimEnd();
                break;
            }
        }

        // Respect the paragraph breaks the AI pass already produced — the previous
        // version flattened them with string.Join(" ", ...) and re-split from scratch,
        // which is why well-structured output kept coming back as one block.
        var paragraphs = new List<string>();
        foreach (var block in Regex.Split(text, @"\n\s*\n"))
        {
            var flat = Regex.Replace(block.Replace('\n', ' '), @"\s+", " ").Trim();
            if (flat.Length == 0) continue;
            paragraphs.AddRange(SplitIntoParagraphs(flat));
        }

        var result = new List<string>();
        if (greetingLine != null) result.Add(greetingLine);
        result.AddRange(paragraphs);
        if (signOffLine != null) result.Add(signOffLine);

        return result.Count > 0 ? string.Join("\n\n", result) : text;
    }

    // Breaks one dense block into paragraphs of at most 3 sentences, starting a new
    // one at any transition word.
    internal static List<string> SplitIntoParagraphs(string block)
    {
        // Same initialism guard as NormalizeTypography, shifted one char: do not treat
        // the trailing period of "u.s.a." as a sentence boundary.
        var sentences = Regex.Split(block, @"(?<!\b[A-Za-z]\.)(?<=[.!?])\s+")
            .Where(x => x.Trim().Length > 0).ToList();
        var paragraphs = new List<string>();
        var current = new List<string>();

        foreach (var raw in sentences)
        {
            var sentence = raw.Trim();
            var firstWord = Regex.Match(sentence, @"^[A-Za-z]+").Value;
            bool startsTransition = TransitionWords.Contains(firstWord, StringComparer.OrdinalIgnoreCase);
            if (current.Count > 0 && (startsTransition || current.Count >= 3))
            {
                paragraphs.Add(string.Join(" ", current));
                current.Clear();
            }
            current.Add(sentence);
        }
        if (current.Count > 0) paragraphs.Add(string.Join(" ", current));
        return paragraphs;
    }
}
