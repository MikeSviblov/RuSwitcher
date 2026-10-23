using RuSwitcher.Win.Core;
using Xunit;

namespace RuSwitcher.Win.Tests;

/// <summary>The shared dead-key composition rule (shared/testdata/README.md): spacing accent
/// attaches to the NEXT base char as a combining mark, then NFC. Inputs mirror what
/// <c>KeyMapper.ConvertWord</c> collects from ToUnicodeEx for the Greek layout.</summary>
public class DeadKeysTests
{
    [Fact]
    public void Tonos_attaches_to_the_next_vowel_and_precomposes()
    {
        // keys «e ; i n a i» in the Greek layout: ε + spacing tonos + ιναι → είναι (U+03AF, NFC).
        Assert.Equal("είναι", DeadKeys.Compose("ε΄ιναι"));
        Assert.Equal("λόγος", DeadKeys.Compose("λ΄ογος"));
    }

    [Fact]
    public void Composed_form_is_nfc_single_codepoints()
    {
        string s = DeadKeys.Compose("΄ι");
        Assert.Equal("ί", s);
        Assert.Equal(1, s.Length);           // U+03AF, not ι + U+0301
    }

    [Fact]
    public void Dialytika_and_combined_dialytika_tonos_compose()
    {
        Assert.Equal("ϊ", DeadKeys.Compose("¨ι"));        // U+03CA
        Assert.Equal("ΐ", DeadKeys.Compose("΅ι"));        // U+0390
        Assert.Equal("ΐ", DeadKeys.Compose("¨΄ι"));  // stacked accents merge
    }

    [Fact]
    public void Trailing_accent_stays_spacing()
    {
        // A dead key with no following base char keeps its spacing form (vector
        // trailing-tonos-stays-spacing).
        Assert.Equal("ε΄", DeadKeys.Compose("ε΄"));
    }

    [Fact]
    public void Text_without_accents_passes_through()
    {
        Assert.Equal("ειναι", DeadKeys.Compose("ειναι"));
        Assert.Equal("привет", DeadKeys.Compose("привет"));
        Assert.Equal("", DeadKeys.Compose(""));
    }
}
