using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Finance.Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Finance.Application;

/// <summary><see cref="ExchangeRates"/>: one unit of each currency in the base <see cref="Currency"/>.</summary>
public sealed record FinanceSettingsDto(string Currency, IReadOnlyList<string> Available, IReadOnlyDictionary<string, decimal> ExchangeRates);

public sealed record SaveFinanceSettingsRequest(string Currency, IReadOnlyDictionary<string, decimal>? ExchangeRates = null);

/// <summary>
/// The academy's base currency (salaries, payouts, expenses, report totals, and the default for new
/// student billings) and the exchange rates that bring other billing currencies into report totals.
/// </summary>
public interface IFinanceSettingsService
{
    Task<FinanceSettingsDto> GetAsync(CancellationToken ct = default);
    Task<FinanceSettingsDto> SaveAsync(SaveFinanceSettingsRequest request, CancellationToken ct = default);
    Task<string> CurrencyAsync(CancellationToken ct = default);

    /// <summary>Rates into the base currency; the base currency itself is 1.</summary>
    Task<IReadOnlyDictionary<string, decimal>> RatesAsync(CancellationToken ct = default);
}

internal sealed class FinanceSettingsService(IFinanceDbContext db, FinanceAccess access, IReportCache reports, IAuditTrail audit)
    : IFinanceSettingsService
{
    public async Task<FinanceSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        return new FinanceSettingsDto(settings?.Currency ?? Currencies.Default, Currencies.All, settings?.Rates() ?? new Dictionary<string, decimal>());
    }

    public async Task<string> CurrencyAsync(CancellationToken ct = default) =>
        await db.Settings.Select(s => s.Currency).FirstOrDefaultAsync(ct) ?? Currencies.Default;

    public async Task<IReadOnlyDictionary<string, decimal>> RatesAsync(CancellationToken ct = default)
    {
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        var rates = new Dictionary<string, decimal>(settings?.Rates() ?? new Dictionary<string, decimal>(), StringComparer.OrdinalIgnoreCase)
        {
            [settings?.Currency ?? Currencies.Default] = 1m,
        };
        return rates;
    }

    /// <summary>
    /// Existing billings and invoices keep the currency they were created in. Only new billings, and
    /// report totals from now on, use the new base currency.
    /// </summary>
    public async Task<FinanceSettingsDto> SaveAsync(SaveFinanceSettingsRequest request, CancellationToken ct = default)
    {
        var currency = request.Currency.ToUpperInvariant();
        if (!Currencies.IsKnown(currency))
        {
            throw new BusinessRuleException($"Currency must be one of {string.Join(", ", Currencies.All)}.");
        }

        var unknown = (request.ExchangeRates?.Keys ?? []).Where(k => !Currencies.IsKnown(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new BusinessRuleException($"Unknown currencies: {string.Join(", ", unknown)}.");
        }

        var settings = await db.Settings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new FinanceSettings();
            db.Settings.Add(settings);
        }

        settings.Currency = currency;
        if (request.ExchangeRates is { } rates)
        {
            settings.ExchangeRates = FinanceSettings.FormatRates(
                rates.Where(r => !r.Key.Equals(currency, StringComparison.OrdinalIgnoreCase)).ToDictionary(r => r.Key, r => r.Value));
        }

        await audit.RecordAsync("finance.settings", nameof(FinanceSettings), null, new { currency, request.ExchangeRates }, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);
        return await GetAsync(ct);
    }
}

internal sealed class SaveFinanceSettingsValidator : AbstractValidator<SaveFinanceSettingsRequest>
{
    public SaveFinanceSettingsValidator()
    {
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleForEach(x => x.ExchangeRates).Must(r => r.Value > 0 && r.Value < 1_000_000)
            .WithMessage("Exchange rates must be positive.")
            .When(x => x.ExchangeRates is not null);
    }
}

/// <summary>Adds amounts in several currencies into one, using the academy's rates.</summary>
public static class CurrencyMath
{
    /// <summary>The total in the base currency, and the currencies that had no rate (left out).</summary>
    public static (decimal Total, IReadOnlyList<string> Missing) ToBase(IEnumerable<(string Currency, decimal Amount)> amounts, IReadOnlyDictionary<string, decimal> rates)
    {
        decimal total = 0;
        var missing = new SortedSet<string>();
        foreach (var (currency, amount) in amounts)
        {
            if (rates.TryGetValue(currency, out var rate))
            {
                total += amount * rate;
            }
            else if (amount != 0)
            {
                missing.Add(currency);
            }
        }

        return (Math.Round(total, 2), missing.ToList());
    }

    public static IReadOnlyDictionary<string, decimal> ByCurrency(IEnumerable<(string Currency, decimal Amount)> amounts) =>
        amounts.GroupBy(a => a.Currency).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
}
