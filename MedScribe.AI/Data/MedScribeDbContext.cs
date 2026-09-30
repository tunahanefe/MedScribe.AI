using MedScribe.AI.Domain;
using Microsoft.EntityFrameworkCore;

namespace MedScribe.AI.Data;

/// <summary>
/// Mevcut MiaMed HBYS veritabanına eşlenmiş context.
/// UYARI: Şema MiaMed'e aittir. Bu context için migration üretmeyin,
/// EnsureCreated / "dotnet ef database update" çalıştırmayın.
/// </summary>
public sealed class MedScribeDbContext(DbContextOptions<MedScribeDbContext> options) : DbContext(options)
{
    public DbSet<Hasta> Hastalar => Set<Hasta>();
    public DbSet<Basvuru> Basvurular => Set<Basvuru>();
    public DbSet<Protokol> Protokoller => Set<Protokol>();
    public DbSet<Birim> Birimler => Set<Birim>();
    public DbSet<Tani> Tanilar => Set<Tani>();
    public DbSet<TaniTur> TaniTurleri => Set<TaniTur>();
    public DbSet<HastaTani> HastaTanilari => Set<HastaTani>();
    public DbSet<Hizmet> Hizmetler => Set<Hizmet>();
    public DbSet<BasvuruHizmet> BasvuruHizmetleri => Set<BasvuruHizmet>();
    public DbSet<Muayene> Muayeneler => Set<Muayene>();
    public DbSet<MuayeneSonuc> MuayeneSonuclari => Set<MuayeneSonuc>();
    public DbSet<MuayeneGenelBilgi> MuayeneGenelBilgileri => Set<MuayeneGenelBilgi>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("dbo");

