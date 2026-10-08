using System.Security.Cryptography;
using System.Text.Json;

namespace Encore.Core;

public record Track(int Id, string Title, string Artist, string Genre, int Duration, string Color, string Initials, bool Explicit = false);
public record RestaurantTable(int Number, string Token);
public record GuestSession(string Id, int Table, DateTimeOffset Expires);
public class RequestItem {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int TrackId { get; set; }
    public int Table { get; set; }
    public string Status { get; set; } = "waiting";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Started { get; set; }
    public string? Reason { get; set; }
    public HashSet<int> Votes { get; set; } = [];
}
public class Settings {
    public int TableLimit { get; set; } = 2;
    public bool AllowExplicit { get; set; }
    public int MaxDuration { get; set; } = 360;
    public string Mood { get; set; } = "Вечерний джаз";
    public bool Accepting { get; set; } = true;
}
public class SavedState {
    public List<RestaurantTable> Tables { get; set; } = [];
    public List<RequestItem> Orders { get; set; } = [];
    public Settings Settings { get; set; } = new();
}
public class RuleException(string message, int status = 400) : Exception(message) { public int Status { get; } = status; }

public class MusicStore {
    private readonly object gate = new();
    private readonly string file;
    private SavedState state;
    private readonly Dictionary<string, GuestSession> sessions = [];
    public IReadOnlyList<Track> Tracks { get; } = TrackCatalog.All;
    public MusicStore(IWebHostEnvironment env) {
        var directory = Environment.GetEnvironmentVariable("ENCORE_DATA_DIR") ?? Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(directory);
        file = Path.Combine(directory, "state.json");
        state = File.Exists(file) ? JsonSerializer.Deserialize<SavedState>(File.ReadAllText(file)) ?? throw new InvalidDataException("Некорректный файл состояния") : new SavedState();
        var changed = false;
        for(var number = 1; number <= 12; number++) {
            if(state.Tables.Any(t => t.Number == number)) continue;
            state.Tables.Add(new RestaurantTable(number, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()));
            changed = true;
        }
        if(changed) Save();
    }
    public object TableNumbers() { lock(gate) return state.Tables.OrderBy(t=>t.Number).Select(t=>new {number=t.Number}).ToArray(); }
    private void Save() {
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, file, true);
    }
    private static bool Active(RequestItem o) => o.Status is "waiting" or "approved" or "playing";
    private List<RequestItem> Queue() => state.Orders.Where(o => o.Status is "approved" or "waiting").OrderBy(o => o.Status == "approved" ? 0 : 1).ThenByDescending(o => o.Votes.Count).ThenBy(o => o.Created).ToList();
    private bool Available(Track t) => (!t.Explicit || state.Settings.AllowExplicit) && t.Duration <= state.Settings.MaxDuration;
    public object Catalog(string? query, string? genre) { lock(gate) {
        var q = query?.Trim() ?? "";
        return Tracks.Where(Available).Where(t => q.Length == 0 || t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Artist.Contains(q, StringComparison.OrdinalIgnoreCase)).Where(t => string.IsNullOrEmpty(genre) || genre == "Все" || t.Genre == genre).ToArray();
    }}
    public GuestSession CreateSession(string token) { lock(gate) {
        var table = state.Tables.FirstOrDefault(t => t.Token == token) ?? throw new RuleException("Неизвестный код столика.",404);
        foreach (var id in sessions.Where(s => s.Value.Expires <= DateTimeOffset.UtcNow).Select(s=>s.Key).ToArray()) sessions.Remove(id);
        var session = new GuestSession(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), table.Number, DateTimeOffset.UtcNow.AddHours(4));
        sessions.Add(session.Id,session); return session;
    }}
    public string DemoToken() { lock(gate) return state.Tables[0].Token; }
    public GuestSession Guest(string? id) { lock(gate) {
        if (id == null || !sessions.TryGetValue(id,out var s) || s.Expires <= DateTimeOffset.UtcNow) throw new RuleException("Откройте ссылку столика или начните демо-сеанс.",401);
        return s;
    }}
    public object Snapshot(int? table = null) { lock(gate) {
        var queue = Queue();
        var playing = state.Orders.FirstOrDefault(o=>o.Status == "playing");
        var remaining = playing == null ? 0 : Math.Max(0, Tracks.First(t=>t.Id==playing.TrackId).Duration - (int)(DateTimeOffset.UtcNow - playing.Started!.Value).TotalSeconds);
        var eta = remaining;
        var items = queue.Select((o,i)=> {
            var t=Tracks.First(t=>t.Id==o.TrackId); var wait=eta; eta+=t.Duration;
            return new { o.Id, track=t, o.Table, o.Status, votes=o.Votes.Count, voted=table.HasValue && o.Votes.Contains(table.Value), position=i+1, waitSeconds=wait, o.Created };
        }).ToArray();
        return new { queue=items, playing=playing == null ? null : new { playing.Id, track=Tracks.First(t=>t.Id==playing.TrackId), playing.Table, playing.Started, remaining }, settings=state.Settings, stats=new { total=state.Orders.Count, completed=state.Orders.Count(o=>o.Status=="completed"), waiting=queue.Count, votes=state.Orders.Sum(o=>o.Votes.Count) }, history=state.Orders.Where(o=>!Active(o) && (!table.HasValue || o.Table==table.Value)).OrderByDescending(o=>o.Created).Take(20).Select(o=>new {o.Id,track=Tracks.First(t=>t.Id==o.TrackId),o.Table,o.Status,o.Reason}) };
    }}
    public object Add(GuestSession guest,int trackId) { lock(gate) {
        if(!state.Settings.Accepting) throw new RuleException("Приём заявок на паузе. Дождитесь следующего музыкального слота.",409);
        var track=Tracks.FirstOrDefault(t=>t.Id==trackId) ?? throw new RuleException("Композиция не найдена.",404);
        if(!Available(track)) throw new RuleException("Композиция не подходит под текущие настройки вечера.",409);
        if(state.Orders.Any(o=>o.Table==guest.Table && o.TrackId==trackId && Active(o))) throw new RuleException("Этот трек уже заказан вашим столиком.",409);
        if(state.Orders.Count(o=>o.Table==guest.Table && Active(o)) >= state.Settings.TableLimit) throw new RuleException($"Лимит: {state.Settings.TableLimit} активные заявки на столик.",409);
        var order=new RequestItem {TrackId=trackId,Table=guest.Table}; state.Orders.Add(order); Save(); return new {order.Id,order.Status};
    }}
    public void Vote(GuestSession guest,string id) { lock(gate) {
        var o=state.Orders.FirstOrDefault(o=>o.Id==id) ?? throw new RuleException("Заявка не найдена.",404);
        if(o.Status is not ("waiting" or "approved")) throw new RuleException("Голосование закрыто.",409);
        if(o.Table==guest.Table) throw new RuleException("Поддержите заявку другого столика.",409);
        if(!o.Votes.Add(guest.Table)) throw new RuleException("Ваш столик уже проголосовал.",409);
        Save();
    }}
    public void Action(string id,string action) { lock(gate) {
        var o=state.Orders.FirstOrDefault(o=>o.Id==id) ?? throw new RuleException("Заявка не найдена.",404);
        if(!Active(o)) throw new RuleException("Заявка уже завершена.",409);
        switch(action) {
            case "approve": if(o.Status!="waiting") throw new RuleException("Заявка уже одобрена.",409); o.Status="approved"; break;
            case "play":
                if(o.Status!="approved") throw new RuleException("Сначала одобрите заявку.",409);
                if(state.Orders.Any(x=>x.Status=="playing")) throw new RuleException("Сначала завершите текущее выступление.",409);
                o.Status="playing"; o.Started=DateTimeOffset.UtcNow; break;
            case "complete": if(o.Status!="playing") throw new RuleException("Трек ещё не запущен.",409); o.Status="completed"; break;
            case "reject": if(o.Status=="playing") throw new RuleException("Используйте завершение или пропуск.",409); o.Status="rejected"; o.Reason="Не подходит к программе вечера"; break;
            case "skip": o.Status="skipped"; break;
            default: throw new RuleException("Неизвестное действие.");
        }
        Save();
    }}
    public void Update(Settings value) { lock(gate) {
        if(value.TableLimit is <1 or >5 || value.MaxDuration is <60 or >600 || !new[]{"Вечерний джаз","Караоке-вечеринка","Тихий ужин"}.Contains(value.Mood)) throw new RuleException("Проверьте настройки: лимит 1–5, длительность 60–600 секунд.");
        state.Settings=value; Save();
    }}
    public void Tick() { lock(gate) {
        var playing=state.Orders.FirstOrDefault(o=>o.Status=="playing");
        if(playing!=null && playing.Started!.Value.AddSeconds(Tracks.First(t=>t.Id==playing.TrackId).Duration)<=DateTimeOffset.UtcNow) {playing.Status="completed";Save();}
    }}
    public object Tables() { lock(gate) return state.Tables.ToArray(); }
    public string TableToken(int number) { lock(gate) return state.Tables.FirstOrDefault(t=>t.Number==number)?.Token ?? throw new RuleException("Столик не найден.",404); }
    public string Csv() { lock(gate) {
        static string Cell(string s) => "\"" + s.Replace("\"","\"\"") + "\"";
        return "Дата;Столик;Композиция;Исполнитель;Статус;Голоса\r\n"+string.Join("\r\n",state.Orders.Select(o=> {var t=Tracks.First(t=>t.Id==o.TrackId);return $"{o.Created:O};{o.Table};{Cell(t.Title)};{Cell(t.Artist)};{o.Status};{o.Votes.Count}";}));
    }}
}
public class QueueClock(MusicStore store) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
        while(await timer.WaitForNextTickAsync(stoppingToken)) store.Tick();
    }
}
