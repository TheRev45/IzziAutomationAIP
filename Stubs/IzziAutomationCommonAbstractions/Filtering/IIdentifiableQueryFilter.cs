using IzziAutomationCommonAbstractions.Identification;

namespace IzziAutomationCommonAbstractions.Filtering;

public interface IIdentifiableQueryFilter<T>
{
    IQueryable<T> FilterQuery(IQueryable<T> query);
    IdentificationInformation GetId();
}
