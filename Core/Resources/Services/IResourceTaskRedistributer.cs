using IzziAutomationCore.Resources.Entities;

namespace IzziAutomationCore.Resources.Services;

public interface IResourceTaskRedistributer
{
    public void Redistribute(IEnumerable<PopulatedResource> populatedResources);
}