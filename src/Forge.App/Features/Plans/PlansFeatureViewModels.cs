using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Domain.Planning;
using Forge.Domain.Training;

namespace Forge.App.Features.Plans;

public sealed partial class PlanListViewModel(IPlanPersistenceService planStore) : ObservableObject
{
    public ObservableCollection<PlanCardViewModel> Plans { get; } = [];

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool isEmpty;

    public bool HasPlans => !IsEmpty;

    partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(HasPlans));

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;

        var userPlans = (await planStore.ListUserPlansAsync(cancellationToken).ConfigureAwait(false))
            .Select(Map)
            .ToList();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Plans.Clear();
            foreach (var plan in userPlans)
            {
                Plans.Add(plan);
            }

            IsEmpty = Plans.Count == 0;
            IsLoading = false;
        });
    }

    [RelayCommand]
    private async Task ActivateAsync(PlanCardViewModel planCard, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(planCard);

        var allPlans = await planStore.ListUserPlansAsync(cancellationToken).ConfigureAwait(false);
        foreach (var plan in allPlans)
        {
            plan.IsActive = plan.Id == planCard.Id;
            await planStore.SavePlanAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    [RelayCommand]
    private static Task OpenTemplatesAsync() => Shell.Current.GoToAsync(ForgeRoutes.PlanTemplates);

    [RelayCommand]
    private static Task OpenEditorAsync()
        => Shell.Current.GoToAsync(ForgeRoutes.PlanEditor, new Dictionary<string, object> { ["forge.plan"] = Guid.Empty });

    [RelayCommand]
    private static Task OpenScheduleAsync() => Shell.Current.GoToAsync(ForgeRoutes.PlanSchedule);

    private static PlanCardViewModel Map(TrainingPlan plan)
    {
        var activePrefix = plan.IsActive ? "Active · " : string.Empty;
        return new PlanCardViewModel(
            plan.Id,
            plan.Name,
            plan.Description,
            $"{activePrefix}{plan.Days.Count} days · {plan.TargetSessionsPerWeek} sessions/week",
            plan.IsActive);
    }
}

public sealed partial class PlanTemplatesViewModel(IPlanPersistenceService planStore) : ObservableObject
{
    public ObservableCollection<PlanTemplateViewModel> Templates { get; } = [];

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private PlanTemplateViewModel? selectedTemplate;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;

        var templates = PlanTemplateCatalogue.Templates.Select(Map).ToList();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Templates.Clear();
            foreach (var template in templates)
            {
                Templates.Add(template);
            }

            SelectedTemplate = Templates.FirstOrDefault();
            IsLoading = false;
        });
    }

    [RelayCommand]
    private void PreviewTemplate(PlanTemplateViewModel template) => SelectedTemplate = template;

    [RelayCommand]
    private async Task AdoptTemplateAsync(PlanTemplateViewModel template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);

        var source = PlanTemplateCatalogue.Templates.Single(plan => plan.Name == template.Name);
        var adoptedPlan = await planStore.AdoptTemplateAsync(source, cancellationToken).ConfigureAwait(false);

        await MainThread.InvokeOnMainThreadAsync(() =>
            Shell.Current.GoToAsync(ForgeRoutes.PlanEditor, new Dictionary<string, object> { ["forge.plan"] = adoptedPlan.Id }));
    }

    private static PlanTemplateViewModel Map(TrainingPlan template)
        => new(template.Name, template.Description, $"{template.Days.Count} days · about {template.TargetSessionsPerWeek} sessions/week",
            template.Days.OrderBy(day => day.Ordinal)
                .Select(day => new PlanDayPreviewViewModel(day.Name, string.Join(" · ", day.Exercises.Take(4).Select(exercise => exercise.ExerciseName))))
                .ToList());
}

public sealed partial class PlanEditorViewModel(IPlanPersistenceService planStore) : ObservableObject
{
    private TrainingPlan? plan;

    public ObservableCollection<EditorDayViewModel> Days { get; } = [];

    public ObservableCollection<string> BalanceLines { get; } = [];

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string estimatedDuration = string.Empty;

    [ObservableProperty]
    private string imbalanceWarning = string.Empty;

    [RelayCommand]
    public async Task LoadAsync(Guid planId, CancellationToken cancellationToken)
    {
        IsLoading = true;

        plan = planId == Guid.Empty
            ? new TrainingPlan { Name = "My plan", Description = "A custom training plan.", IsActive = true }
            : await planStore.GetPlanAsync(planId, cancellationToken).ConfigureAwait(false)
                ?? new TrainingPlan { Name = "My plan", Description = "A custom training plan.", IsActive = true };

        if (plan.Days.Count == 0)
        {
            plan.Days.Add(new PlanDay { Name = "Day 1", Ordinal = 0 });
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Name = plan.Name;
            RefreshAnalysis();
            IsLoading = false;
        });
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (plan is null)
        {
            return;
        }

