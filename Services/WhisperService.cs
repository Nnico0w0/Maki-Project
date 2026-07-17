using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace Maki.Services;

/// <summary>
/// Transcripción local de voz a texto usando Whisper.net.
/// Descarga automáticamente el modelo GGML en el primer uso.
/// </summary>
public sealed class WhisperService : IAsyncDisposable
{
    private WhisperFactory?   _factory;
    private WhisperProcessor? _processor;

    private readonly string _modelPath;

    public bool IsReady { get; private set; }

    // Cambia a GgmlType.Base o GgmlType.Small para mayor precisión (más lento)
    public WhisperService(GgmlType modelType = GgmlType.Tiny)
    {
        var modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".maki", "models");

        Directory.CreateDirectory(modelDir);

        _modelPath = Path.Combine(modelDir, $"ggml-{modelType.ToString().ToLower()}.bin");
    }

    public async Task InitializeAsync(IProgress<string>? progress = null)
    {
        if (IsReady) return;

        if (!File.Exists(_modelPath))
        {
            progress?.Report("Descargando modelo Whisper (primera vez, ~75 MB)...");
            await using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(
                GgmlType.Tiny, QuantizationType.NoQuantization, CancellationToken.None);
            await using var fileStream  = File.OpenWrite(_modelPath);
            await modelStream.CopyToAsync(fileStream);
        }

        progress?.Report("Cargando modelo Whisper...");

        _factory = await Task.Run(() => WhisperFactory.FromPath(_modelPath));

        _processor = _factory.CreateBuilder()
            .WithLanguage("es")
            .Build();

        IsReady = true;
    }

    /// <summary>
    /// Transcribe un archivo WAV (16 kHz, mono, 16-bit PCM).
    /// </summary>
    public async Task<string> TranscribeAsync(string wavFilePath)
    {
        if (_processor is null)
            throw new InvalidOperationException("WhisperService no está inicializado.");

        var sb = new StringBuilder();

        await using var fs = File.OpenRead(wavFilePath);
        await foreach (var segment in _processor.ProcessAsync(fs))
            sb.Append(segment.Text);

        return sb.ToString().Trim();
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
            await _processor.DisposeAsync();

        _factory?.Dispose();
    }
}
