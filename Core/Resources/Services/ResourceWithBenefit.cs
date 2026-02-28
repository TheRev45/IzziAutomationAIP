using IzziAutomationCore.Benefits.Entities;
using IzziAutomationCore.Resources.Entities;

namespace IzziAutomationCore.Resources.Services;

public sealed record ResourceWithBenefit(PopulatedResource Resource, Benefit Benefit);