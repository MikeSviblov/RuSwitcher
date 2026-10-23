using System;
using System.Collections.Generic;
using RuSwitcher.Win.Core;
using Xunit;

namespace RuSwitcher.Win.Tests;

/// <summary>The per-word verdict of the smart selection conversion over the N-layout set (#42):
/// the direction is decided per word, a word valid in any set language is kept, otherwise the
/// first dictionary-valid rendering wins. Pair precedence (always/never lists) unchanged.</summary>
public class SmartConvertTests
{
    private static readonly ICollection<string> None = new List<string>();
    private static readonly List<string> Tags = new() { "ru", "en", "el" };

    private static Func<string, string, bool> Dict(params (string word, string tag)[] valid)
    {
        var set = new HashSet<(string, string)>(valid);
        return (w, t) => set.Contains((w, t));
    }

    [Fact]
    public void Flips_to_the_first_dictionary_valid_candidate()
    {
        // "ghbdtn" typed in EN wanting RU — candidates carry flips from EVERY source layout.
        var candidates = new List<(string, string, string)> { ("привет", "привет", "ru"), ("γηβδτν", "γηβδτν", "el") };
        string r = SmartConvert.PickWordConversion("ghbdtn", "ghbdtn", Tags, candidates,
            Dict(("привет", "ru")), None, None);
        Assert.Equal("привет", r);
    }

    [Fact]
    public void Keeps_a_word_valid_in_any_set_language()
    {
        // The mixed-selection case: "hello" must survive even when the CURRENT layout is RU —
        // validity is checked against every language of the set, not the current one.
        var candidates = new List<(string, string, string)> { ("руддщ", "руддщ", "ru"), ("ηελλο", "ηελλο", "el") };
        string r = SmartConvert.PickWordConversion("hello", "hello", Tags, candidates,
            Dict(("hello", "en"), ("руддщ", "ru")), None, None);
        Assert.Equal("hello", r);
    }

    [Fact]
    public void Keeps_unresolved_words()
    {
        var candidates = new List<(string, string, string)> { ("йцукен", "йцукен", "ru") };
        string r = SmartConvert.PickWordConversion("qwerty", "qwerty", Tags, candidates, Dict(), None, None);
        Assert.Equal("qwerty", r);
    }

    [Fact]
    public void Never_list_wins_over_the_dictionary()
    {
        var candidates = new List<(string, string, string)> { ("привет", "привет", "ru") };
        string r = SmartConvert.PickWordConversion("ghbdtn", "ghbdtn", Tags, candidates,
            Dict(("привет", "ru")), new List<string> { "ghbdtn" }, None);
        Assert.Equal("ghbdtn", r);
    }

    [Fact]
    public void Always_list_flips_to_that_candidate_without_the_dictionary()
    {
        var candidates = new List<(string, string, string)> { ("жоппа", "жоппа", "ru") };
        string r = SmartConvert.PickWordConversion(";jggf", ";jggf", Tags, candidates,
            Dict(), None, new List<string> { "жоппа" });
        Assert.Equal("жоппа", r);
    }

    [Fact]
    public void Keeps_words_too_short_to_judge()
    {
        var candidates = new List<(string, string, string)> { ("я", "я", "ru") };
        string r = SmartConvert.PickWordConversion("z", "z", Tags, candidates, Dict(("я", "ru")), None, None);
        Assert.Equal("z", r);
    }
}
