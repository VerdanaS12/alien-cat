using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using AlienCat.App.Services;
using AlienCat.App.Tray;
using AlienCat.App.Win32;
using AlienCat.App.Windows;
using AlienCat.Core.Animation;
using AlienCat.Core.Logging;
using AlienCat.Core.Notion;
using AlienCat.Core.Pet;
using AlienCat.Core.Storage;
using AlienCat.Core.Tasks;
using AlienCat.Core.Time;

namespace AlienCat.App;

/// <summary>Wires the pet, Notion sync, reminders, tray and dialogs together.</summary>
internal sealed class AppController : IDisposable
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    readonly IClock _clock = new SystemClock();
    readonly FileLog _log;
    readonly JsonFileStore<AppSettings> _settingsStore;
    readonly JsonFileStore<List<CatTask>> _cacheStore;
    readonly JsonFileStore<AppState> _stateStore;
    readonly ISecretProtector _protector = new DpapiProtector();
    readonly DateTimeOffset _sessionStart = DateTimeOffset.Now;
    readonly PetRunner _runner;
    readonly TrayIcon _tray;
    readonly DispatcherTimer _pollTimer;
    readonly DispatcherTimer _reminderTimer;
    readonly DispatcherTimer _signTimer;

    AppSettings _settings;
    TaskSync? _sync;
    ReminderSign? _sign;
    DateTimeOffset _signShownAt;
    TodayWindow? _today;
    SettingsWindow? _settingsWindow;
    QuickNoteWindow? _note;
    string? _lastError;
    bool _polling;
    bool _firstPollDone;

    public AppController(AnimationCatalog catalog)
    {
        _log = new FileLog(AppPaths.Logs, _clock);
        _settingsStore = new JsonFileStore<AppSettings>(AppPaths.Settings, _log.Info);
        _cacheStore = new JsonFileStore<List<CatTask>>(AppPaths.Cache, _log.Info);
        _stateStore = new JsonFileStore<AppState>(AppPaths.State, _log.Info);
        _settings = _settingsStore.Load();
        _log.Info("Котик запускается");

        var brain = new PetBrain(new SystemRandom(), DateTimeOffset.Now, new Vec(0, 0));
        _runner = new PetRunner(brain, catalog, () => _settings, HasOverdue);
        _runner.QuickNoteRequested += ShowQuickNote;

        _tray = new TrayIcon(ShowQuickNote, ShowToday, ShowSettings, Exit);

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _pollTimer.Tick += async (_, _) => await PollAsync();
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _reminderTimer.Tick += (_, _) => CheckReminders();
        _signTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _signTimer.Tick += (_, _) => FollowSign();
    }

    PetBrain Brain => _runner.Brain;

    public async void Start()
    {
        _runner.Start();
        RebuildSync();
        _pollTimer.Start();
        _reminderTimer.Start();
        _signTimer.Start();
        if (!_settings.HasNotion)
        {
            _tray.ShowBalloon("Космокот", "Привет! Подключите Notion в настройках, чтобы я напоминал о задачах.");
            ShowSettings();
        }
        await PollAsync();
    }

    // ------------------------------------------------------------- Notion sync

    void RebuildSync()
    {
        _sync = null;
        if (!_settings.HasNotion) return;
        try
        {
            var token = _protector.Unprotect(_settings.ProtectedToken!);
            var gateway = new NotionGateway(Http, token, _settings.DatabaseUrl!, TimeZoneInfo.Local);
            _sync = new TaskSync(gateway, _cacheStore, _stateStore, _clock);
        }
        catch (Exception ex)
        {
            _log.Error("Не удалось подготовить подключение к Notion", ex);
            ReportError("Не удалось прочитать ключ Notion. Введите его заново в настройках.");
        }
    }

    async Task PollAsync()
    {
        if (_sync is null || _polling) return;
        _polling = true;
        try
        {
            var result = await _sync.PollAsync(CancellationToken.None);
            switch (result.Status)
            {
                case SyncStatus.Online:
                    _lastError = null;
                    CleanupState(result.Tasks);
                    break;
                case SyncStatus.Offline:
                    _log.Info("Notion недоступен, работаю по сохранённому списку");
                    break;
                case SyncStatus.Error:
                    _log.Info("Ошибка Notion: " + result.Message);
                    ReportError(result.Message ?? "Ошибка Notion");
                    break;
            }
            if (!_firstPollDone)
            {
                _firstPollDone = true;
                ShowMorningIfNeeded();
            }
            CloseSignIfTaskGone();
            _today?.Refresh();
        }
        catch (Exception ex)
        {
            _log.Error("Сбой при обновлении задач", ex);
        }
        finally
        {
            _polling = false;
        }
    }

    void ReportError(string message)
    {
        if (message == _lastError) return;
        _lastError = message;
        _tray.ShowBalloon("Космокот грустит", message);
    }

    /// <summary>Forget snoozes and handled reminders of tasks that no longer exist.</summary>
    void CleanupState(IReadOnlyList<CatTask> tasks)
    {
        if (_sync is null) return;
        var ids = tasks.Select(t => t.Id).ToHashSet();
        var state = _sync.State;
        var changed = false;
        foreach (var id in state.Snoozes.Keys.Where(id => !ids.Contains(id)).ToList()) { state.Snoozes.Remove(id); changed = true; }
        changed |= state.HandledKeys.RemoveWhere(k => !ids.Contains(k.Split('|')[0])) > 0;
        if (changed) _sync.SaveState();
    }

    bool HasOverdue() => _sync is not null && ReminderEngine.HasOverdue(_sync.Current, DateTimeOffset.Now);

    // --------------------------------------------------------------- reminders

    void CheckReminders()
    {
        if (_sync is null) return;
        var now = DateTimeOffset.Now;
        if (_sign is not null)
        {
            Brain.Nagging = ReminderEngine.IsNagging(_signShownAt, now);
            return;
        }
        if (Brain.Activity is PetActivity.Hidden or PetActivity.Dragged) return;

        var pending = ReminderEngine.GetPending(_sync.Current, now, _sessionStart, _sync.State.Snoozes,
            _sync.State.HandledKeys, _settings.Quiet, TimeZoneInfo.Local);
        if (pending.Count == 0) return;
        ShowSign(pending[0]);
    }

    void ShowSign(Reminder reminder)
    {
        _sign = new ReminderSign(reminder);
        _sign.Done += () => _ = CompleteFromSignAsync();
        _sign.Snoozed += Snooze;
        _signShownAt = DateTimeOffset.Now;
        Brain.StartReminder();
        _sign.Show();
        FollowSign();
        _log.Info($"Напоминание: {reminder.Task.Title} ({reminder.Kind})");
    }

    void FollowSign()
    {
        if (_sign is null) return;
        var hidden = Brain.Activity == PetActivity.Hidden;
        if (hidden && _sign.IsVisible) _sign.Hide();
        else if (!hidden && !_sign.IsVisible) _sign.Show();
        var area = ScreenInfo.WorkArea();
        _sign.Follow(_runner.Pet.Left + _runner.Pet.Width / 2, _runner.Pet.Top + _runner.Pet.Height * 0.35, area.Left, area.Right);
    }

    async Task CompleteFromSignAsync()
    {
        if (_sign is null || _sync is null) return;
        var reminder = _sign.Reminder;
        CloseSign(done: true);
        _sync.State.HandledKeys.Add(ReminderEngine.Key(reminder));
        _sync.State.Snoozes.Remove(reminder.Task.Id);
        _sync.SaveState();
        await CompleteAsync(reminder.Task);
    }

    void Snooze(SnoozeOption option)
    {
        if (_sign is null || _sync is null) return;
        var reminder = _sign.Reminder;
        CloseSign(done: false);
        _sync.State.Snoozes[reminder.Task.Id] = ReminderEngine.SnoozeUntil(option, DateTimeOffset.Now, TimeZoneInfo.Local);
        _sync.SaveState();
    }

    void CloseSign(bool done)
    {
        _sign?.Close();
        _sign = null;
        Brain.EndReminder(done);
    }

    void CloseSignIfTaskGone()
    {
        if (_sign is null || _sync is null) return;
        var id = _sign.Reminder.Task.Id;
        var task = _sync.Current.FirstOrDefault(t => t.Id == id);
        if (task is null || task.Done || task.Due != _sign.Reminder.Task.Due) CloseSign(done: false);
    }

    async Task CompleteAsync(CatTask task)
    {
        if (_sync is null) return;
        try
        {
            var status = await _sync.CompleteAsync(task, CancellationToken.None);
            if (status == SyncStatus.Error) ReportError("Не получилось отметить задачу в Notion.");
        }
        catch (Exception ex)
        {
            _log.Error("Не удалось отметить задачу", ex);
        }
        _today?.Refresh();
    }

    // ----------------------------------------------------------------- windows

    void ShowMorningIfNeeded()
    {
        if (_sync is null) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_sync.State.LastMorning == today) return;
        _sync.State.LastMorning = today;
        _sync.SaveState();
        if (ReminderEngine.MorningSummary(_sync.Current, DateTimeOffset.Now, TimeZoneInfo.Local).Count > 0) ShowToday();
    }

    void ShowToday()
    {
        if (_today is not null) { _today.Activate(); return; }
        _today = new TodayWindow(() => _sync?.Current ?? []);
        _today.DoneClicked += t => _ = CompleteAsync(t);
        _today.Closed += (_, _) => _today = null;
        _today.Show();
    }

    void ShowQuickNote()
    {
        if (_note is not null) { _note.Activate(); return; }
        if (_sync is null)
        {
            _tray.ShowBalloon("Космокот", "Сначала подключите Notion в настройках.");
            ShowSettings();
            return;
        }
        _note = new QuickNoteWindow();
        _note.Saved += async text =>
        {
            var status = await _sync.AddNoteAsync(text, CancellationToken.None);
            if (status == SyncStatus.Error) ReportError("Не получилось сохранить заметку в Notion.");
            else Brain.Click();
        };
        _note.Closed += (_, _) => _note = null;
        _note.Show();
    }

    void ShowSettings()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settings, SafeAutostart(), CheckConnectionAsync);
        _settingsWindow.Saved += ApplySettings;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    async Task<string> CheckConnectionAsync(string? newToken, string url)
    {
        string token;
        if (newToken is not null) token = newToken;
        else if (_settings.ProtectedToken is not null) token = _protector.Unprotect(_settings.ProtectedToken);
        else return "Вставьте ключ интеграции Notion.";
        if (string.IsNullOrWhiteSpace(url)) return "Вставьте ссылку на базу.";

        try
        {
            var gateway = new NotionGateway(Http, token, url, TimeZoneInfo.Local);
            var check = await gateway.CheckSchemaAsync(CancellationToken.None);
            return check.Ok ? "Всё работает!" : string.Format(TaskSync.MsgMissingFields, string.Join(", ", check.MissingFields));
        }
        catch (ArgumentException ex) { return ex.Message; }
        catch (NotionException ex)
        {
            return ex.Kind switch
            {
                NotionErrorKind.Unauthorized => TaskSync.MsgUnauthorized,
                NotionErrorKind.NotFound => TaskSync.MsgNotFound,
                NotionErrorKind.Network or NotionErrorKind.RateLimited => "Нет связи с Notion. Проверьте интернет.",
                _ => string.Format(TaskSync.MsgUnknown, ex.Message),
            };
        }
    }

    void ApplySettings(AppSettings updated, string? newToken)
    {
        if (newToken is not null) updated = updated with { ProtectedToken = _protector.Protect(newToken) };
        var notionChanged = newToken is not null || updated.DatabaseUrl != _settings.DatabaseUrl;
        var scaleChanged = updated.Scale != _settings.Scale;
        _settings = updated;
        _settingsStore.Save(updated);

        try { Autostart.Set(updated.Autostart); }
        catch (Exception ex) { _log.Error("Не удалось изменить автозапуск", ex); }

        if (scaleChanged) _runner.SetScale(updated.Scale);
        if (notionChanged)
        {
            _lastError = null;
            RebuildSync();
            _ = PollAsync();
        }
    }

    bool SafeAutostart()
    {
        try { return Autostart.IsEnabled(); }
        catch { return _settings.Autostart; }
    }

    void Exit()
    {
        _log.Info("Котик уходит спать");
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _reminderTimer.Stop();
        _signTimer.Stop();
        _sign?.Close();
        _sync?.SaveState();
        _tray.Dispose();
        _runner.Stop();
    }

    public void LogError(string message, Exception ex) => _log.Error(message, ex);
}
