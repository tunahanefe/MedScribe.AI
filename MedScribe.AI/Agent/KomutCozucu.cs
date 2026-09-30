using System.Text.RegularExpressions;

namespace MedScribe.AI.Agent;

public abstract record SesliKomut;

/// <summary>Komut değil; transkripte eklenecek klinik dikte.</summary>
public sealed record DikteMetni(string Metin) : SesliKomut;

public sealed record SonCumleyiSilKomutu : SesliKomut;
public sealed record SonKelimeyiSilKomutu : SesliKomut;
public sealed record TranskriptiGosterKomutu : SesliKomut;
public sealed record TaniListeleKomutu(int? Adet) : SesliKomut;
/// <summary>"hemogram ve biyokimya tetkiki ekle" → ["hemogram", "biyokimya"]</summary>
public sealed record HizmetEkleKomutu(IReadOnlyList<string> HizmetIfadeleri) : SesliKomut;
public sealed record MuayeneyiTamamlaKomutu : SesliKomut;
public sealed record MuayenedenCikKomutu : SesliKomut;

/// <summary>
/// Hekim ifadesini LLM'e gitmeden, yerel kurallarla komut/dikte olarak sınıflandırır.
/// Komut eşleşmeleri ASCII'ye katlanmış metin üzerinde yapılır; böylece STT'nin "cumleyi"/"cümleyi" farkı sorun olmaz.
/// </summary>
public sealed partial class KomutCozucu
{
    [GeneratedRegex(@"\bson (cumleyi|cumle|kismi|kisim|satiri|yaziyi) sil\b")]
    private static partial Regex SonCumleyiSilRegex();

    [GeneratedRegex(@"\bson kelimeyi sil\b")]
    private static partial Regex SonKelimeyiSilRegex();

    [GeneratedRegex(@"\b(transkripti|notlari|metni) (goster|oku|listele)\b")]
    private static partial Regex TranskriptiGosterRegex();

    // "hastanın aldığı son 10 tanıyı listele", "son on tanıyı göster", "geçmiş tanıları getir"
    [GeneratedRegex(@"\b(?:son (?<adet>\d+|[a-z]+) )?(?:tani|tanilari|taniyi|tanisini|tanilarini)\b.*\b(listele|goster|getir|say|oku)\b")]
    private static partial Regex TaniListeleRegex();

    // "hemogram tetkiki ekle", "hastaya biyokimya hizmeti ekle", "tam kan sayımı testi iste"
    // Türkçe karakterleri korumak için katlanmamış (sadeleştirilmiş) metin üzerinde çalışır.
    [GeneratedRegex(@"(?<ad>[\p{L}\p{N} ]+?) (?:tetkiki|tetkik|tetkikini|hizmeti|hizmet|hizmetini|testi|test|tahlili|tahlil) (?:ekle|iste|istiyorum|isteyelim|yaz)\b")]
    private static partial Regex HizmetEkleRegex();

    [GeneratedRegex(@"\bmuayeneyi (tamamla|bitir|sonlandir|kapat)\b")]
    private static partial Regex TamamlaRegex();

    [GeneratedRegex(@"^(muayeneden cik|muayeneyi iptal et|iptal|cikis)$")]
    private static partial Regex CikRegex();

    private static readonly string[] MuayeneKelimeleri = ["muayeneyi", "muayene"];
    private static readonly string[] TamamlaKelimeleri = ["tamamla", "bitir", "sonlandir", "kapat"];

    /// <summary>
    /// "mueyeneyi tamamla", "muayeneyi tamamlaa" gibi yazım/STT hataları. Yalnızca tam iki kelimelik ifadeler denenir;
    /// böylece uzun dikte cümleleri yanlışlıkla komuta dönmez. Yanlış pozitifte de kayıt öncesi hekim onayı istenir.
    /// </summary>
    private static bool TamamlaBenzeri(string katli)
    {
        var kelimeler = katli.Split(' ');
        return kelimeler.Length == 2
               && MuayeneKelimeleri.Any(k => TurkceMetin.Benzer(kelimeler[0], k))
               && TamamlaKelimeleri.Any(k => TurkceMetin.Benzer(kelimeler[1], k));
    }

    public SesliKomut Coz(string hamMetin)
    {
        var sade = TurkceMetin.Sadelestir(hamMetin);
        var katli = TurkceMetin.Katla(sade);

        if (SonCumleyiSilRegex().IsMatch(katli)) return new SonCumleyiSilKomutu();
        if (SonKelimeyiSilRegex().IsMatch(katli)) return new SonKelimeyiSilKomutu();
        if (TamamlaRegex().IsMatch(katli)) return new MuayeneyiTamamlaKomutu();
        if (CikRegex().IsMatch(katli)) return new MuayenedenCikKomutu();
        if (TamamlaBenzeri(katli)) return new MuayeneyiTamamlaKomutu();
        if (TranskriptiGosterRegex().IsMatch(katli)) return new TranskriptiGosterKomutu();

        var tani = TaniListeleRegex().Match(katli);
        if (tani.Success) return new TaniListeleKomutu(AdetCoz(tani.Groups["adet"].Value));

        var hizmet = HizmetEkleRegex().Match(sade);
        if (hizmet.Success)
        {
            var ifadeler = hizmet.Groups["ad"].Value
                .Split(" ve ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new HizmetEkleKomutu(ifadeler);
        }

        return new DikteMetni(hamMetin.Trim());
    }

    private static int? AdetCoz(string deger)
    {
        if (int.TryParse(deger, out var sayi) && sayi > 0) return sayi;
        return TurkceMetin.SayiKelimesi(deger);
    }
}
