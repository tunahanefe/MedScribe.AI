using System.Text;
using MedScribe.AI.Agent;
using MedScribe.AI.App;
using MedScribe.AI.Configuration;
using MedScribe.AI.Data;
using MedScribe.AI.Services;
using MedScribe.AI.Speech;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,   // appsettings.json, çalışma dizininden bağımsız olarak bin'den okunur
});

builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.Section));
builder.Services.Configure<MuayeneOptions>(builder.Configuration.GetSection(MuayeneOptions.Section));
builder.Services.Configure<KlinikHizmetOptions>(builder.Configuration.GetSection(KlinikHizmetOptions.Section));
builder.Services.Configure<TaniAnaliziOptions>(builder.Configuration.GetSection(TaniAnaliziOptions.Section));
builder.Services.Configure<DebugOptions>(builder.Configuration.GetSection(DebugOptions.Section));

// Gizli ayarlar (bağlantı cümlesi) git'e girmeyen dosyada; appsettings.json'daki boş değeri ezer.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

var connectionString = builder.Configuration.GetConnectionString("MiaMed");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "ConnectionStrings:MiaMed tanımlı değil. appsettings.Local.example.json dosyasını appsettings.Local.json " +
        "olarak kopyalayıp bağlantı cümlesini girin.");

builder.Services.AddDbContextFactory<MedScribeDbContext>(o => o
    .UseSqlServer(connectionString, sql => sql
        .UseCompatibilityLevel(120)                    // MiaMed_ULU uyumluluk seviyesi 120 (OPENJSON kullanılmasın)
        .EnableRetryOnFailure(3)
        .CommandTimeout(30))
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

builder.Services.AddHttpClient<LlmAgentService>((sp, http) =>
{
    var ollama = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    http.BaseAddress = new Uri(ollama.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(ollama.TimeoutSaniye);
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<SesOptions>(builder.Configuration.GetSection(SesOptions.Section));
if (builder.Configuration.GetSection(SesOptions.Section).Get<SesOptions>()?.MikrofonModu == true)
    builder.Services.AddSingleton<ISpeechInput, WhisperSpeechInput>();
else
    builder.Services.AddSingleton<ISpeechInput, ConsoleSpeechInput>();
builder.Services.AddSingleton<KomutCozucu>();
builder.Services.AddSingleton<HekimSorusu>();
builder.Services.AddSingleton<PatientService>();
builder.Services.AddSingleton<TaniService>();
builder.Services.AddSingleton<KlinikHizmetService>();
builder.Services.AddSingleton<MuayeneKayitService>();
builder.Services.AddSingleton<HastaGecmisiService>();
builder.Services.AddSingleton<DebugGunlugu>();
builder.Services.AddTransient<MuayeneOturumu>();
builder.Services.AddTransient<MedScribeApp>();

using var host = builder.Build();

// Whisper modelini ve GPU ısınmasını hekim hasta ararken arka planda hazırla (hata olursa ilk dinlemede görünür).
if (host.Services.GetRequiredService<ISpeechInput>() is WhisperSpeechInput whisper)
    _ = whisper.HazirlaAsync();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await host.Services.GetRequiredService<MedScribeApp>().CalistirAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Konsol.Bilgi("Oturum sonlandırıldı.");
}
catch (Microsoft.Data.SqlClient.SqlException ex)
{
    Konsol.Hata($"Veritabanı hatası: {ex.Message}");
    return 1;
}

return 0;
