using MedScribe.AI.Data;
using MedScribe.AI.Domain;
using Microsoft.EntityFrameworkCore;

namespace MedScribe.AI.Services;

public sealed record MuayeneKayitSonucu(int MuayeneId, int YazilanAlan, int YazilanVital, int EklenenTani, int MevcutTani);

/// <summary>Muayene_MuayeneGenelBilgiler'e yazılacak vital değer. Ham: modelin verdiği; Deger: kolona yazılan (yuvarlanmış).</summary>
public sealed record VitalDeger(int TurId, string Baslik, string Birim, double Ham, decimal Deger)
{
    public bool Yuvarlandi => (double)Deger != Ham;
}

/// <summary>
/// Onaylanan medikal raporu MiaMed muayene kaydına yazar:
///   anamnez → dbo.Muayene_MuayeneSonuc (başlık başına tek satır, varsa üzerine yazılır)
///   vitaller → dbo.Muayene_MuayeneGenelBilgiler (tür başına tek satır, varsa üzerine yazılır)
///   tanılar → dbo.HastaTani (aynı protokolde aynı tanı varsa atlanır)
/// Muayene.Tamamlandi alanına dokunulmaz; muayenenin kapatılması MiaMed'de kalır.
/// </summary>
public sealed class MuayeneKayitService(IDbContextFactory<MedScribeDbContext> dbFactory, TimeProvider zaman)
{
    private const int OnTaniTurId = 1;
    private const int KesinTaniTurId = 2;
    private const int MuayeneTanisiTipId = 1;

    /// <summary>Anamnez alanlarının MuayeneSonucTur karşılıkları, MuayeneSonucTur.Sira düzeninde; boş alanlar yazılmaz.</summary>
    public static IReadOnlyList<(int TurId, string Baslik, string Metin)> AnamnezAlanlari(Anamnez a) =>
        new (int, string, string)[]
        {
            (MuayeneSonucTurleri.Sikayeti, "Şikayeti", a.Sikayet),
            (MuayeneSonucTurleri.Hikayesi, "Hikayesi", a.Hikayesi),
            (MuayeneSonucTurleri.Ozgecmis, "Özgeçmiş", a.Ozgecmis),
            (MuayeneSonucTurleri.Soygecmis, "Soygeçmiş", a.Soygecmis),
            (MuayeneSonucTurleri.Bulgular, "Bulgular", a.Bulgular),
            (MuayeneSonucTurleri.FizikiMuayene, "Fiziki Muayene", a.FizikiMuayene),
            (MuayeneSonucTurleri.TedaviPlani, "Tedavi Planı", a.TedaviPlani),
        }
        .Where(x => !string.IsNullOrWhiteSpace(x.Item3))
        .Select(x => (x.Item1, x.Item2, x.Item3.Trim()))
        .ToList();

    /// <summary>
    /// Vital bulguları MuayeneGenelBilgiler türlerine eşler. Fizyolojik aralık dışındaki değerler (STT/model hatası olabilir)
    /// Reddedilenler listesine düşer ve yazılmaz. VKİ yazılmaz (MiaMed hesaplıyor; bkz. MuayeneGenelBilgiTurleri).
    /// </summary>
    public static (IReadOnlyList<VitalDeger> Degerler, IReadOnlyList<string> Reddedilenler) VitalDegerleri(VitalBulgular v)
    {
        var degerler = new List<VitalDeger>();
        var reddedilenler = new List<string>();

        void Ekle(int turId, string baslik, string birim, double? ham, double enAz, double enCok)
        {
            if (ham is not { } d) return;
            if (d < enAz || d > enCok)
            {
                reddedilenler.Add($"{baslik} {d} {birim} (beklenen {enAz}–{enCok})");
                return;
            }
            // SQL CAST ile aynı: yarım değerler sıfırdan uzağa yuvarlanır.
            var deger = Math.Round((decimal)d, MuayeneGenelBilgi.DegerOndalikBasamak, MidpointRounding.AwayFromZero);
            degerler.Add(new VitalDeger(turId, baslik, birim, d, deger));
        }

        Ekle(MuayeneGenelBilgiTurleri.KanBasinciSistolik, "TA sistolik", "mmHg", v.KanBasinciSistolik, 50, 300);
        Ekle(MuayeneGenelBilgiTurleri.KanBasinciDiastolik, "TA diastolik", "mmHg", v.KanBasinciDiastolik, 20, 200);
        Ekle(MuayeneGenelBilgiTurleri.Nabiz, "Nabız", "/dk", v.Nabiz, 20, 250);
        Ekle(MuayeneGenelBilgiTurleri.Ates, "Ateş", "°C", v.Ates, 30, 45);
        Ekle(MuayeneGenelBilgiTurleri.Solunum, "Solunum", "/dk", v.SolunumSayisi, 4, 80);
        Ekle(MuayeneGenelBilgiTurleri.Boy, "Boy", "cm", v.BoyCm, 30, 250);
        Ekle(MuayeneGenelBilgiTurleri.Kilo, "Kilo", "kg", v.KiloKg, 1, 400);

        return (degerler, reddedilenler);
    }

