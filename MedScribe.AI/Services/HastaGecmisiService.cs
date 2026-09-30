using System.Globalization;
using System.Text.RegularExpressions;
using MedScribe.AI.Configuration;
using MedScribe.AI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedScribe.AI.Services;

/// <summary>
/// "Muayeneyi tamamla" sırasında LLM'e verilecek hasta geçmişini toplar:
/// aynı uzmanlıktaki son vizitlerin anamnezi, geçmiş tanılar (aday listesi), son ilaçlar ve referans dışı lab sonuçları.
/// Tablolar çok büyük olduğundan (LaboratuarSonuc ~430M, Hizmet ~420M satır) tüm sorgular Hasta_ID ile daraltılır.
/// </summary>
public sealed class HastaGecmisiService(
    IDbContextFactory<MedScribeDbContext> dbFactory,
    IOptions<TaniAnaliziOptions> options,
    TimeProvider zaman)
{
    private const int ReddedildiHizmetDurumu = 9;
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private readonly TaniAnaliziOptions _options = options.Value;
    private readonly IReadOnlyList<IcdAraligi> _haricAraliklar = options.Value.HaricIcdAraliklari.Select(IcdAraligi.Coz).ToList();

    public async Task<HastaGecmisi> GetirAsync(MuayeneBaglami baglam, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var bugun = zaman.GetLocalNow().DateTime;
        var maske = AdMaskesi(baglam.HastaAdSoyad);

        var vizitler = await VizitleriGetirAsync(db, baglam, maske, ct);
        var (adaylar, haricler) = await TanilariGetirAsync(db, baglam, ct);
        var ilaclar = await IlaclariGetirAsync(db, baglam.HastaId, bugun.AddDays(-_options.IlacGunSayisi), ct);
        var lablar = await AnormalLablariGetirAsync(db, baglam.HastaId, bugun.AddDays(-_options.LabGunSayisi), ct);

        return new HastaGecmisi(vizitler, adaylar, haricler, ilaclar, lablar);
    }

    private async Task<IReadOnlyList<GecmisVizit>> VizitleriGetirAsync(
        MedScribeDbContext db, MuayeneBaglami baglam, IReadOnlyList<string> maske, CancellationToken ct)
    {
        var protokolId = baglam.ProtokolId ?? 0;
        var satirlar = await db.Database.SqlQuery<VizitSatiri>($"""
            SELECT TOP ({_options.GecmisVizitAdedi}) p.AcilisTarihi AS Tarih, b.Adi AS Birim,
                   (SELECT s.Aciklama FROM Muayene_MuayeneSonuc s WHERE s.Muayene_ID = p.ID AND s.MuayeneSonucTur_ID = 2) AS Sikayet,
                   (SELECT s.Aciklama FROM Muayene_MuayeneSonuc s WHERE s.Muayene_ID = p.ID AND s.MuayeneSonucTur_ID = 1) AS Hikayesi,
                   (SELECT s.Aciklama FROM Muayene_MuayeneSonuc s WHERE s.Muayene_ID = p.ID AND s.MuayeneSonucTur_ID = 4) AS TedaviPlani
            FROM Protokol p
            JOIN HastaKart k ON k.ID = p.HastaKart_ID
            JOIN Birim b ON b.ID = p.Bririm_ID
            WHERE k.Hasta_ID = {baglam.HastaId} AND p.ID <> {protokolId}
              AND ({baglam.UzmanlikDalId} IS NULL OR b.UzmanlikDal_ID = {baglam.UzmanlikDalId})
              AND EXISTS (SELECT 1 FROM Muayene_MuayeneSonuc s
                          WHERE s.Muayene_ID = p.ID AND s.MuayeneSonucTur_ID IN (1, 2, 4) AND LEN(s.Aciklama) > 3)
            ORDER BY p.AcilisTarihi DESC
            """).ToListAsync(ct);

        return satirlar
            .Select(v => new GecmisVizit(v.Tarih, v.Birim?.Trim(), Temizle(v.Sikayet, maske), Temizle(v.Hikayesi, maske), Temizle(v.TedaviPlani, maske)))
            .ToList();
    }

    private async Task<(IReadOnlyList<GecmisTani> Adaylar, IReadOnlyList<GecmisTani> Haricler)> TanilariGetirAsync(
        MedScribeDbContext db, MuayeneBaglami baglam, CancellationToken ct)
    {
        var satirlar = await db.Database.SqlQuery<TaniSatiri>($"""
            SELECT t.ID AS TaniId, t.SkrsKodu AS IcdKodu, t.Adi AS TaniAdi, COUNT(*) AS Adet,
                   SUM(CASE WHEN b.UzmanlikDal_ID = {baglam.UzmanlikDalId} THEN 1 ELSE 0 END) AS AyniUzmanlikAdet,
                   MAX(p.AcilisTarihi) AS SonTarih
            FROM HastaTani ht
            JOIN Protokol p ON p.ID = ht.Protokol_ID
            JOIN HastaKart k ON k.ID = p.HastaKart_ID
            JOIN Tani t ON t.ID = ht.Tani_ID
            LEFT JOIN Birim b ON b.ID = p.Bririm_ID
            WHERE k.Hasta_ID = {baglam.HastaId} AND t.SkrsKodu IS NOT NULL
            GROUP BY t.ID, t.SkrsKodu, t.Adi
            """).ToListAsync(ct);

        var tanilar = satirlar
            .Select(t => new GecmisTani(t.TaniId, t.IcdKodu.Trim(), t.TaniAdi?.Trim(), t.Adet, t.AyniUzmanlikAdet, t.SonTarih))
            .OrderByDescending(t => t.AyniUzmanlikAdet > 0)
            .ThenByDescending(t => t.Adet)
            .ThenByDescending(t => t.SonTarih)
            .ToList();

        var haricler = _options.HaricKodlariDahilEt
            ? []
            : tanilar.Where(t => _haricAraliklar.Any(a => a.Icerir(t.IcdKodu))).ToList();

        var adaylar = tanilar.Except(haricler).Take(_options.AdayTaniAdedi).ToList();
        return (adaylar, haricler);
    }

    private static async Task<IReadOnlyList<GecmisIlac>> IlaclariGetirAsync(
        MedScribeDbContext db, int hastaId, DateTime baslangic, CancellationToken ct)
    {
        var satirlar = await db.Database.SqlQuery<IlacSatiri>($"""
            SELECT TOP (20) il.IlacAdi, il.AtcAdi AS EtkenMadde, COUNT(*) AS Adet, MAX(r.Tarih) AS SonTarih
            FROM ReceteIlac2 rx
            JOIN Recete r ON r.ID = rx.Recete_ID
            JOIN Protokol p ON p.ID = r.Protokol_ID
            JOIN HastaKart k ON k.ID = p.HastaKart_ID
            JOIN Ilac il ON il.ID = rx.Ilac_ID
            WHERE k.Hasta_ID = {hastaId} AND r.Tarih >= {baslangic}
            GROUP BY il.IlacAdi, il.AtcAdi
            ORDER BY MAX(r.Tarih) DESC
            """).ToListAsync(ct);

        return satirlar.Select(i => new GecmisIlac(i.IlacAdi?.Trim(), i.EtkenMadde?.Trim(), i.Adet, i.SonTarih)).ToList();
    }

    /// <summary>Uzman onaylı, referans aralığı tanımlı sonuçlardan her testin en son değeri; yalnızca aralık dışındakiler.</summary>
    private static async Task<IReadOnlyList<AnormalLab>> AnormalLablariGetirAsync(
        MedScribeDbContext db, int hastaId, DateTime baslangic, CancellationToken ct)
    {
        var satirlar = await db.Database.SqlQuery<LabSatiri>($"""
            SELECT ls.SonucTarihi AS Tarih, te.Adi AS Test, ls.Sonuc, ls.OlcuBirimi,
                   CAST(ls.AltLimit AS float) AS AltLimit, CAST(ls.UstLimit AS float) AS UstLimit
            FROM LaboratuarSonuc ls
            JOIN Hizmet hz ON hz.ID = ls.Hizmet_ID
            JOIN HastaKart k ON k.ID = hz.HastaKart_ID
            JOIN Tetkik te ON te.ID = ls.Tetkik_ID
            WHERE k.Hasta_ID = {hastaId} AND ls.UzmanOnayladi = 1 AND ls.SonucTarihi >= {baslangic}
              AND ls.AltLimit IS NOT NULL AND ls.UstLimit IS NOT NULL
              AND ISNULL(hz.HizmetDurumTur_ID, 0) <> {ReddedildiHizmetDurumu}
            """).ToListAsync(ct);

        return satirlar
            .Where(l => l.Tarih is not null && !string.IsNullOrWhiteSpace(l.Test))
            .GroupBy(l => l.Test!.Trim())
            .Select(g => g.OrderByDescending(l => l.Tarih).First())
            .Select(l => (Satir: l, Deger: SayiCoz(l.Sonuc)))
            .Where(x => x.Deger is { } d && (d < x.Satir.AltLimit || d > x.Satir.UstLimit))
            .Select(x => new AnormalLab(x.Satir.Tarih!.Value, x.Satir.Test!.Trim(), x.Satir.Sonuc!.Trim(), x.Satir.OlcuBirimi?.Trim(),
                x.Satir.AltLimit!.Value, x.Satir.UstLimit!.Value, x.Deger > x.Satir.UstLimit))
            .OrderBy(l => l.Test)
            .ToList();
    }

    private static double? SayiCoz(string? sonuc) =>
        double.TryParse(sonuc?.Trim(), NumberStyles.Number, Tr, out var deger) ? deger : null;

    /// <summary>
    /// Maskelenecek ad/soyad parçaları. Veritabanında zaten maskeli olan ("N*****") parçalar metinde eşleşemeyeceği için atlanır.
    /// </summary>
    private static IReadOnlyList<string> AdMaskesi(string adSoyad) =>
        adSoyad.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length >= 2 && !p.Contains('*')).ToList();

    /// <summary>
    /// Anamnez metinlerinde geçen hasta adı/soyadını "[HASTA]" ile değiştirir; ad modele gönderilmez.
    /// Regex yerine tr-TR karşılaştırması kullanılır: "EMİR"/"Emir"/"emir" eşleşmesi için İ/i/I/ı kuralları gerekir.
    /// </summary>
    private static string AdlariMaskele(string metin, IReadOnlyList<string> parcalar)
    {
        foreach (var parca in parcalar)
        {
            var konum = 0;
            while ((konum = Tr.CompareInfo.IndexOf(metin, parca, konum, CompareOptions.IgnoreCase)) >= 0)
            {
                var son = konum + parca.Length;
                var kelimeSiniri = (konum == 0 || !char.IsLetter(metin[konum - 1])) && (son == metin.Length || !char.IsLetter(metin[son]));
                if (!kelimeSiniri)
                {
                    konum = son;
                    continue;
                }
                metin = string.Concat(metin.AsSpan(0, konum), "[HASTA]", metin.AsSpan(son));
                konum += "[HASTA]".Length;
            }
        }
        return metin;
    }

    private string? Temizle(string? metin, IReadOnlyList<string> maske)
    {
        if (string.IsNullOrWhiteSpace(metin)) return null;

        var temiz = AdlariMaskele(Regex.Replace(metin, @"\s+", " ").Trim(), maske);
        if (temiz.Length <= 3) return null;
        return temiz.Length > _options.VizitMetniMaksimumKarakter ? temiz[.._options.VizitMetniMaksimumKarakter] + "…" : temiz;
    }

    // SqlQuery<T> satır tipleri (kolon adları SELECT takma adlarıyla eşleşir).
    private sealed class VizitSatiri
    {
        public DateTime Tarih { get; set; }
        public string? Birim { get; set; }
        public string? Sikayet { get; set; }
        public string? Hikayesi { get; set; }
        public string? TedaviPlani { get; set; }
    }

    private sealed class TaniSatiri
    {
        public int TaniId { get; set; }
        public string IcdKodu { get; set; } = "";
        public string? TaniAdi { get; set; }
        public int Adet { get; set; }
        public int AyniUzmanlikAdet { get; set; }
        public DateTime? SonTarih { get; set; }
    }

    private sealed class IlacSatiri
    {
        public string? IlacAdi { get; set; }
        public string? EtkenMadde { get; set; }
        public int Adet { get; set; }
        public DateTime? SonTarih { get; set; }
    }

    private sealed class LabSatiri
    {
        public DateTime? Tarih { get; set; }
        public string? Test { get; set; }
        public string? Sonuc { get; set; }
        public string? OlcuBirimi { get; set; }
        public double? AltLimit { get; set; }   // SQL'de float'a çevrilir (ad-hoc SqlQuery tipinde decimal hassasiyeti ayarlanamıyor)
        public double? UstLimit { get; set; }
    }
}

/// <summary>"Z00-Z13" gibi bir ICD aralığı veya "R69" gibi tekil kod; karşılaştırma kodun ilk 3 karakteri üzerinden yapılır.</summary>
public readonly record struct IcdAraligi(string Baslangic, string Bitis)
{
    public static IcdAraligi Coz(string tanim)
    {
        var parcalar = tanim.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var bas = Kok(parcalar[0]);
        return new IcdAraligi(bas, parcalar.Length > 1 ? Kok(parcalar[1]) : bas);
    }

    public bool Icerir(string icdKodu)
    {
        var kok = Kok(icdKodu);
        return string.CompareOrdinal(kok, Baslangic) >= 0 && string.CompareOrdinal(kok, Bitis) <= 0;
    }

    private static string Kok(string kod)
    {
        var temiz = kod.Trim().ToUpperInvariant();
        return temiz.Length > 3 ? temiz[..3] : temiz;
    }
}
