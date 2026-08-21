namespace Forge.App.Features.Plans;

public sealed record PlanCardViewModel(Guid Id, string Name, string Description, string Summary, bool IsActive);
public sealed record PlanDayPreviewViewModel(string Name, string Summary);
public sealed record PlanTemplateViewModel(string Name, string Description, string Summary, IReadOnlyList<PlanDayPreviewViewModel> Days);
public sealed record EditorDayViewModel(Guid Id, string Name, string Duration, IReadOnlyList<EditorExerciseViewModel> Exercises);
public sealed record EditorExerciseViewModel(Guid Id, string Name, string Prescription, string GroupKey);
public sealed record ScheduleCellViewModel(string DateText, string DayName, string SessionTitle, bool HasSession, bool WasShifted);
