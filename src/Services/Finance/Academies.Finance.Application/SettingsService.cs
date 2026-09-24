using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Finance.Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Finance.Application;

public sealed record FinanceSettingsDto(string Currency, IReadOnlyList<string> Available);

public sealed record SaveFinanceSettingsRequest(string Currency);

/// <summary>The academy's billing currency (USD or EGP). New plans, reports and salaries use it.</summary>
public interface IFinanceSettingsService
{
    Task<FinanceSettingsDto> GetAsync(CancellationToken ct = default);
    Task<FinanceSettingsDto> SaveAsync(SaveFinanceSettingsRequest request, CancellationToken ct = default);
    Task<string> CurrencyAsync(CancellationToken ct = default);
}

internal sealed class FinanceSettingsService(IFinanceDbContext db, FinanceAccess access, IReportCache reports, IAuditTrail audit)
    : IFinanceSettingsService
{
    public async Task<FinanceSettingsDto> GetAsync(CancellationToken ct = default) =>
        new(await CurrencyAsync(ct), Currencies.All);

    public async Task<string> CurrencyAsync(CancellationToken ct = default) =>
        await db.Settings.Select(s => s.Currency).FirstOrDefaultAsync(ct) ?? Currencies.Default;

    /// <summary>
    /// Existing plans keep the currency they were created in. Only new plans, and reports from
    /// now on, use the new one.
    /// </summary>
    public async Task<FinanceSettingsDto> SaveAsync(SaveFinanceSettingsRequest request, CancellationToken ct = default)
    {
        var currency = request.Currency.ToUpperInvariant();
        if (!Currencies.All.Contains(currency))
        {
            throw new BusinessRuleException($"Currency must be one of {string.Join(", ", Currencies.All)}.");
        }

        var settings = await db.Settings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            db.Settings.Add(new FinanceSettings { Currency = currency });
        }
        else
        {
            settings.Currency = currency;
        }

        await audit.RecordAsync("finance.settings", nameof(FinanceSettings), null, new { currency }, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);
        return await GetAsync(ct);
    }
}

internal sealed class SaveFinanceSettingsValidator : AbstractValidator<SaveFinanceSettingsRequest>
{
    public SaveFinanceSettingsValidator() => RuleFor(x => x.Currency).NotEmpty().Length(3);
}
