using IzziAutomationCommonAbstractions.Filtering;

namespace IzziAutomationDatabase.Services;

public interface IEntityService<T> where T : notnull
{
    Task<List<T>> Filtered(IIdentifiableQueryFilter<T> filter);
}