        plan.Name = string.IsNullOrWhiteSpace(Name) ? "My plan" : Name.Trim();
        await planStore.SavePlanAsync(plan, cancellationToken).ConfigureAwait(false);
        RefreshAnalysis();
    }

    [RelayCommand]
    private void MoveDayUp(EditorDayViewModel day)
    {
        if (plan is null)
        {
            return;
        }

        var ordered = plan.Days.OrderBy(item => item.Ordinal).ToList();
        var index = ordered.FindIndex(item => item.Id == day.Id);
        if (index <= 0)
        {
            return;
        }

        (ordered[index - 1].Ordinal, ordered[index].Ordinal) = (ordered[index].Ordinal, ordered[index - 1].Ordinal);
        RefreshAnalysis();
    }

    [RelayCommand]
    private void AddDay()
    {
        if (plan is null)
        {
            return;
        }

        plan.Days.Add(new PlanDay { Name = $"Day {plan.Days.Count + 1}", Ordinal = plan.Days.Count });
        RefreshAnalysis();
    }

    [RelayCommand]
    private void AddExercise()
    {
        if (plan is null)
        {
            return;
        }

        var day = plan.Days.OrderBy(day => day.Ordinal).FirstOrDefault();
        if (day is null)
        {
            return;
        }

        var exercise = new PlannedExercise
        {
            ExerciseName = "New exercise",
            Pattern = MovementPattern.Push,
            PrimaryMuscle = "General",
            Ordinal = day.Exercises.Count,
            GroupKey = day.Exercises.Count % 2 == 0 ? "A1" : "A2"
        };
        for (var set = 1; set <= 3; set++)
        {
            exercise.Sets.Add(new PlannedSet { Ordinal = set, TargetRepsMin = 8, TargetRepsMax = 10, Rest = TimeSpan.FromSeconds(90), TargetRpe = 8m });
        }

        day.Exercises.Add(exercise);
        RefreshAnalysis();
    }

    [RelayCommand]
    private void AddTargetSet(EditorExerciseViewModel exerciseViewModel)
    {
        var exercise = plan?.Days.SelectMany(day => day.Exercises).SingleOrDefault(exercise => exercise.Id == exerciseViewModel.Id);
        if (exercise is null)
        {
            return;
        }

        var previous = exercise.Sets.OrderBy(set => set.Ordinal).LastOrDefault();
        exercise.Sets.Add(new PlannedSet
        {
            Ordinal = exercise.Sets.Count + 1,
            TargetRepsMin = previous?.TargetRepsMin ?? 8,
            TargetRepsMax = previous?.TargetRepsMax ?? 10,
            Rest = previous?.Rest ?? TimeSpan.FromSeconds(90),
            TargetRpe = previous?.TargetRpe ?? 8m,
            TargetLoad = previous?.TargetLoad,
            IsWarmUp = previous?.IsWarmUp ?? false
        });
        RefreshAnalysis();
    }

    private void RefreshAnalysis()
    {
        if (plan is null)
        {
            return;
        }

        Days.Clear();
        foreach (var day in plan.Days.OrderBy(day => day.Ordinal))
        {
            Days.Add(new EditorDayViewModel(
                day.Id,
                day.Name,
                Format(SessionDurationEstimator.Estimate(day)),
                day.Exercises.OrderBy(exercise => exercise.Ordinal).Select(exercise =>
                    new EditorExerciseViewModel(
                        exercise.Id,
                        exercise.ExerciseName,
                        $"{exercise.Sets.Count} × {exercise.Sets.FirstOrDefault()?.TargetRepsMin}-{exercise.Sets.FirstOrDefault()?.TargetRepsMax} · RPE {exercise.Sets.FirstOrDefault()?.TargetRpe:0.#} · rest {exercise.Sets.FirstOrDefault()?.Rest.TotalSeconds:0}s",
                        string.IsNullOrWhiteSpace(exercise.GroupKey) ? "Solo" : exercise.GroupKey!)).ToList()));
        }

        var total = plan.Days.Select(day => SessionDurationEstimator.Estimate(day)).Aggregate(TimeSpan.Zero, (left, right) => left + right);
        EstimatedDuration = $"Week estimate: {Format(total)}";

        var report = VolumeBalanceAnalyzer.Analyze(plan);
        BalanceLines.Clear();
        foreach (var line in report.SetsByMovementPattern.OrderBy(pair => pair.Key.ToString()).Select(pair => $"{pair.Key}: {pair.Value} working sets"))
        {
            BalanceLines.Add(line);
        }

        ImbalanceWarning = report.Warnings.Count > 0 ? report.Warnings[0].Message : "Volume balance looks usable.";
    }

    private static string Format(TimeSpan time) => time.TotalMinutes < 1 ? "0 min" : $"{Math.Round(time.TotalMinutes)} min";
}

public sealed partial class PlanScheduleViewModel(IPlanPersistenceService planStore) : ObservableObject
{
    public ObservableCollection<ScheduleCellViewModel> Days { get; } = [];

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private string reassurance = "Missed a day? Forge shifts the plan forward so you keep momentum instead of losing a streak.";

    public bool HasSchedule => !IsEmpty;

    partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(HasSchedule));

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;

        var userPlans = await planStore.ListUserPlansAsync(cancellationToken).ConfigureAwait(false);
        var plan = userPlans.Count > 0 ? userPlans[0] : null;

        var cells = plan is null ? [] : BuildCells(plan);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Days.Clear();
            foreach (var cell in cells)
            {
                Days.Add(cell);
            }

            IsEmpty = plan is null;
            IsLoading = false;
        });
    }

    [RelayCommand]
    private static Task OpenTemplatesAsync() => Shell.Current.GoToAsync(ForgeRoutes.PlanTemplates);

    private static List<ScheduleCellViewModel> BuildCells(TrainingPlan plan)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var schedule = PlanScheduler.Schedule(plan, weekStart, 4);
        var lookup = schedule.ToLookup(session => session.Date);
        var cells = new List<ScheduleCellViewModel>();
        for (var i = 0; i < 28; i++)
        {
            var date = weekStart.AddDays(i);
            var session = lookup[date].FirstOrDefault();
            cells.Add(new ScheduleCellViewModel(date.Day.ToString(CultureInfo.CurrentCulture), date.DayOfWeek.ToString()[..3], session?.Day.Name ?? string.Empty, session is not null, session?.WasShifted ?? false));
        }

        return cells;
    }
}
