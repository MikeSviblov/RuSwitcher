using System;
using System.Collections.Generic;
using RuSwitcher.Win.Core;
using Xunit;

namespace RuSwitcher.Win.Tests;

/// <summary>The per-word verdict of the smart selection conversion against the N-layout
/// candidate set (#42): same ranking as the manual trigger, pair precedence unchanged.</summary>
public class SmartConvertTests
{
    private static readonly ICollection<string> None = new List<string>();

    private static Func<string, string, bool> Dict(params (string word, string tag)[] valid)
    {
        var set = new HashSet<(string, string)>(valid);
        return (w, t) => set.Contains((w, t));
    }

    private static readonly List<(string, string, string)> RuEl = new()
    {
        ("привет", "привет", "ru"), ("γηβδτν", "γηβδτν", "el"),
    };

    [Fact]
    public void Flips_to_the_first_dictionary_valid_candidate()
    {
        string r = SmartConvert.PickWordConversion("ghbdtn", "ghbdtn", "en", RuEl,
            Dict(("привет", "ru")), None, None);
        Assert.Equal("привет", r);
    }

    [Fact]
    public void Keeps_a_word_valid_in_the_source_language()
    {
        // iPhone / стоит: correct source words are never flipped, even if a candidate is valid too.
        string r = SmartConvert.PickWordConversion("hello", "hello", "en",
            new List<(string, string, string)> { ("руддщ", "руддщ", "ru") },
            Dict(("hello", "en"), ("руддщ", "ru")), None, None);
        Assert.Equal("hello", r);
    }

    [Fact]
    public void Keeps_unresolved_words()
    {
        string r = SmartConvert.PickWordConversion("qwerty", "qwerty", "en", RuEl, Dict(), None, None);
        Assert.Equal("qwerty", r);
    }

    [Fact]
    public void Never_list_wins_over_the_dictionary()
    {
        string r = SmartConvert.PickWordConversion("ghbdtn", "ghbdtn", "en", RuEl,
            Dict(("привет", "ru")), new List<string> { "ghbdtn" }, None);
        Assert.Equal("ghbdtn", r);
    }

    [Fact]
    public void Always_list_flips_to_that_candidate_without_the_dictionary()
    {
        string r = SmartConvert.PickWordConversion(";jggf", ";jggf", "en",
            new List<(string, string, string)> { ("жоппа", "жоппа", "ru") },
            Dict(), None, new List<string> { "жоппа" });
        Assert.Equal("жоппа", r);
    }

    [Fact]
    public void Keeps_words_too_short_to_judge()
    {
        string r = SmartConvert.PickWordConversion("z", "z", "en",
            new List<(string, string, string)> { ("я", "я", "ru") }, Dict(("я", "ru")), None, None);
        Assert.Equal("z", r);
    }
}
