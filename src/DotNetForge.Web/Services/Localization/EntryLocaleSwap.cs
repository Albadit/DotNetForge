using DotNetForge.Content;
using DotNetForge.Shared.Entities;

namespace DotNetForge.Web.Services.Localization;

/// <summary>Moves entry content when the default locale changes (<see cref="EntryTranslationService.SwapDefaultAsync"/>).</summary>
public sealed class EntryLocaleSwap(EntryTranslationService translations) : IDefaultLocaleSwap
{
    public Task SwapAsync(Locale oldDefault, Locale newDefault, CancellationToken ct) =>
        translations.SwapDefaultAsync(oldDefault.Id, newDefault.Id, ct);
}
