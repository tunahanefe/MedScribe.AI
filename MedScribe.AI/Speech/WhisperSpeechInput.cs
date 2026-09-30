using System.Text;
using System.Threading.Channels;
using MedScribe.AI.Agent;
using MedScribe.AI.App;
using MedScribe.AI.Configuration;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace MedScribe.AI.Speech;

/// <summary>
/// Gerçek mikrofon + yerel Whisper (large-v3-turbo) transkripsiyonu.
/// Mikrofon sesi enerji tabanlı VAD ile ifadelere bölünür; her ifade Whisper'dan geçip tek bir metin olarak döner.
/// Mikrofon modunda klavyeden yazılan satır da ifade olarak kabul edilir (düzeltme/komut için).
///
/// GPU: Whisper.net Vulkan çalışma zamanı (NVIDIA sürücüsüyle gelir, CUDA Toolkit gerektirmez), yoksa CPU.
/// VRAM: qwen2.5:14b (16K bağlam) ~11 GB + bu model ~1–1,5 GB, RTX 5080'in 16 GB'ına sığar.
/// </summary>
public sealed class WhisperSpeechInput(IOptions<SesOptions> options, DebugGunlugu debug) : ISpeechInput, IDisposable
{
    private const int OrnekHizi = 16_000;          // Whisper 16 kHz mono bekler
    private const int TamponMs = 100;

    /// <summary>Whisper'ın sessizlik/gürültüde ürettiği bilinen Türkçe uydurmalar (altyazı veri setlerinden).</summary>
    private static readonly string[] Halusinasyonlar =
    [
        "altyazi m k", "altyazi", "izlediginiz icin tesekkur ederim", "izlediginiz icin tesekkurler",
        "abone olmayi unutmayin", "tesekkurler", "tesekkur ederim", "muzik", "alkis",
    ];

    private readonly SesOptions _options = options.Value;
    private readonly Channel<float[]> _ifadeler = Channel.CreateUnbounded<float[]>();
    private readonly object _kilit = new();

    private readonly object _hazirlikKilidi = new();
    private Task? _hazirlik;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private WaveInEvent? _mikrofon;
    private volatile bool _aktif;

    // VAD durumu (yalnızca mikrofon geri çağrısında, _kilit altında değişir)
    private readonly Queue<float[]> _onKayit = new();
    private readonly List<float> _ifade = [];
    private bool _konusuyor;
    private int _sessizOrnek;
    private int _konusmaOrnek;
    private double _gurultu = -1;

    public async Task<string?> DinleAsync(CancellationToken ct)
    {
        await BaslatAsync();
        if (!_aktif)
        {
            IfadeleriTemizle();
            _aktif = true;
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (_ifadeler.Reader.TryRead(out var ses))
            {
                var metin = await CozumleAsync(ses, ct);
                if (metin is not null) return metin;
                continue;
            }

            // Klavye her zaman açık: hekim düzeltme veya komut yazabilir.
            if (!Console.IsInputRedirected && Console.KeyAvailable)
            {
                _aktif = false;                    // yazarken konuşulan ses satırla karışmasın
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("⌨  ");
                Console.ResetColor();
                return Console.ReadLine();
            }

            await Task.WhenAny(_ifadeler.Reader.WaitToReadAsync(ct).AsTask(), Task.Delay(50, ct));
        }
    }

    public void Duraklat()
    {
        _aktif = false;
        IfadeleriTemizle();
    }

    /// <summary>
    /// Modeli yükler ve GPU'yu ısıtır. Vulkan ilk çözümlemede shader derler (~25 sn); bu yüzden Program.cs bunu
    /// uygulama açılışında arka planda başlatır, hekim ilk cümleyi söylediğinde model hazır olur. Tekrar çağrılabilir.
    /// </summary>
    public Task HazirlaAsync()
    {
        lock (_hazirlikKilidi)
            return _hazirlik ??= Task.Run(ModeliYukle);
    }

