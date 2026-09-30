using MedScribe.AI.Configuration;
using MedScribe.AI.Data;
using MedScribe.AI.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedScribe.AI.Services;

public sealed class KlinikHizmetService(
    IDbContextFactory<MedScribeDbContext> dbFactory,
    IOptions<KlinikHizmetOptions> options,
    TimeProvider zaman)
{
    private const int MaksimumAday = 5;

    private static readonly HashSet<string> DolguKelimeleri = new(StringComparer.Ordinal)
    {
        "hastaya", "hasta", "için", "lütfen", "bir", "de", "da", "ayrıca", "şimdi", "bide", "bi", "ve",
    };

    private readonly KlinikHizmetOptions _options = options.Value;

    /// <summary>
    /// Sesli ifadeden (örn. "hastaya hemogram") aktif Tetkik tanımlarını arar.
    /// İfade soldan kısaltılarak denenir: "hastaya hemogram" → "hemogram".
    /// </summary>
    public async Task<IReadOnlyList<HizmetAdayi>> AdaylariBulAsync(string sesliIfade, CancellationToken ct)
    {
        var kelimeler = IfadeyiHazirla(sesliIfade);
        if (kelimeler.Length == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        for (var i = 0; i < kelimeler.Length; i++)
        {
            var aranan = string.Join(' ', kelimeler[i..]);
            if (aranan.Length < 3) continue;

            var desen = $"%{aranan}%";
            var adaylar = await db.Hizmetler.AsNoTracking()
                .Where(h => h.Aktif == true && h.Adi != null && EF.Functions.Like(h.Adi, desen)
                            && !h.Adi.StartsWith(">>"))   // ">>BİYOKİMYASAL TETKİKLER<<" gibi grup başlıkları
                .OrderBy(h => h.Adi!.StartsWith(aranan) ? 0 : 1)
                .ThenBy(h => h.Adi!.Length)
                .Take(MaksimumAday)
                .Select(h => new HizmetAdayi(h.Id, h.Adi!.Trim(), h.Kodu))
                .ToListAsync(ct);

            if (adaylar.Count > 0) return adaylar;
        }

        return [];
    }

    /// <summary>Tetkiki başvuruya ekler; oluşan Hizmet.ID değerini döner.</summary>
    public async Task<int> EkleAsync(MuayeneBaglami baglam, HizmetAdayi hizmet, CancellationToken ct)
    {
        var simdi = zaman.GetLocalNow().DateTime;
        var kayit = new BasvuruHizmet
        {
            BasvuruId = baglam.BasvuruId,
            ProtokolId = baglam.ProtokolId,
            HizmetId = hizmet.Id,
            HizmetDurumTurId = _options.HizmetDurumTurId,
            IsteyenDoktorId = _options.IsteyenDoktorId ?? baglam.DoktorId,
            KaydedenKullaniciId = _options.KaydedenKullaniciId,
            Miktar = _options.Miktar,
            EklemeTarihi = simdi,
            KayitTarihi = simdi,
            Aciklama = "MedScribeAI sesli komut ile eklendi",
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.BasvuruHizmetleri.Add(kayit);
        await db.SaveChangesAsync(ct);

        return kayit.Id;
    }

    private string[] IfadeyiHazirla(string sesliIfade)
    {
        var kelimeler = sesliIfade
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SkipWhile(DolguKelimeleri.Contains)
            .ToArray();

        var ifade = string.Join(' ', kelimeler);
        return _options.EsAnlamlilar.TryGetValue(ifade, out var karsilik)
            ? karsilik.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : kelimeler;
    }
}