        model.Entity<Hasta>(e =>
        {
            e.ToTable("Hasta");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.TcKimlikNo).HasColumnName("TCKimlik").HasPrecision(18, 0);
            e.Property(x => x.Adi).HasColumnName("Adi").HasMaxLength(50);
            e.Property(x => x.Soyadi).HasColumnName("Soyadi").HasMaxLength(50);
            e.Property(x => x.DogumTarihi).HasColumnName("DogumTarihiResmi");
        });

        model.Entity<Basvuru>(e =>
        {
            e.ToTable("HastaKart");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.HastaId).HasColumnName("Hasta_ID");
            e.Property(x => x.KayitTarihi).HasColumnName("KayiTarihi"); // MiaMed kolon adı bu şekilde
            e.Property(x => x.Kapali).HasColumnName("Kapali");
            e.Property(x => x.TaburcuTarihi).HasColumnName("TaburcuTarihi");

            e.HasOne(x => x.Hasta).WithMany(h => h.Basvurular).HasForeignKey(x => x.HastaId);
        });

        model.Entity<Protokol>(e =>
        {
            e.ToTable("Protokol");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.BasvuruId).HasColumnName("HastaKart_ID");
            e.Property(x => x.BirimId).HasColumnName("Bririm_ID"); // MiaMed kolon adı bu şekilde
            e.Property(x => x.DoktorId).HasColumnName("Doktor_ID");
            e.Property(x => x.DefterProtokolNo).HasColumnName("DefterProtokolNo");
            e.Property(x => x.AcilisTarihi).HasColumnName("AcilisTarihi");
            e.Property(x => x.ProtokolTurId).HasColumnName("ProtokolTur_ID");

            e.HasOne(x => x.Basvuru).WithMany(b => b.Protokoller).HasForeignKey(x => x.BasvuruId);
            e.HasOne(x => x.Birim).WithMany(b => b.Protokoller).HasForeignKey(x => x.BirimId);
        });

        model.Entity<Birim>(e =>
        {
            e.ToTable("Birim");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.Adi).HasColumnName("Adi").HasMaxLength(200).IsUnicode(false);
            e.Property(x => x.UzmanlikDalId).HasColumnName("UzmanlikDal_ID");
            e.Property(x => x.BirimTipId).HasColumnName("BirimTip_ID");
        });

        model.Entity<Tani>(e =>
        {
            e.ToTable("Tani");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.IcdKodu).HasColumnName("SkrsKodu").HasMaxLength(15);
            e.Property(x => x.Adi).HasColumnName("Adi").HasMaxLength(250);
            e.Property(x => x.Aktif).HasColumnName("Aktif");
        });

        model.Entity<TaniTur>(e =>
        {
            e.ToTable("TaniTur");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.Adi).HasColumnName("Adi").HasMaxLength(100).IsUnicode(false);
        });

        model.Entity<HastaTani>(e =>
        {
            e.ToTable("HastaTani");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.BasvuruId).HasColumnName("HastaKart_ID");
            e.Property(x => x.ProtokolId).HasColumnName("Protokol_ID");
            e.Property(x => x.TaniId).HasColumnName("Tani_ID");
            e.Property(x => x.TaniTurId).HasColumnName("TaniTur_ID");
            e.Property(x => x.TaniTipId).HasColumnName("TaniTip_ID");
            e.Property(x => x.EklenmeTarihi).HasColumnName("EklenmeTarihi");

            e.HasOne(x => x.Basvuru).WithMany(b => b.Tanilar).HasForeignKey(x => x.BasvuruId);
            e.HasOne(x => x.Protokol).WithMany(p => p.Tanilar).HasForeignKey(x => x.ProtokolId);
            e.HasOne(x => x.Tani).WithMany(t => t.HastaTanilari).HasForeignKey(x => x.TaniId);
            e.HasOne(x => x.TaniTur).WithMany().HasForeignKey(x => x.TaniTurId);
        });

        model.Entity<Hizmet>(e =>
        {
            e.ToTable("Tetkik");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.Adi).HasColumnName("Adi").HasMaxLength(500).IsUnicode(false);
            e.Property(x => x.Kodu).HasColumnName("TetkikKodu").HasMaxLength(15);
            e.Property(x => x.Aktif).HasColumnName("KullanimDurumu");
        });

        model.Entity<BasvuruHizmet>(e =>
        {
            e.ToTable("Hizmet");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.BasvuruId).HasColumnName("HastaKart_ID");
            e.Property(x => x.ProtokolId).HasColumnName("Protokol_ID");
            e.Property(x => x.HizmetId).HasColumnName("Tetkik_ID");
            e.Property(x => x.HizmetDurumTurId).HasColumnName("HizmetDurumTur_ID");
            e.Property(x => x.IsteyenDoktorId).HasColumnName("IsteyenDoktor_ID");
            e.Property(x => x.KaydedenKullaniciId).HasColumnName("KaydedenKullanici_ID");
            e.Property(x => x.Miktar).HasColumnName("Miktar").HasPrecision(18, 2);
            e.Property(x => x.EklemeTarihi).HasColumnName("EklemeTarihi");
            e.Property(x => x.KayitTarihi).HasColumnName("KayitTarihi");
            e.Property(x => x.Aciklama).HasColumnName("Aciklama").HasMaxLength(4000).IsUnicode(false);

            e.HasOne(x => x.Basvuru).WithMany(b => b.Hizmetler).HasForeignKey(x => x.BasvuruId);
            e.HasOne(x => x.Protokol).WithMany(p => p.Hizmetler).HasForeignKey(x => x.ProtokolId);
            e.HasOne(x => x.Hizmet).WithMany(h => h.BasvuruHizmetleri).HasForeignKey(x => x.HizmetId);
        });

        model.Entity<Muayene>(e =>
        {
            e.ToTable("Muayene");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID").ValueGeneratedNever();   // Protokol.ID ile aynı
            e.Property(x => x.DoktorId).HasColumnName("Doktor_ID");
            e.Property(x => x.Tamamlandi).HasColumnName("Tamamlandi");
            e.Property(x => x.KayitTarihi).HasColumnName("KayitTarihi");
            e.Property(x => x.TamamlanmaTarihi).HasColumnName("TamamlanmaTarihi");
        });

        model.Entity<MuayeneSonuc>(e =>
        {
            e.ToTable("Muayene_MuayeneSonuc");
            e.HasKey(x => new { x.MuayeneId, x.MuayeneSonucTurId });
            e.Property(x => x.MuayeneId).HasColumnName("Muayene_ID");
            e.Property(x => x.MuayeneSonucTurId).HasColumnName("MuayeneSonucTur_ID");
            e.Property(x => x.Aciklama).HasColumnName("Aciklama").IsUnicode(false);
        });

        model.Entity<MuayeneGenelBilgi>(e =>
        {
            e.ToTable("Muayene_MuayeneGenelBilgiler");
            e.HasKey(x => new { x.MuayeneId, x.MuayeneGenelBilgilerId });
            e.Property(x => x.MuayeneId).HasColumnName("Muayene_ID");
            e.Property(x => x.MuayeneGenelBilgilerId).HasColumnName("MuayeneGenelBilgiler_ID");
            e.Property(x => x.Deger).HasColumnName("Deger").HasPrecision(18, MuayeneGenelBilgi.DegerOndalikBasamak);
        });
    }
}
