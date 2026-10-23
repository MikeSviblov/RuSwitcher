#!/usr/bin/env python3
"""COMPLETE collision audit for the short-word (2-letter) frequency signal.

Unlike a top-N corpus check, this enumerates the FULL preimage set: every
2-letter input whose layout image is a listed word in the OTHER language. The
app converts such an input unless the input itself is in its OWN list, so the
only false-convert risk is a REAL word/abbreviation whose image is a listed word
but which is missing from its own list. We check the curated lists against a
reference of real 2-letter tokens and assert closure (exit 1 on any risk).

N languages: the closure property must hold for EVERY ordered pair of languages
that have a list — a word added to one list must not create a false conversion
from any other layout. The audit enumerates all pairs automatically; adding a
language means adding its key map, its curated list and its reference tokens.

Keep the lists below in sync with macos/Sources/RuSwitcher/ShortWords.swift.
"""
import sys
from itertools import permutations

# ---- physical key -> character, per layout --------------------------------
# Keyed by the US-QWERTY base character of the key. Non-letter and dead-key
# positions are simply absent (an image through them is not a 2-letter word;
# the detector's letter veto blocks such inputs anyway).
BASE = "qwertyuiopasdfghjklzxcvbnm"
LAYOUT = {
    "en": dict(zip(BASE, BASE)),
    "ru": dict(zip(BASE, "йцукенгшщзфывапролдячсмить")),
    # Greek (ELOT): q is the Greek question mark «;», the «;» key is the tonos
    # dead key — both excluded. w is the final sigma «ς».
    "el": dict(zip(BASE, "?ςερτυθιοπασδφγηξκλζχψωβνμ")),
}
LAYOUT["el"].pop("q")  # «;» — not a letter

KEY_OF = {lang: {c: k for k, c in m.items()} for lang, m in LAYOUT.items()}

def image(word, src, dst):
    """What the keys of `word` (typed in layout `src`) produce in layout `dst`."""
    out = []
    for ch in word:
        key = KEY_OF[src].get(ch)
        out.append(LAYOUT[dst].get(key, "?") if key else "?")
    return "".join(out)

# ---- lists under test (must match ShortWords.swift) ------------------------
SHORT = {
    "ru": set("не ты на он мы вы да но за бы же из ну по то от их ее её со ли ни об ей "
              "во им ко те та уж ок эй".split()),
    "en": set("to it of is in we me he my on do no be so go if up at as an us or by am "
              "ok hi oh ah um mr ya "
              # Declined ambiguous vs RU (real en tokens, image = listed ru word):
              "vs dj kb jr bp ye ds "
              # Declined ambiguous vs EL (real en tokens, image = listed el word):
              "na ta de ti ki ma oi aw".split()),
    "el": set("να θα το τα τη με σε δε μη αν ως οι ας τι κι μα".split()),
}

# ---- reference of REAL 2-letter tokens (words + common abbreviations) ------
REF = {
    # English: Scrabble TWL two-letter words + abbreviations common in prose.
    "en": set(("aa ab ad ae ag ah ai al am an ar as at aw ax ay ba be bi bo by da de do "
               "ed ef eh el em en er es et ex fa fe gi go ha he hi hm ho id if in is it jo "
               "ka ki la li lo ma me mi mm mo mu my na ne no nu od oe of oh oi om on op or "
               "os ow ox oy pa pe pi po qi re sh si so ta te ti to uh um un up ur us ut we "
               "wo xi xu ya ye yo za "
               "vs dj tv kb mb gb jr sr mr ms dr st pm ds").split()),
    # Russian: common real 2-letter words.
    "ru": set(("не ни но на ну ты вы мы он да за до по из от об во со то та ту те их им ей ею её ее "
               "же ли бы ко уж ок ад ас ум ус юг юр эх ах ох ой эй яд як ял ил ел ем еж").split()),
    # Greek: frequent words, particles, interjections and common colloquialisms.
    "el": set(("να θα το τα τη με σε δε μη αν ως οι ας τι κι μα "
               "ρε ζω δω πω γη ου εν εκ ων αχ ωχ ην ευ").split()),
}

def check(src, dst):
    risks = []
    for t in sorted(REF[src]):
        c = image(t, src, dst)
        if "?" in c:
            continue
        if c in SHORT[dst] and t not in SHORT[src]:
            risks.append((t, c))
    print(f"=== {src}->{dst} (typing real {src}): real tokens that would wrongly convert ===")
    for t, c in risks:
        print(f"  RISK: «{t}» -> «{c}» (image listed, «{t}» not in own list)")
    print(f"  risks: {len(risks)}")
    return len(risks)

total = sum(check(a, b) for a, b in permutations(SHORT, 2))

print("\n=== ambiguous pairs (both listed -> declined, safe) ===")
for a, b in permutations(SHORT, 2):
    if a > b:
        continue
    for w in sorted(SHORT[a]):
        c = image(w, a, b)
        if c in SHORT[b]:
            print(f"  {a} «{w}» <-> {b} «{c}»")

print("\n=== sanity: key conversions still fire ===")
SANITY = [
    ("en", "ru", "yt", "не", True),   # issue #22: «Yt могу»
    ("en", "ru", "ns", "ты", True),
    ("en", "ru", "yf", "на", True),
    ("en", "ru", "lf", "да", True),
    ("en", "el", "ua", "θα", True),   # θα typed on EN converts
    ("en", "el", "mh", "μη", True),
    ("en", "el", "to", "το", False),  # both frequent -> declined
    ("en", "el", "na", "να", False),  # real en token -> declined
]
ok = True
for src, dst, t, expect, want in SANITY:
    c = image(t, src, dst)
    fires = c in SHORT[dst] and t not in SHORT[src]
    good = fires == want and c == expect
    ok &= good
    print(f"  type {src} '{t}' -> {dst} «{c}» converts? {fires} (want {want}) {'OK' if good else 'FAIL'}")

sys.exit(1 if (total or not ok) else 0)
