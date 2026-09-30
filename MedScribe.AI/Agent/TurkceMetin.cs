using System.Globalization;
using System.Text;

namespace MedScribe.AI.Agent;

public static class TurkceMetin
{
    private static readonly CultureInfo Turkce = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Dictionary<string, int> SayiKelimeleri = new(StringComparer.Ordinal)
    {
        ["bir"] = 1, ["iki"] = 2, ["uc"] = 3, ["dort"] = 4, ["bes"] = 5, ["alti"] = 6, ["yedi"] = 7,
        ["sekiz"] = 8, ["dokuz"] = 9, ["on"] = 10, ["onbes"] = 15, ["yirmi"] = 20, ["otuz"] = 30, ["elli"] = 50,
    };

    /// <summary>Katlanmış sayı kelimesi ("uc", "dort") → değer; sayı değilse null.</summary>
    public static int? SayiKelimesi(string katliKelime) =>
        SayiKelimeleri.TryGetValue(katliKelime, out var sayi) ? sayi : null;

    /// <summary>Türkçe küçük harfe çevirir, noktalamayı atar, boşlukları sadeleştirir. Türkçe karakterler korunur.</summary>
    public static string Sadelestir(string metin)
    {
        var sb = new StringBuilder(metin.Length);
        foreach (var c in metin.ToLower(Turkce))
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>İki kelime arasındaki düzenleme mesafesi (ekleme/silme/değiştirme sayısı).</summary>
    public static int Levenshtein(string a, string b)
    {
        var onceki = new int[b.Length + 1];
        var simdiki = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) onceki[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            simdiki[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var maliyet = a[i - 1] == b[j - 1] ? 0 : 1;
                simdiki[j] = Math.Min(Math.Min(simdiki[j - 1] + 1, onceki[j] + 1), onceki[j - 1] + maliyet);
            }
            (onceki, simdiki) = (simdiki, onceki);
        }
        return onceki[b.Length];
    }

    /// <summary>Yazım/STT hatasına toleranslı kelime eşleşmesi: 6+ harfte en çok 2, daha kısada 1 harf farkı.</summary>
    public static bool Benzer(string kelime, string hedef) =>
        Levenshtein(kelime, hedef) <= (hedef.Length >= 6 ? 2 : 1);

    /// <summary>Komut eşleştirmesi için Türkçe karakterleri ASCII karşılıklarına indirger ("cümleyi" → "cumleyi").</summary>
    public static string Katla(string sadeMetin) => sadeMetin
        .Replace('ı', 'i').Replace('ş', 's').Replace('ç', 'c')
        .Replace('ğ', 'g').Replace('ö', 'o').Replace('ü', 'u')
        .Replace('â', 'a').Replace('î', 'i').Replace('û', 'u');
}
