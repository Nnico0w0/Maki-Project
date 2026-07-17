using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Maki.Services;

/// <summary>
/// Síntesis de voz usando Piper TTS (local, alta calidad).
/// Si Piper no está disponible, usa espeak-ng como fallback.
///
/// Instalar Piper: https://github.com/rhasspy/piper
/// Instalar espeak-ng: sudo apt install espeak-ng
/// </summary>
public sealed class PiperTtsService
{
    private readonly string _piperBinary;
    private readonly string _outputFile;

    /// <summary>Ruta al modelo .onnx de Piper. Puede cambiarse desde la UI.</summary>
    public string ModelPath { get; set; }

    public bool IsPiperAvailable { get; }

    public PiperTtsService()
    {
        _piperBinary = "piper";
        _outputFile  = Path.Combine(Path.GetTempPath(), "maki_response.wav");

        // Modelo español por defecto en ~/.maki/tts/
        ModelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".maki", "tts", "es_ES-davefx-medium.onnx");

        IsPiperAvailable = CheckBinary("piper");
    }

    /// <summary>
    /// Sintetiza el texto y lo reproduce por el altavoz.
    /// </summary>
    public async Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (IsPiperAvailable && File.Exists(ModelPath))
            await SpeakWithPiperAsync(text);
        else
            await SpeakWithEspeakAsync(text);
    }

    private async Task SpeakWithPiperAsync(string text)
    {
        // Piper lee el texto desde stdin y escribe el WAV a un archivo
        var psi = new ProcessStartInfo
        {
            FileName              = _piperBinary,
            Arguments             = $"--model \"{ModelPath}\" --output_file \"{_outputFile}\"",
            RedirectStandardInput = true,
            UseShellExecute       = false,
            CreateNoWindow        = true
        };

        using var piper = Process.Start(psi)!;
        await piper.StandardInput.WriteAsync(text);
        piper.StandardInput.Close();
        await piper.WaitForExitAsync();

        await PlayWavAsync(_outputFile);
    }

    private async Task SpeakWithEspeakAsync(string text)
    {
        // espeak-ng lee el texto desde stdin
        var psi = new ProcessStartInfo
        {
            FileName              = "espeak-ng",
            Arguments             = "-v es+f3 -s 145",
            RedirectStandardInput = true,
            UseShellExecute       = false,
            CreateNoWindow        = true
        };

        using var espeak = Process.Start(psi)!;
        await espeak.StandardInput.WriteAsync(text);
        espeak.StandardInput.Close();
        await espeak.WaitForExitAsync();
    }

    private static async Task PlayWavAsync(string wavFile)
    {
        if (!File.Exists(wavFile)) return;

        var psi = new ProcessStartInfo
        {
            FileName        = "aplay",
            Arguments       = $"\"{wavFile}\"",
            UseShellExecute = false,
            CreateNoWindow  = true
        };

        using var player = Process.Start(psi)!;
        await player.WaitForExitAsync();
    }

    private static bool CheckBinary(string name)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName               = "which",
                Arguments              = name,
                RedirectStandardOutput = true,
                UseShellExecute        = false
            })!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
