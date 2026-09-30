using MedScribe.AI.Data;
using Microsoft.EntityFrameworkCore;

namespace MedScribe.AI.Services;

public sealed class PatientService(IDbContextFactory<MedScribeDbContext> dbFactory)
{
    /// <summary>11 haneli giriş önce T.C. Kimlik No olarak, bulunamazsa Hasta No (Hasta.ID) olarak aranır.</summary>
    public async Task<Domain.Hasta?> HastaBulAsync(string giris, CancellationToken ct)
    {
        var deger = giris.Trim();
        if (deger.Length == 0 || !deger.All(char.IsAsciiDigit)) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hastalar = db.Hastalar.AsNoTracking();

        if (deger.Length == 11 && decimal.TryParse(deger, out var tcKimlik))
        {
            var hasta = await hastalar
                .Where(h => h.TcKimlikNo == tcKimlik)
                .OrderByDescending(h => h.Id)
                .FirstOrDefaultAsync(ct);
            if (hasta is not null) return hasta;
        }

        return int.TryParse(deger, out var hastaId)
            ? await hastalar.FirstOrDefaultAsync(h => h.Id == hastaId, ct)
            : null;
    }

    public async Task<IReadOnlyList<BasvuruOzet>> SonBasvurulariGetirAsync(int hastaId, int adet, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await db.Basvurular.AsNoTracking()
            .Where(b => b.HastaId == hastaId)
            .OrderByDescending(b => b.KayitTarihi)
            .Take(adet)
            .Select(b => new BasvuruOzet(
                b.Id,
                b.KayitTarihi,
                b.Kapali == true,
                b.Protokoller.OrderBy(p => p.AcilisTarihi).Select(p => p.Birim!.Adi).ToList()))
            .ToListAsync(ct);
    }

    /// <summary>Hastanın verilen gün açılmış başvuruları (en yeni önce); eski tarihli başvuru seçildiğinde öneri için.</summary>
    public async Task<IReadOnlyList<BasvuruOzet>> GunlukBasvurulariGetirAsync(int hastaId, DateTime gun, CancellationToken ct)
    {
        var baslangic = gun.Date;
        var bitis = baslangic.AddDays(1);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await db.Basvurular.AsNoTracking()
            .Where(b => b.HastaId == hastaId && b.KayitTarihi >= baslangic && b.KayitTarihi < bitis)
            .OrderByDescending(b => b.KayitTarihi)
            .Select(b => new BasvuruOzet(
                b.Id,
                b.KayitTarihi,
                b.Kapali == true,
                b.Protokoller.OrderBy(p => p.AcilisTarihi).Select(p => p.Birim!.Adi).ToList()))
            .ToListAsync(ct);
    }

    public async Task<BasvuruDetay?> BasvuruDetayGetirAsync(int basvuruId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await db.Basvurular.AsNoTracking()
            .Where(b => b.Id == basvuruId)
            .Select(b => new BasvuruDetay(
                b.Id,
                b.KayitTarihi,
                b.Kapali == true,
                b.HastaId,
                b.Hasta!.Adi,
                b.Hasta.Soyadi,
                b.Hasta.DogumTarihi,
                b.Protokoller
                    .OrderByDescending(p => p.AcilisTarihi)
                    .Select(p => new ProtokolOzet(p.Id, p.DefterProtokolNo, p.AcilisTarihi, p.Birim!.Adi, p.DoktorId, p.Birim.UzmanlikDalId,
                        p.Birim.BirimTipId == Domain.Birim.PoliklinikBirimTipId,
                        p.ProtokolTurId == Domain.Protokol.KonsultasyonProtokolTurId))
                    .ToList()))
            .FirstOrDefaultAsync(ct);
    }
}