    public async Task<MuayeneKayitSonucu> KaydetAsync(
        MuayeneBaglami baglam, MedikalRapor rapor, IReadOnlyList<KaydedilecekTani> tanilar, CancellationToken ct)
    {
        if (baglam.ProtokolId is not { } protokolId)
            throw new InvalidOperationException("Protokol seçilmeden muayene kaydı yazılamaz.");

        var simdi = zaman.GetLocalNow().DateTime;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strateji = db.Database.CreateExecutionStrategy();

        return await strateji.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // MiaMed protokol açılışında Muayene satırını genelde oluşturur; yoksa biz açarız.
            if (!await db.Muayeneler.AnyAsync(m => m.Id == protokolId, ct))
                db.Muayeneler.Add(new Muayene { Id = protokolId, DoktorId = baglam.DoktorId, Tamamlandi = false, KayitTarihi = simdi });

            var alanlar = AnamnezAlanlari(rapor.Anamnez);
            var turIdler = alanlar.Select(x => x.TurId).ToList();
            var mevcutSonuclar = await db.MuayeneSonuclari.AsTracking()
                .Where(s => s.MuayeneId == protokolId && turIdler.Contains(s.MuayeneSonucTurId))
                .ToDictionaryAsync(s => s.MuayeneSonucTurId, ct);

            foreach (var (turId, _, metin) in alanlar)
            {
                if (mevcutSonuclar.TryGetValue(turId, out var mevcut)) mevcut.Aciklama = metin;
                else db.MuayeneSonuclari.Add(new MuayeneSonuc { MuayeneId = protokolId, MuayeneSonucTurId = turId, Aciklama = metin });
            }

            var (vitaller, _) = VitalDegerleri(rapor.VitalBulgular);
            var vitalTurIdler = vitaller.Select(x => x.TurId).ToList();
            var mevcutVitaller = await db.MuayeneGenelBilgileri.AsTracking()
                .Where(g => g.MuayeneId == protokolId && vitalTurIdler.Contains(g.MuayeneGenelBilgilerId))
                .ToDictionaryAsync(g => g.MuayeneGenelBilgilerId, ct);

            foreach (var vital in vitaller)
            {
                if (mevcutVitaller.TryGetValue(vital.TurId, out var mevcut)) mevcut.Deger = vital.Deger;
                else db.MuayeneGenelBilgileri.Add(new MuayeneGenelBilgi { MuayeneId = protokolId, MuayeneGenelBilgilerId = vital.TurId, Deger = vital.Deger });
            }

            var taniIdler = tanilar.Select(t => t.Tani.Id).ToList();
            var mevcutTaniIdler = await db.HastaTanilari
                .Where(t => t.ProtokolId == protokolId && taniIdler.Contains(t.TaniId))
                .Select(t => t.TaniId)
                .ToListAsync(ct);

            var eklenecekler = tanilar.Where(t => !mevcutTaniIdler.Contains(t.Tani.Id)).DistinctBy(t => t.Tani.Id).ToList();
            foreach (var t in eklenecekler)
            {
                db.HastaTanilari.Add(new HastaTani
                {
                    BasvuruId = baglam.BasvuruId,
                    ProtokolId = protokolId,
                    TaniId = t.Tani.Id,
                    TaniTurId = t.Kesin ? KesinTaniTurId : OnTaniTurId,
                    TaniTipId = MuayeneTanisiTipId,
                    EklenmeTarihi = simdi,
                });
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new MuayeneKayitSonucu(protokolId, alanlar.Count, vitaller.Count, eklenecekler.Count, tanilar.Count - eklenecekler.Count);
        });
    }
}
