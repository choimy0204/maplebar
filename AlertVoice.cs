using System.Speech.Synthesis;

namespace MapleBar;

/// <summary>알림 제목 읽어 주기: 알림음이 조금 난 뒤 윈도우 한국어 목소리로 제목을 읽는다.
/// 한국어 목소리가 없으면 소리만 내고 로그만 남긴다. 기다리고 읽는 일은 따로 돌아서 화면을 막지 않는다.</summary>
static class AlertVoice
{
    static readonly object gate = new();
    static SpeechSynthesizer? syn;
    static bool missing;              // 한국어 목소리를 못 찾았다 (다시 찾지 않는다)
    static CancellationTokenSource? wait;

    /// <summary>설정에서 켜 두었으면 delay초 뒤에 글을 읽는다. 앞 알림의 읽기는 끊는다.</summary>
    public static void Say(string? text, double delay)
    {
        var st = AlertSound.St;
        if (st is not { SpeakOn: true, SoundOn: true }) return;
        Speak(text, st.SoundVol, delay);
    }

    /// <summary>설정과 상관없이 주어진 크기로 읽는다 (스위치를 켤 때 들려주기용)</summary>
    public static void Speak(string? text, int vol, double delay)
    {
        text = text?.Trim();
        vol = Math.Clamp(vol, 0, 100);
        if (string.IsNullOrEmpty(text) || vol == 0 || missing) return;
        CancellationTokenSource cts;
        lock (gate) { wait?.Cancel(); wait = cts = new(); }
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, delay)), cts.Token); }
            catch (OperationCanceledException) { return; }
            lock (gate)
            {
                if (cts.IsCancellationRequested) return;
                try
                {
                    var s = Get();
                    if (s == null) return;
                    s.SpeakAsyncCancelAll();
                    // 알림음 끝자락과 겹치면 말이 뭉개진다: 읽기 시작할 때 알림음은 멈춘다
                    AlertSound.Stop();
                    s.Volume = Math.Max(1, vol);  // 말소리는 줄이면 금방 안 들려서 크기 그대로
                    s.Rate = -1;                  // 조금 천천히 또렷하게
                    s.SpeakAsync(text); // 기다리지 않고 바로 돌아온다
                }
                catch (Exception ex) { Log.Write("sound", "알림 제목을 읽지 못함", ex); }
            }
        });
    }

    // 처음 읽을 때 한 번만 만든다. 한국어 목소리 중 해미(Heami)를 먼저 고른다.
    static SpeechSynthesizer? Get()
    {
        if (syn != null || missing) return syn;
        var s = new SpeechSynthesizer();
        var ko = s.GetInstalledVoices()
            .Where(v => v.Enabled && v.VoiceInfo.Culture.Name.Equals("ko-KR", StringComparison.OrdinalIgnoreCase))
            .Select(v => v.VoiceInfo)
            .OrderByDescending(v => v.Name.Contains("Heami", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (ko == null)
        {
            missing = true;
            s.Dispose();
            Log.Write("sound", "한국어 목소리가 없어 알림 제목을 읽지 않음");
            return null;
        }
        s.SelectVoice(ko.Name);
        s.SetOutputToDefaultAudioDevice();
        return syn = s;
    }
}
