namespace FireWill.App.Services.Background;

public interface IBackgroundCatalog
{
    IReadOnlyList<BackgroundOption> Options { get; }

    IReadOnlyList<BackgroundDescriptor> RotationItems { get; }

    BackgroundDescriptor Get(BackgroundSelection selection);
}

public sealed class BackgroundCatalog : IBackgroundCatalog
{
    private static readonly BackgroundDescriptor SusanooMadara = new(
        BackgroundSelection.SusanooMadara,
        "须佐斑",
        "susanoo-madara.mp4",
        "FireWill.Assets.Backgrounds.susanoo-madara.mp4",
        "d5cb7ad216cbb3cd0ad4aed1e3fbed82cf4c49716a16dca581ed3ba9a143f715",
        TimeSpan.FromSeconds(8.1));

    private static readonly BackgroundDescriptor FlowingSasuke = new(
        BackgroundSelection.FlowingSasuke,
        "流年佐助",
        "flowing-sasuke.mp4",
        "FireWill.Assets.Backgrounds.flowing-sasuke.mp4",
        "1fdee0e94998eb5442feb25b3e86901f9f1ca74fd9fce938f89af5dfde357ffe",
        TimeSpan.FromSeconds(15));

    private static readonly IReadOnlyList<BackgroundDescriptor> Items =
        Array.AsReadOnly(new[] { SusanooMadara, FlowingSasuke });

    private static readonly IReadOnlyList<BackgroundOption> Choices = Array.AsReadOnly(
    new BackgroundOption[]
    {
        new(BackgroundSelection.SusanooMadara, "须佐斑"),
        new(BackgroundSelection.FlowingSasuke, "流年佐助"),
        new(BackgroundSelection.DynamicRotation, "动态流转"),
    });

    public IReadOnlyList<BackgroundOption> Options => Choices;

    public IReadOnlyList<BackgroundDescriptor> RotationItems => Items;

    public BackgroundDescriptor Get(BackgroundSelection selection)
    {
        return selection switch
        {
            BackgroundSelection.SusanooMadara => SusanooMadara,
            BackgroundSelection.FlowingSasuke => FlowingSasuke,
            BackgroundSelection.DynamicRotation => SusanooMadara,
            _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, null),
        };
    }
}
