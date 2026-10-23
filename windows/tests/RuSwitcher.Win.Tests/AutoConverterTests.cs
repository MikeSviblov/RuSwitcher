using System;
using System.Collections.Generic;
using RuSwitcher.Win.Core;
using Xunit;

namespace RuSwitcher.Win.Tests;

/// <summary>Covers the ported <c>LayoutDetector.decide</c> verdict (AutoConverter.ShouldConvertPure)
/// — the trickiest auto-conversion logic — with the dictionary and exception lists injected.</summary>
public class AutoConverterTests
{
    private static readonly ICollection<string> None = new List<string>();

    // A tiny fake dictionary: (word, langTag) pairs that are "real words".
    private static Func<string, string, bool> Dict(params (string word, string tag)[] valid)
    {
        var set = new HashSet<(string, string)>(valid);
        return (w, t) => set.Contains((w, t));
    }

    [Fact]
    public void Converts_gibberish_that_becomes_a_real_word()
    {
        // "ghbdtn" (EN) → "привет" (RU): not a word in en, a word in ru → convert.
        bool r = AutoConverter.ShouldConvertPure("ghbdtn", "привет", "en", "ru",
            caps: false, dictAvailable: true, Dict(("привет", "ru")), None, None);
        Assert.True(r);
    }

    [Fact]
    public void Keeps_a_word_that_is_already_real_in_the_typed_layout()
    {
        // "test" is a real en word; even though it maps to some ru string, keep it.
        bool r = AutoConverter.ShouldConvertPure("test", "етые", "en", "ru",
            caps: false, dictAvailable: true, Dict(("test", "en"), ("етые", "ru")), None, None);
        Assert.False(r);
    }

    [Fact]
    public void Keeps_when_the_flipped_form_is_not_a_real_word()
    {
        bool r = AutoConverter.ShouldConvertPure("qwerty", "йцукен", "en", "ru",
            caps: false, dictAvailable: true, Dict(/* neither is valid */), None, None);
        Assert.False(r);
    }

    [Theory]
    [InlineData("ab")]     // 1–2 letters: too many collisions
    [InlineData("no")]
    public void Keeps_words_shorter_than_three_letters(string typed)
    {
        bool r = AutoConverter.ShouldConvertPure(typed, "хх", "en", "ru",
            caps: false, dictAvailable: true, Dict(("хх", "ru")), None, None);
        Assert.False(r);
    }

    [Fact]
    public void Keeps_tokens_with_non_letters()
    {
        // digits / punctuation → URL/code/email territory, never auto-convert. Digits render
        // the same in both layouts, so BOTH sides stay mixed and the letter veto holds.
        bool r = AutoConverter.ShouldConvertPure("ab12", "фи12", "en", "ru",
            caps: false, dictAvailable: true, Dict(("фи12", "ru")), None, None);
        Assert.False(r);
    }

    [Fact]
    public void Converts_when_only_the_conversion_is_all_letters()
    {
        // macOS letter-veto parity: ЙЦУКЕН's «ж» lives on the «;» key, so the EN image of
        // correct Russian is mixed while the conversion is a clean word («rf;tncz»→«кажется»).
        bool r = AutoConverter.ShouldConvertPure("rf;tncz", "кажется", "en", "ru",
            caps: false, dictAvailable: true, Dict(("кажется", "ru")), None, None);
        Assert.True(r);
    }

    [Fact]
    public void Converts_greek_typed_through_the_tonos_dead_key()
    {
        // «e;inai» (EN image of Greek typed with the tonos dead key) → «είναι».
        bool r = AutoConverter.ShouldConvertPure("e;inai", "είναι", "en", "el",
            caps: false, dictAvailable: true, Dict(("είναι", "el")), None, None);
        Assert.True(r);
    }

    [Fact]
    public void Keeps_typed_with_trailing_punctuation()
    {
        // Trailing non-letter is genuinely ambiguous: «yt.» may be the abbreviation "yt" +
        // full stop, not «нею» (same bail as the macOS ambiguous-suffix walk; issue #35).
        bool r = AutoConverter.ShouldConvertPure("yt.", "нею", "en", "ru",
            caps: false, dictAvailable: true, Dict(("нею", "ru")), None, None);
        Assert.False(r);
    }