    private async Task ModeliYukle()
    {
        var modelYolu = Path.IsPathRooted(_options.ModelYolu)
            ? _options.ModelYolu
            : Path.Combine(AppContext.BaseDirectory, _options.ModelYolu);
        if (!File.Exists(modelYolu))
            throw new FileNotFoundException(
                $"Whisper modeli bulunamadı: {modelYolu}. models/ klasörüne ggml-large-v3-turbo-q8_0.bin indirin " +
                "(https://huggingface.co/ggerganov/whisper.cpp) veya MedScribe:Ses:Mod = \"Klavye\" yapın.", modelYolu);

        var vulkanLog = new List<string>();
        Whisper.net.Logger.LogProvider.AddLogger((_, mesaj) =>
        {
            if (mesaj is not null && (mesaj.Contains("ggml_vulkan: ") || mesaj.Contains("using Vulkan")))
                lock (vulkanLog) vulkanLog.Add(mesaj.TrimEnd());
        });

        var sure = System.Diagnostics.Stopwatch.StartNew();
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];
        _factory = WhisperFactory.FromPath(modelYolu, new WhisperFactoryOptions { UseGpu = true, GpuDevice = _options.GpuCihazNo });

        var builder = _factory.CreateBuilder()
            .WithLanguage(_options.Dil)
            .WithNoContext()                       // her ifade bağımsız; önceki ifadenin hatası taşınmaz
            .WithTemperature(0f)
            .WithNoSpeechThreshold(0.6f);
        if (!string.IsNullOrWhiteSpace(_options.Prompt)) builder = builder.WithPrompt(_options.Prompt);
        _processor = builder.Build();

        // Isınma: 1 sn sessizlik — Vulkan shader derlemesi burada yapılır.
        await foreach (var _ in _processor.ProcessAsync(new float[OrnekHizi])) { }

