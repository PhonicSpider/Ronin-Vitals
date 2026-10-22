using System.Collections.ObjectModel;

namespace RoninVitals.ViewModels;

public sealed class VendorGroupViewModel
{
    public required string VendorName { get; init; }
    public required ObservableCollection<DriverRowViewModel> Rows { get; init; }
}
