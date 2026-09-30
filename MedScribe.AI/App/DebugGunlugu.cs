using MedScribe.AI.Configuration;
using Microsoft.Extensions.Options;

namespace MedScribe.AI.App;

/// <summary>
/// Geliştirici debug çıktısı: MedScribe:Debug:Aktif ise konsola (gri) yazar,
/// DosyayaYaz ise ayrıca logs/medscribe-debug-yyyyMMdd.log dosyasına ekler.
/// </summary>
public sealed class DebugGunlugu(IOptions<DebugOptions> options, TimeProvider zaman)
{
    private readonly DebugOptions _options = options.Value;
    private readonly object _kilit = new();

    public bool Aktif => _options.Aktif;

    public void Yaz(string baslik, string icerik)
    {
        if (!_options.Aktif) return;

        Konsol.Yaz(ConsoleColor.DarkGray, $"[debug] ── {baslik} ──");
        if (icerik.Length > 0) Konsol.Yaz(ConsoleColor.DarkGray, icerik);

        if (!_options.DosyayaYaz) return;

        var simdi = zaman.GetLocalNow();
        var klasor = Path.IsPathRooted(_options.LogKlasoru)
            ? _options.LogKlasoru
            : Path.Combine(AppContext.BaseDirectory, _options.LogKlasoru);

        try
        {
            lock (_kilit)
            {
                Directory.CreateDirectory(klasor);
                File.AppendAllText(
                    Path.Combine(klasor, $"medscribe-debug-{simdi:yyyyMMdd}.log"),
                    $"[{simdi:HH:mm:ss}] ── {baslik} ──{Environment.NewLine}{icerik}{Environment.NewLine}");
            }
        }
        catch (IOException ex)
        {
            Konsol.Uyari($"Debug log dosyasına yazılamadı: {ex.Message}");
        }
    }
}
