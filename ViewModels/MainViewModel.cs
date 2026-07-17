using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maki.Models;
using Maki.Services;

namespace Maki.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ── Servicios ─────────────────────────────────────────────────────────────
    private readonly AudioRecordingService _audio;
    private readonly WhisperService        _whisper;
    private readonly LMStudioService       _lmStudio;
    private readonly PiperTtsService       _tts;

    private CancellationTokenSource? _cts;

    // ── Estado observables ────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTalk))]
    private bool _isWhisperReady;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Iniciando...";

    [ObservableProperty]
    private string _modelId = "local-model";

    [ObservableProperty]
    private string _lmStudioUrl = "http://localhost:1234";

    [ObservableProperty]
    private bool _ttsEnabled = true;

    // ── Propiedades derivadas ──────────────────────────────────────────────────

    public bool CanTalk => IsWhisperReady && !IsProcessing;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel()
    {
        _audio    = new AudioRecordingService();
        _whisper  = new WhisperService();
        _lmStudio = new LMStudioService();
        _tts      = new PiperTtsService();

        _ = InitializeAsync();
    }

    // ── Inicialización ────────────────────────────────────────────────────────

    private async Task InitializeAsync()
    {
        var progressReporter = new Progress<string>(msg =>
            Dispatcher.UIThread.Post(() => StatusText = msg));

        try
        {
            await _whisper.InitializeAsync(progressReporter);
            IsWhisperReady = true;
        }
        catch (Exception ex)
        {
            StatusText = $"Error al cargar Whisper: {ex.Message}";
            return;
        }

        // Sincronizar URL con el servicio
        _lmStudio.BaseUrl = LmStudioUrl;

        IsConnected = await _lmStudio.CheckConnectionAsync();
        StatusText   = IsConnected
            ? "Listo. Presiona el botón para hablar."
            : "Listo (LM Studio no detectado — inicia el servidor).";
    }

    // ── Comando: activar/desactivar micrófono ─────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanTalk))]
    private async Task ToggleListeningAsync()
    {
        if (IsListening)
            await StopAndProcessAsync();
        else
            BeginListening();
    }

    private void BeginListening()
    {
        IsListening = true;
        StatusText  = "Escuchando...";
        _audio.StartRecording();
    }

    private async Task StopAndProcessAsync()
    {
        IsListening  = false;
        IsProcessing = true;
        ToggleListeningCommand.NotifyCanExecuteChanged();

        // 1. Detener grabación
        StatusText = "Procesando audio...";
        var audioFile = await _audio.StopRecordingAsync();

        if (audioFile is null)
        {
            StatusText   = "No se capturó audio. Intenta de nuevo.";
            IsProcessing = false;
            ToggleListeningCommand.NotifyCanExecuteChanged();
            return;
        }

        // 2. Transcribir con Whisper
        string transcription;
        try
        {
            StatusText    = "Transcribiendo voz...";
            transcription = await _whisper.TranscribeAsync(audioFile);
        }
        catch (Exception ex)
        {
            StatusText   = $"Error en transcripción: {ex.Message}";
            IsProcessing = false;
            ToggleListeningCommand.NotifyCanExecuteChanged();
            return;
        }

        if (string.IsNullOrWhiteSpace(transcription))
        {
            StatusText   = "No se detectó voz. Intenta de nuevo.";
            IsProcessing = false;
            ToggleListeningCommand.NotifyCanExecuteChanged();
            return;
        }

        // 3. Agregar mensaje del usuario
        Messages.Add(new ChatMessage { Role = "user",      Content = transcription });
        var reply = new ChatMessage  { Role = "assistant", Content = "" };
        Messages.Add(reply);

        // 4. Enviar a LM Studio (streaming)
        _lmStudio.BaseUrl = LmStudioUrl;
        _lmStudio.ModelId = ModelId;

        _cts = new CancellationTokenSource();
        var fullText = new System.Text.StringBuilder();

        StatusText = "Generando respuesta...";
        try
        {
            await foreach (var chunk in _lmStudio.SendMessageAsync(transcription, _cts.Token))
            {
                fullText.Append(chunk);
                var snapshot = fullText.ToString();
                Dispatcher.UIThread.Post(() => reply.Content = snapshot);
            }
        }
        catch (OperationCanceledException) { /* cancelado por el usuario */ }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => reply.Content = $"[Error: {ex.Message}]");
        }

        // 5. TTS
        if (TtsEnabled && fullText.Length > 0)
        {
            StatusText = "Reproduciendo respuesta...";
            try   { await _tts.SpeakAsync(fullText.ToString()); }
            catch { /* TTS no crítico */ }
        }

        IsProcessing = false;
        ToggleListeningCommand.NotifyCanExecuteChanged();
        StatusText = "Listo. Presiona el botón para hablar.";
    }

    // ── Comandos auxiliares ───────────────────────────────────────────────────

    [RelayCommand]
    private async Task CheckConnectionAsync()
    {
        _lmStudio.BaseUrl = LmStudioUrl;
        IsConnected  = await _lmStudio.CheckConnectionAsync();
        StatusText   = IsConnected
            ? "LM Studio conectado ✓"
            : "LM Studio no disponible";
    }

    [RelayCommand]
    private void ClearHistory()
    {
        Messages.Clear();
        _lmStudio.ClearHistory();
        StatusText = "Historial borrado.";
    }

    [RelayCommand]
    private void CancelGeneration()
    {
        _cts?.Cancel();
        StatusText = "Cancelado.";
    }

    // ── Notificaciones parciales ──────────────────────────────────────────────

    partial void OnIsProcessingChanged(bool value) =>
        ToggleListeningCommand.NotifyCanExecuteChanged();

    partial void OnIsWhisperReadyChanged(bool value) =>
        ToggleListeningCommand.NotifyCanExecuteChanged();
}