        // Arka planda çalıştığı için hemen yazdırılmaz (menüde hekim yazarken araya girer); mikrofon açılırken gösterilir.
        lock (vulkanLog)
            _hazirlikRaporu = ($"Whisper hazır ({RuntimeOptions.LoadedLibrary}, {sure.Elapsed.TotalSeconds:0.0} sn; " +
                               $"MedScribe:Ses:GpuCihazNo = {_options.GpuCihazNo})", string.Join(Environment.NewLine, vulkanLog));
    }

    private (string Baslik, string Icerik)? _hazirlikRaporu;

    private async Task BaslatAsync()
    {
        if (_mikrofon is not null) return;

        var hazirlik = HazirlaAsync();
        if (!hazirlik.IsCompleted) Konsol.Ajan("Whisper hazırlanıyor (ilk açılışta GPU ısınması ~30 sn sürebilir)...");
        await hazirlik;

        if (debug.Aktif)
        {
            if (_hazirlikRaporu is var (baslik, icerik)) debug.Yaz(baslik, icerik);
            var cihazlar = Enumerable.Range(0, WaveInEvent.DeviceCount)
                .Select(i => $"  [{i}] {WaveInEvent.GetCapabilities(i).ProductName}{(i == _options.MikrofonNo ? "  ← seçili" : "")}");
            debug.Yaz("Mikrofonlar (MedScribe:Ses:MikrofonNo)", string.Join(Environment.NewLine, cihazlar));
        }

        if (WaveInEvent.DeviceCount == 0)
            throw new InvalidOperationException("Mikrofon bulunamadı. Windows Ayarlar > Gizlilik > Mikrofon izinlerini kontrol edin.");

        _mikrofon = new WaveInEvent
        {
            DeviceNumber = _options.MikrofonNo,
            WaveFormat = new WaveFormat(OrnekHizi, 16, 1),
            BufferMilliseconds = TamponMs,
        };
        _mikrofon.DataAvailable += SesGeldi;
        _mikrofon.StartRecording();

        Konsol.Basari($"Mikrofon dinleniyor ({WaveInEvent.GetCapabilities(_options.MikrofonNo).ProductName}; " +
                      $"Whisper: {RuntimeOptions.LoadedLibrary?.ToString() ?? "?"}). Konuşun; klavyeden de yazabilirsiniz.");
    }

    /// <summary>NAudio geri çağrısı (~100 ms'de bir): enerji tabanlı VAD ile ifadeleri ayırır.</summary>
    private void SesGeldi(object? sender, WaveInEventArgs e)
    {
        var parca = new float[e.BytesRecorded / 2];
        double kareToplam = 0;
        for (var i = 0; i < parca.Length; i++)
        {
            parca[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
            kareToplam += parca[i] * parca[i];
        }
        var rms = parca.Length == 0 ? 0 : Math.Sqrt(kareToplam / parca.Length);

        lock (_kilit)
        {
            if (!_aktif) return;

            if (_gurultu < 0) _gurultu = rms;
            var esik = Math.Max(_options.EnerjiEsigi, _gurultu * _options.GurultuCarpani);
            var sesVar = rms > esik;

            if (!_konusuyor)
            {
                _gurultu = _gurultu * 0.95 + rms * 0.05;   // yalnızca sessizlikte güncellenir
                _onKayit.Enqueue(parca);
                while (_onKayit.Count > Math.Max(1, _options.OnKayitMs / TamponMs)) _onKayit.Dequeue();
                if (!sesVar) return;

                _konusuyor = true;
                foreach (var onceki in _onKayit) _ifade.AddRange(onceki);
                _onKayit.Clear();
                _sessizOrnek = 0;
                _konusmaOrnek = parca.Length;
                return;
            }

            _ifade.AddRange(parca);
            if (sesVar) { _sessizOrnek = 0; _konusmaOrnek += parca.Length; }
            else _sessizOrnek += parca.Length;

            var sessizlikBitti = _sessizOrnek >= _options.SessizlikMs * OrnekHizi / 1000;
            var cokUzun = _ifade.Count >= _options.MaxIfadeSaniye * OrnekHizi;
            if (!sessizlikBitti && !cokUzun) return;

            if (_konusmaOrnek >= _options.MinKonusmaMs * OrnekHizi / 1000)
                _ifadeler.Writer.TryWrite(_ifade.ToArray());
            _ifade.Clear();
            _konusuyor = false;
            _sessizOrnek = 0;
            _konusmaOrnek = 0;
        }
    }

    private async Task<string?> CozumleAsync(float[] ses, CancellationToken ct)
    {
        var baslangic = DateTime.UtcNow;
        var metin = new StringBuilder();
        await foreach (var segment in _processor!.ProcessAsync(ses, ct))
            metin.Append(segment.Text);

        var sonuc = metin.ToString().Trim();
        var sure = DateTime.UtcNow - baslangic;
        debug.Yaz("Whisper", $"{ses.Length / (double)OrnekHizi:0.0} sn ses → {sure.TotalSeconds:0.00} sn: \"{sonuc}\"");

        var katli = TurkceMetin.Katla(TurkceMetin.Sadelestir(sonuc));
        if (katli.Length == 0 || Halusinasyonlar.Contains(katli) || (sonuc.StartsWith('[') && sonuc.EndsWith(']')))
        {
            debug.Yaz("Whisper", "ifade atıldı (boş/halüsinasyon)");
            return null;
        }

        Konsol.Yaz(ConsoleColor.Cyan, $"🎙  {sonuc}");
        return sonuc;
    }

    private void IfadeleriTemizle()
    {
        lock (_kilit)
        {
            while (_ifadeler.Reader.TryRead(out _)) { }
            _ifade.Clear();
            _onKayit.Clear();
            _konusuyor = false;
            _sessizOrnek = 0;
            _konusmaOrnek = 0;
        }
    }

    public void Dispose()
    {
        if (_mikrofon is not null)
        {
            _mikrofon.DataAvailable -= SesGeldi;
            _mikrofon.StopRecording();
            _mikrofon.Dispose();
        }
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
