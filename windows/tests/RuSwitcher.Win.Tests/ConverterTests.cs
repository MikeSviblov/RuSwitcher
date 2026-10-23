using System;
using System.Collections.Generic;
using RuSwitcher.Win.Core;
using Xunit;

namespace RuSwitcher.Win.Tests;

/// <summary>The N-layout target pick for the manual trigger (issue #42): first
/// dictionary-valid candidate wins, otherwise the first non-current layout — which with
/// two layouts is exactly the old pair behaviour.</summary>
public class ConverterTests
{
    private static Func<string, string, bool> Dict(params (string word, string tag)[] valid)
    {
        var set = new HashSet<(string, string)>(valid);
        return (w, t) => set.Contains((w, t));
    }

    [Fact]
    public void Picks_the_first_dictionary_valid_candidate()
    {
        // «руддщ» typed in RU with el before en in the installed list: the Greek image is
        // gibberish, the English one is a real word — the dictionary must win over list order.
        var candidates = new List<(string, string)> { ("ηελλο", "el"), ("hello", "en") };
        int i = Converter.PickTargetIndex(candidates, dictAvailable: true, Dict(("hello", "en")));
        Assert.Equal(1, i);
    }

    [Fact]
    public void Falls_back_to_list_order_when_nothing_is_valid()
    {
        var candidates = new List<(string, string)> { ("ηελλο", "el"), ("пждды", "ru") };
        int i = Converter.PickTargetIndex(candidates, dictAvailable: true, Dict());
        Assert.Equal(0, i);
    }

    [Fact]
    public void Without_a_dictionary_behaves_like_the_old_pair()
    {
        var candidates = new List<(string, string)> { ("привет", "ru") };
        int i = Converter.PickTargetIndex(candidates, dictAvailable: false, Dict(("привет", "ru")));
        Assert.Equal(0, i);
    }

    [Fact]
    public void Skips_candidates_that_render_to_nothing()
    {
        var candidates = new List<(string, string)> { ("", "el"), ("hello", "en") };
        int i = Converter.PickTargetIndex(candidates, dictAvailable: false, Dict());
        Assert.Equal(1, i);
    }

    [Fact]
    public void Returns_minus_one_when_nothing_renders()
    {
        var candidates = new List<(string, string)> { ("", "el") };
        Assert.Equal(-1, Converter.PickTargetIndex(candidates, dictAvailable: false, Dict()));
    }
}
