using System.Text.RegularExpressions;
using MedScribe.AI.Configuration;
using MedScribe.AI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedScribe.AI.Services;

public sealed class TaniService(IDbContextFactory<MedScribeDbContext> dbFactory, IOptions<TaniAnaliziOptions> options)
{
    // En uzun ifade önce: "yüksek tansiyon", "tansiyon"dan önce değiştirilmeli.
    private readonly IReadOnlyList<KeyValuePair<string, string>> _esAnlamlilar =
        options.Value.EsAnlamlilar.OrderByDescending(e => e.Key.Length).ToList();

    public async Task<IReadOnlyList<TaniKaydi>> SonTanilariGetirAsync(int hastaId, int adet, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // HastaTani.Protokol_ID pratikte her kayıtta dolu; Protokol.HastaKart_ID ise NOT NULL.
        return await db.HastaTanilari.AsNoTracking()
            .Where(t => t.Protokol!.Basvuru!.HastaId == hastaId)
            .OrderByDescending(t => t.EklenmeTarihi)
            .ThenByDescending(t => t.Id)
            .Take(adet)
            .Select(t => new TaniKaydi(t.EklenmeTarihi, t.Tani!.IcdKodu, t.Tani.Adi, t.TaniTur!.Adi, t.Protokol!.BasvuruId))
            .ToListAsync(ct);
    }

    private static readonly HashSet<string> AramaDolgulari = new(StringComparer.OrdinalIgnoreCase)
    {
        "ve", "ile", "veya", "diğer", "tanımlanmamış", "başka", "yerde", "sınıflanmamış", "olmayan", "akut", "kronik",
    };

    /// <summary>
    /// Tanı adına göre aktif sözlük kayıtlarını arar: anlamlı kelimelerin hepsi Adi içinde geçmeli.
    /// Önce TaniAnalizi:EsAnlamlilar uygulanır (en uzun ifade önce); hiç sonuç yoksa en uzun kelimeyle tekrar dener.
    /// "akut/kronik" gibi niteleyiciler aramayı daraltmasın diye atlanır.
    /// oncelikliTaniIdler (hastanın geçmiş tanıları) sonuçlarda en üstte gösterilir.
    /// </summary>
    public async Task<IReadOnlyList<TaniSozlukKaydi>> AdaGoreAraAsync(
        string taniAdi, int adet, IReadOnlyCollection<int> oncelikliTaniIdler, CancellationToken ct)
    {
        taniAdi = EsAnlamlilariUygula(taniAdi);
        var kelimeler = taniAdi
            .Split([' ', ',', '(', ')', '/', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => k.Length >= 3 && !AramaDolgulari.Contains(k))
            .ToList();
        if (kelimeler.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        async Task<List<TaniSozlukKaydi>> AraAsync(IEnumerable<string> arananlar)
        {
            var sorgu = db.Tanilar.AsNoTracking().Where(t => t.Aktif == true && t.IcdKodu != null && t.Adi != null);
            foreach (var kelime in arananlar)
            {
                var desen = $"%{kelime}%";
                sorgu = sorgu.Where(t => EF.Functions.Like(t.Adi!, desen));
            }
            return await sorgu
                .OrderBy(t => oncelikliTaniIdler.Contains(t.Id) ? 0 : 1)
                .ThenBy(t => t.Adi!.StartsWith(taniAdi) ? 0 : 1)
                .ThenBy(t => t.Adi!.Length)
                .Take(adet)
                .Select(t => new TaniSozlukKaydi(t.Id, t.IcdKodu!, t.Adi))
                .ToListAsync(ct);
        }

        var sonuc = await AraAsync(kelimeler);
        if (sonuc.Count == 0 && kelimeler.Count > 1)
            sonuc = await AraAsync([kelimeler.OrderByDescending(k => k.Length).First()]);
        return sonuc;
    }

    private string EsAnlamlilariUygula(string taniAdi)
    {
        foreach (var (ifade, karsilik) in _esAnlamlilar)
            taniAdi = Regex.Replace(taniAdi, $@"\b{Regex.Escape(ifade)}\b", karsilik, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return taniAdi;
    }
}
