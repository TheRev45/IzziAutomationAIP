using IzziAutomationCore.Resources.Entities;

namespace IzziAutomationCore.Resources.Comparers;

internal sealed class TieBreakComparer : IComparer<PopulatedResource>
{
    public int Compare(PopulatedResource? x, PopulatedResource? y) => (x, y) switch
    {
        (null, null) => 0,
        (_, null) => 1,
        (null, _) => -1,
        ({ } a, { } b) => GetComparisonValue(a, b)
    };

    private static int GetComparisonValue(PopulatedResource x, PopulatedResource y)
    {
        int mustRunComparison = MustRunComparison(x, y);
        if (mustRunComparison != 0)
        {
            return mustRunComparison;
        }

        int criticalityComparison = CriticalityComparison(x, y);
        return criticalityComparison != 0
            ? criticalityComparison
            : SlaComparison(x, y);
    }

    private static int MustRunComparison(PopulatedResource x, PopulatedResource y) =>
        (x.Queue.Parameters.MustRun, y.Queue.Parameters.MustRun) switch
        {
            (true, false) => 1,
            (false, true) => -1,
            (_, _) => 0
        };

    private static int CriticalityComparison(PopulatedResource x, PopulatedResource y) =>
        x.Queue.Parameters.Criticality - y.Queue.Parameters.Criticality;

    private static int SlaComparison(PopulatedResource x, PopulatedResource y) =>
        Convert.ToInt32((y.Queue.Parameters.Sla - x.Queue.Parameters.Sla).Ticks);
}