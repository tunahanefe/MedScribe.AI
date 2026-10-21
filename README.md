# MedScribe.AI

Hekim muayenesi için sesli medikal asistan (.NET 8 konsol uygulaması).

```
Mikrofon → Whisper (yerel, Vulkan GPU) → transkript → Ollama qwen2.5:14b → anamnez / tanı / tetkik / vitaller
                                                                              ↓ hekim onayı
                                                                      MiaMed HBYS veritabanı
```

- Konuşma yerelde yazıya çevrilir, LLM yerelde çalışır; ses veya metin dışarıya gönderilmez.
- Tanı önerileri hastanın MiaMed geçmişinden (önceki vizitler, tanılar, ilaçlar, laboratuvar) çıkarılan aday listesiyle sınırlıdır.
- Anamnez, tanılar ve vitaller hekim onayından sonra yazılır; tetkikler hekimin sesli komutuyla ("hemogram tetkiki ekle") eklenir.

**Kurulum:** [KURULUM.md](KURULUM.md) (hızlı kontrol: `.\kurulum.ps1`)
**Geliştirici notları / DB kuralları:** [CLAUDE.md](CLAUDE.md)