    [Fact]
    public void Keeps_greek_question_mark_tail()
    {
        // Correct Greek «τι;» (the Greek question mark is a literal «;») — its EN image "tiq"
        // is no word, and the trailing «;» on the typed side stays vetoed anyway.
        bool r = AutoConverter.ShouldConvertPure("τι;", "tiq", "el", "en",
            caps: false, dictAvailable: true, Dict(), None, None);
        Assert.False(r);
    }

    [Fact]
    public void Keeps_accentless_greek_the_speller_rejects()
    {
        // Chat-style Greek without tonos: the system speller rejects «ειναι» → stays as typed
        // (a future el frequency pack may flip this — vector accentless-greek-undecided-today).
        bool r = AutoConverter.ShouldConvertPure("einai", "ειναι", "en", "el",
            caps: false, dictAvailable: true, Dict(), None, None);
        Assert.False(r);
    }

    [Fact]
    public void Keeps_all_caps_acronyms_when_caps_lock_off()
    {
        bool r = AutoConverter.ShouldConvertPure("USA", "гыф", "en", "ru",
            caps: false, dictAvailable: true, Dict(("гыф", "ru")), None, None);
        Assert.False(r);
    }

    [Fact]
    public void Keeps_camel_case_identifiers()
    {
        bool r = AutoConverter.ShouldConvertPure("myVar", "туМфк", "en", "ru",
            caps: false, dictAvailable: true, Dict(("туМфк", "ru")), None, None);
        Assert.False(r);
    }

    [Fact]
    public void Always_list_forces_conversion_even_for_short_words()
    {
        var always = new List<string> { "жоппа" };
        bool r = AutoConverter.ShouldConvertPure(";jggf", "жоппа", "en", "ru",
            caps: false, dictAvailable: true, Dict(), None, always);
        Assert.True(r);
    }

    [Fact]
    public void Never_list_blocks_a_would_be_conversion()
    {
        var never = new List<string> { "ghbdtn" };
        bool r = AutoConverter.ShouldConvertPure("ghbdtn", "привет", "en", "ru",
            caps: false, dictAvailable: true, Dict(("привет", "ru")), never, None);
        Assert.False(r);
    }

    [Fact]
    public void Without_a_dictionary_it_never_auto_converts()
    {
        bool r = AutoConverter.ShouldConvertPure("ghbdtn", "привет", "en", "ru",
            caps: false, dictAvailable: false, Dict(("привет", "ru")), None, None);
        Assert.False(r);
    }

    [Fact]
    public void Hebrew_converts_only_on_positive_signal_from_the_other_side()
    {
        // Typed in the Hebrew layout, its EN image is a real English word → convert.
        bool r = AutoConverter.ShouldConvertPure("akuo", "hello", "he", "en",
            caps: false, dictAvailable: true, Dict(("hello", "en")), None, None);
        Assert.True(r);
    }

    // --- N-set aggregation (#42): exactly one candidate must claim the word ---

    [Fact]
    public void Aggregation_converts_when_exactly_one_candidate_agrees()
    {
        var candidates = new List<(string, string)> { ("привет", "ru"), ("γηβδτν", "el") };
        int i = AutoConverter.PickAutoTargetIndex("ghbdtn", candidates, "en",
            caps: false, dictAvailable: true, Dict(("привет", "ru")), None, None);
        Assert.Equal(0, i);
    }

    [Fact]
    public void Aggregation_keeps_when_two_candidates_agree()
    {
        // Both renderings are valid words of their languages → ambiguous, do nothing.
        var candidates = new List<(string, string)> { ("привет", "ru"), ("χψηθω", "el") };
        int i = AutoConverter.PickAutoTargetIndex("ghbdtn", candidates, "en",
            caps: false, dictAvailable: true, Dict(("привет", "ru"), ("χψηθω", "el")), None, None);
        Assert.Equal(-1, i);
    }

    [Fact]
    public void Aggregation_keeps_when_no_candidate_agrees()
    {
        var candidates = new List<(string, string)> { ("йцукен", "ru"), ("ςερτυ", "el") };
        int i = AutoConverter.PickAutoTargetIndex("qwerty", candidates, "en",
            caps: false, dictAvailable: true, Dict(), None, None);
        Assert.Equal(-1, i);
    }

    [Fact]
    public void Never_auto_converts_toward_hebrew()
    {
        // Typed on the non-Hebrew side: direction toward Hebrew is unresolvable → keep.
        bool r = AutoConverter.ShouldConvertPure("hello", "אקkf", "en", "he",
            caps: false, dictAvailable: true, Dict(), None, None);
        Assert.False(r);
    }
}
