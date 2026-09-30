using System.Text.RegularExpressions;

namespace MedScribe.AI.Agent;

/// <summary>
/// Dikteyi başlık söyleyerek bir anamnez alanına zorlama: "fizik muayene: batın rahat", "soygeçmiş babada HT".
/// İfade bir başlıkla başlıyorsa başlık atılır ve cümleler "[FİZİKİ MUAYENE] ..." etiketiyle transkripte girer;
/// LLM talimatı etiketli satırı yalnızca o alana yazar. Başlık söylenmezse model alanı kendisi seçer.
/// </summary>
public static class AnamnezBasligi
{
    // Anahtarlar katlanmış (ASCII, küçük harf) metindir; etiketler LlmAgentService talimatıyla aynı olmalı.
    private static readonly Dictionary<string, string> Basliklar = new(StringComparer.Ordinal)
    {
        ["fiziki muayene"] = "[FİZİKİ MUAYENE]", ["fizik muayene"] = "[FİZİKİ MUAYENE]",
        ["fizik muayenede"] = "[FİZİKİ MUAYENE]", ["muayene bulgulari"] = "[FİZİKİ MUAYENE]",
        ["soygecmis"] = "[SOYGEÇMİŞ]", ["soygecmisi"] = "[SOYGEÇMİŞ]", ["soy gecmis"] = "[SOYGEÇMİŞ]",
        ["aile oykusu"] = "[SOYGEÇMİŞ]", ["aile hikayesi"] = "[SOYGEÇMİŞ]",
        ["ozgecmis"] = "[ÖZGEÇMİŞ]", ["ozgecmisi"] = "[ÖZGEÇMİŞ]", ["oz gecmis"] = "[ÖZGEÇMİŞ]",
        ["sikayet"] = "[ŞİKAYETİ]", ["sikayeti"] = "[ŞİKAYETİ]", ["sikayetleri"] = "[ŞİKAYETİ]",
        ["hikaye"] = "[HİKAYESİ]", ["hikayesi"] = "[HİKAYESİ]", ["oyku"] = "[HİKAYESİ]", ["oykusu"] = "[HİKAYESİ]",
        ["bulgular"] = "[BULGULAR]", ["bulgulari"] = "[BULGULAR]", ["tetkik sonuclari"] = "[BULGULAR]",
        ["tedavi plani"] = "[TEDAVİ PLANI]", ["plan"] = "[TEDAVİ PLANI]",
    };

    // En uzun ifade önce: "fizik muayenede", "fizik muayene"den önce denenmeli.
    private static readonly Regex BaslikRegex = new(
        @"^(?<baslik>" + string.Join('|', Basliklar.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")\b",
        RegexOptions.Compiled);

    /// <summary>İfade başlıkla başlıyorsa (Etiket, başlıksız metin), değilse (null, metin) döner.</summary>
    public static (string? Etiket, string Metin) Ayir(string hamMetin)
    {
        var eslesme = BaslikRegex.Match(TurkceMetin.Katla(TurkceMetin.Sadelestir(hamMetin)));
        if (!eslesme.Success) return (null, hamMetin);

        // Başlığın kelime sayısı kadar kelimeyi özgün metinden at; ardından gelen ":" "-" gibi ayraçları temizle.
        var baslik = eslesme.Groups["baslik"].Value;
        var kalan = string.Join(' ', hamMetin.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(baslik.Split(' ').Length))
            .TrimStart(':', '-', ',', '.', ' ');

        return kalan.Length == 0 ? (null, hamMetin) : (Basliklar[baslik], kalan);
    }
}
