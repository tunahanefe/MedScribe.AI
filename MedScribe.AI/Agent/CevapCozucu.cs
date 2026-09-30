namespace MedScribe.AI.Agent;

/// <summary>
/// Hekimin onay/seçim sorularına verdiği kısa cevabı (ses veya klavye) yorumlar.
/// Eşleşmeler ASCII'ye katlanmış kelimeler üzerinde yapılır; "Evet." / "evet" / "EVET" aynıdır.
/// </summary>
public static class CevapCozucu
{
    /// <summary>Daha uzun ifadeler cevap sayılmaz (onay sırasında sürdürülen dikte yanlışlıkla "evet" olmasın).</summary>
    private const int MaxKelime = 4;

    private static readonly HashSet<string> Olumsuz = new(StringComparer.Ordinal)
    {
        "h", "hayir", "yok", "olmaz", "olmasin", "iptal", "vazgec", "vazgectim", "istemiyorum", "gec", "atla",
        "ekleme", "eklenmesin", "yazma", "yazilmasin", "kaydetme", "kaydedilmesin", "cikma",
    };

    private static readonly HashSet<string> Olumlu = new(StringComparer.Ordinal)
    {
        "e", "evet", "tamam", "olur", "onay", "onayla", "onayliyorum", "kaydet", "ekle", "eklensin", "yaz", "yazilsin",
        "dogru", "tabii", "tabi", "aynen", "cik",
    };

    private static readonly string[] SiraSayilari =
        ["birinci", "ikinci", "ucuncu", "dorduncu", "besinci", "altinci", "yedinci", "sekizinci", "dokuzuncu", "onuncu"];

    /// <summary>true = evet, false = hayır, null = anlaşılmadı.</summary>
    public static bool? EvetHayir(string cevap)
    {
        var kelimeler = Kelimeler(cevap);
        if (kelimeler.Length is 0 or > MaxKelime) return null;

        // Olumsuz önce: "hayır, tamam kalsın" / "evet... yok hayır" kararsızlığında kayıt yapılmasın.
        if (kelimeler.Any(Olumsuz.Contains)) return false;
        if (kelimeler.Any(Olumlu.Contains)) return true;
        return null;
    }

    /// <summary>"2", "iki", "ikinci", "ikinciyi", "ilki", "numara 3" → sayı; bulunamazsa null.</summary>
    public static int? Sayi(string cevap)
    {
        var kelimeler = Kelimeler(cevap);
        if (kelimeler.Length is 0 or > MaxKelime) return null;

        foreach (var k in kelimeler)
        {
            if (int.TryParse(k, out var rakam)) return rakam;
            if (k.StartsWith("ilk", StringComparison.Ordinal)) return 1;
            for (var i = SiraSayilari.Length - 1; i >= 0; i--)          // "onuncu" "on"dan önce denenmeli
                if (k.StartsWith(SiraSayilari[i], StringComparison.Ordinal)) return i + 1;
            if (TurkceMetin.SayiKelimesi(k) is { } sayi) return sayi;
        }
        return null;
    }

    private static string[] Kelimeler(string cevap) =>
        TurkceMetin.Katla(TurkceMetin.Sadelestir(cevap)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
