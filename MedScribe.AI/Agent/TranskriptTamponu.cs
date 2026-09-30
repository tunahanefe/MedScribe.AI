using System.Text.RegularExpressions;

namespace MedScribe.AI.Agent;

/// <summary>Muayene boyunca dikte edilen cümleleri tutar. Ajan komutları buraya hiç girmez.</summary>
public sealed partial class TranskriptTamponu
{
    private readonly List<string> _cumleler = [];

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex CumleSonuRegex();

    public IReadOnlyList<string> Cumleler => _cumleler;

    public bool BosMu => _cumleler.Count == 0;

    /// <summary>
    /// Tek ifadede birden fazla cümle varsa ayrı ayrı eklenir; böylece "son cümleyi sil" gerçekten son cümleyi siler.
    /// İfade bir anamnez başlığıyla başlıyorsa ("fizik muayene: ...") etiket ifadenin tüm cümlelerine uygulanır.
    /// </summary>
    public void Ekle(string metin)
    {
        var (etiket, govde) = AnamnezBasligi.Ayir(metin);
        foreach (var cumle in CumleSonuRegex().Split(govde))
        {
            var temiz = cumle.Trim();
            if (temiz.Length > 0) _cumleler.Add(etiket is null ? temiz : $"{etiket} {temiz}");
        }
    }

    public string? SonCumleyiSil()
    {
        if (_cumleler.Count == 0) return null;

        var son = _cumleler[^1];
        _cumleler.RemoveAt(_cumleler.Count - 1);
        return son;
    }

    public string? SonKelimeyiSil()
    {
        if (_cumleler.Count == 0) return null;

        var son = _cumleler[^1];
        var bosluk = son.TrimEnd().LastIndexOf(' ');
        if (bosluk < 0)
        {
            _cumleler.RemoveAt(_cumleler.Count - 1);
            return son;
        }

        var kalan = son[..bosluk].TrimEnd();
        if (kalan.StartsWith('[') && kalan.EndsWith(']')) _cumleler.RemoveAt(_cumleler.Count - 1);   // yalnızca başlık etiketi kaldı
        else _cumleler[^1] = kalan;
        return son[(bosluk + 1)..];
    }

    public string BirlesikMetin() => string.Join(Environment.NewLine, _cumleler);
}
