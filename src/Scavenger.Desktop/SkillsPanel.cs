using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Haldane.CareerSim.Work;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Skills;
using static Scavenger.Desktop.ConsoleUi;

namespace Scavenger.Desktop;

/// <summary>Learning controls only; progression, availability and reservations belong to the simulation.</summary>
public sealed class SkillsPanel : UserControl
{
    private readonly GameSession _session;
    private readonly ComboBox _member = new() { Name = "LearningMember" }, _skill = new() { Name = "LearningSkill" },
        _book = new() { Name = "LearningBook" }, _mentor = new() { Name = "LearningMentor" };
    private readonly TextBlock _progress = Text(""), _studyReason = Text("", 11, Muted), _practiceReason = Text("", 11, Muted);
    private readonly StackPanel _roster = new() { Spacing = 8 }, _tasks = new() { Spacing = 8 };
    private readonly Button _study, _practice, _find;
    private bool _refreshing;
    public event Action<string>? FindMaterial;
    public SkillsPanel(GameSession session)
    {
        _session = session;
        _study = Button("Study selected volume", () => Submit(LearningKind.Study), "StartSkillStudyButton", true);
        _practice = Button("Start hands-on practice", () => Submit(LearningKind.Practice), "StartSkillPracticeButton", true);
        _find = Button("Find next-level material →", () =>
        {
            if (Progress() is { } skill) FindMaterial?.Invoke(new SkillBook(skill.SkillId, skill.NextLevel).MaterialId);
        }, "FindSkillBookButton");
        var root = new StackPanel { Spacing = 10, Children = {
            Text("Skills & learning", 20),
            Text("Study the next level's material, then earn practical XP through work or an exercise. Each member learns independently.", 12, Muted),
            Field("Member", _member), Field("Specialty", _skill), _progress, _find,
            Field("Recovered volume", _book), _study, _studyReason,
            Field("Optional practice mentor", _mentor), _practice, _practiceReason,
            Eyebrow("Learning tasks"), _tasks,
            new Expander { Header = "All sixteen skills for this member", Content = _roster, Name = "SkillRosterExpander" }
        } };
        foreach (var combo in new[] { _member, _skill, _book, _mentor })
        { combo.HorizontalAlignment = HorizontalAlignment.Stretch; combo.SelectionChanged += (_, _) => { if (!_refreshing) Refresh(); }; }
        Content = root; Refresh();
    }
    private static string? Selected(ComboBox combo) => (combo.SelectedItem as Choice)?.Id;
    private static void Choices(ComboBox combo, IEnumerable<Choice> values)
    {
        string? prior = Selected(combo); var choices = values.ToArray();
        combo.ItemsSource = choices; combo.SelectedItem = choices.FirstOrDefault(c => c.Id == prior) ?? choices.FirstOrDefault();
    }
    private MemberSkill? Progress() => int.TryParse(Selected(_member), out int worker) && Selected(_skill) is { } skill
        ? _session.World.Module<SkillsModule>().Find(worker, skill) : null;
    private LearningPlan? Plan(LearningKind kind)
        => Progress() is { } progress ? new(_session.NextId("learning"), progress.WorkerId, progress.SkillId, kind,
            kind == LearningKind.Study ? Selected(_book) : null,
            kind == LearningKind.Practice && int.TryParse(Selected(_mentor), out int mentor) ? mentor : null) : null;
    private void Submit(LearningKind kind) { if (Plan(kind) is { } plan) _session.Submit(new StartLearningCommand(plan)); }
    public void Refresh()
    {
        if (!_session.World.TryModule<SkillsModule>(out var skills)) { IsEnabled = false; return; }
        IsEnabled = true; _refreshing = true;
        try
        {
            var world = _session.World; var workforce = world.Module<WorkforceModule>(); var inventory = world.Module<InventoryModule>();
            Choices(_member, workforce.Staff.Select(w => new Choice(w.Id.ToString(), $"{w.Name} · {(w.Present ? workforce.ReservationFor(w.Id) is null ? "available" : "assigned" : "absent / ill")}")));
            Choices(_skill, SkillCatalogue.All.Select(s => new Choice(s.Id, s.Name)));
            if (Progress() is not { } progress) return;
            var definition = SkillCatalogue.Find(progress.SkillId)!;
            _progress.Text = $"{definition.Name} · L{progress.Level} / 10 · rating {progress.Rating}\nTheory L{progress.KnowledgeLevel} · {progress.Gate}\n{definition.Description}";
            _find.IsEnabled = !progress.Mastered && !progress.TheoryReady && world.Has<Scavenger.Sim.Scavenging.ScavengingModule>();
            Choices(_book, inventory.Batches.Where(b => SkillCatalogue.Book(b.MaterialId) is { } book && book.SkillId == progress.SkillId && book.Level == progress.NextLevel)
                .Select(b => new Choice(b.Id, $"L{progress.NextLevel} · {inventory.FindStockpile(b.StockpileId)!.Name} · {(inventory.AvailableKg(b.Id) == SkillBook.MassKg ? "available" : "reserved")}")));
            Choices(_mentor, new[] { new Choice("none", "Independent practice") }.Concat(workforce.Staff.Where(w => w.Id != progress.WorkerId && skills.Find(w.Id, progress.SkillId)!.Level >= progress.NextLevel)
                .Select(w => new Choice(w.Id.ToString(), $"{w.Name} · L{skills.Find(w.Id, progress.SkillId)!.Level}"))));
            string? studyReason = Plan(LearningKind.Study) is { } study ? skills.Explain(world, study) : "Choose a member and specialty.";
            string? practiceReason = Plan(LearningKind.Practice) is { } practice ? skills.Explain(world, practice) : "Choose a member and specialty.";
            _study.IsEnabled = studyReason is null; _practice.IsEnabled = practiceReason is null;
            _studyReason.Text = studyReason ?? $"Read for {SkillCatalogue.StudyRequired(progress.NextLevel) - progress.StudyEffort:0.##} more effort. This reserves the reader and volume; the book is reusable.";
            _practiceReason.Text = practiceReason ?? $"{definition.Exercise}. {SkillCatalogue.ExperienceRequired(progress.NextLevel) - progress.Experience:0.##} XP remaining. Productive {definition.Name.ToLowerInvariant()} work also counts.";
            _tasks.Children.Clear();
            foreach (var job in skills.Jobs.Where(j => j.Active))
            {
                string id = job.Plan.Id;
                _tasks.Children.Add(Frame(new StackPanel { Spacing = 6, Children = {
                    Text($"{workforce.Find(job.Plan.WorkerId)!.Name} · {job.Plan.Kind} {SkillCatalogue.Find(job.Plan.SkillId)!.Name} L{job.TargetLevel}", 12, Accent),
                    Text($"{job.Status} · {job.Effort:0.##} effort\n{job.BlockedReason}", 11, Muted),
                    Button("Cancel; keep progress", () => _session.Submit(new CancelLearningCommand(id)), "CancelLearning_" + id)
                } }, new Thickness(10)));
            }
            if (_tasks.Children.Count == 0) _tasks.Children.Add(Text("No active learning tasks.", 11, Muted));
            _roster.Children.Clear();
            foreach (var skill in skills.Members.Where(s => s.WorkerId == progress.WorkerId))
                _roster.Children.Add(Text($"{SkillCatalogue.Find(skill.SkillId)!.Name} · L{skill.Level} · rating {skill.Rating}\nTheory L{skill.KnowledgeLevel} · {skill.Gate}", 11, Muted));
        }
        finally { _refreshing = false; }
    }
}
