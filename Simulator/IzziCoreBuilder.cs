using IzziAutomationCore;
using IzziAutomationCore.Benefits.Comparers;
using IzziAutomationCore.Benefits.Rules;
using IzziAutomationCore.Resources.Comparers;
using IzziAutomationCore.Resources.Services;
using ResultAndOption.Options;

namespace IzziAutomationSimulator;

/// <summary>
/// Constructs a fully-wired IIzziCore instance using the internal Core implementations.
///
/// Wiring decisions:
///   - SimCompatibleQueueChecker: all resources compatible with all queues (login/logout
///     sequences in ResourceState handle user-switching already).
///   - MustRunRule: the only rule that needs no IAssignedResourceForQueueAccessor.
///     Min/MaxResources rules are no-ops given ConfigurableQueueParameters defaults
///     (MinResources=0, MaxResources=int.MaxValue), so omitting them is correct.
///   - BiasedResourceBenefitCalculator bias=0.5f: balanced weight between raw capacity
///     and SLA-breach history.
/// </summary>
internal static class IzziCoreBuilder
{
    internal static IIzziCore Build(TimeSpan discTime)
    {
        var checker      = new SimCompatibleQueueChecker();
        var populator    = new PriorityAndQueueBasedResourcePopulator(checker);
        var redistributer = new PriorityBasedResourceTaskRedistributer();
        var calculator   = new BiasedResourceBenefitCalculator(bias: 0.5f);
        var rule         = new MustRunRule();
        var benefitCmp   = new BenefitComparer();
        var resourceCmp  = new BenefitResourceComparer(benefitCmp);
        var getter       = new MostBeneficialResourceGetter(resourceCmp, calculator, rule);

        Option<TimeSpan> discTimeOption = discTime;   // implicit Option<T> conversion
        return new IzziCore(discTimeOption, redistributer, populator, getter);
    }
}
