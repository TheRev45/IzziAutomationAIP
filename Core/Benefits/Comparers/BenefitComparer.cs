using IzziAutomationCore.Benefits.Entities;

namespace IzziAutomationCore.Benefits.Comparers;

internal sealed class BenefitComparer : IComparer<Benefit>
{
    public int Compare(Benefit? x, Benefit? y) => (x, y) switch
    {
        (null, null) => 0,
        (_, null) => 1,
        (null, _) => -1,
        (InfiniteBenefit, InfiniteBenefit) => 0,
        (InfiniteBenefit, _) => 1,
        (_, InfiniteBenefit) => -1,
        (FloatBenefit ix, FloatBenefit iy) => Convert.ToInt32(ix.Benefit - iy.Benefit),
        _ => 0
    };
}