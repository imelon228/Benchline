namespace Benchline.Components;

public sealed record ThemeSetting(bool Dark, Func<Task> Toggle);
