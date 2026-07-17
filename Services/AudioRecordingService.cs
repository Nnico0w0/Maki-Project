using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Maki.Services;

/// <summary>
/// Graba audio del micrófono usando arecord (ALSA/PulseAudio).
/// Requisito: tener instalado alsa-utils (arecord).
/// </summary>
public sealed class AudioRecordingService : IDisposable
{
    private Process? _process;
    private readonly string _outputFile;

    public bool IsRecording => _process is { HasExited: false };

    public AudioRecordingService()
    {
        _outputFile = Path.Combine(Path.GetTempPath(), "maki_recording.wav");
    }

    public void StartRecording()
    {
        if (IsRecording) return;

        if (File.Exists(_outputFile))
            File.Delete(_outputFile);

        var psi = new ProcessStartInfo
        {
            FileName               = "arecord",
            // 16 kHz, mono, signed 16-bit — formato ideal para Whisper
            Arguments              = $"-f S16_LE -r 16000 -c 1 \"{_outputFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        _process = Process.Start(psi);
    }

    public async Task<string?> StopRecordingAsync()
    {
        if (_process == null) return null;

        try
        {
            _process.Kill();
            await _process.WaitForExitAsync();
        }
        catch { /* proceso ya terminado */ }
        finally
        {
            _process.Dispose();
            _process = null;
        }

        // Breve pausa para que el SO termine de escribir el archivo
        await Task.Delay(300);

        if (!File.Exists(_outputFile) || new FileInfo(_outputFile).Length < 1024)
            return null;

        // Corregir cabecera WAV si fue interrumpido abruptamente
        FixWavHeader(_outputFile);

        return _outputFile;
    }

    /// <summary>
    /// Corrige los campos de tamaño del encabezado WAV cuando el proceso
    /// fue terminado con SIGKILL y dejó los campos en 0 o en max-int.
    /// </summary>
    private static void FixWavHeader(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);
            if (fs.Length < 44) return;

            int totalSize = (int)fs.Length;

            // RIFF chunk size (offset 4) = total bytes - 8
            WriteInt32LE(fs, offset: 4,  value: totalSize - 8);
            // data chunk size (offset 40) = total bytes - 44
            WriteInt32LE(fs, offset: 40, value: totalSize - 44);
        }
        catch { /* no crítico */ }
    }

    private static void WriteInt32LE(FileStream fs, int offset, int value)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        fs.Write(BitConverter.GetBytes(value));
    }

    public void Dispose()
    {
        try { _process?.Kill(); } catch { }
        _process?.Dispose();
    }
}
