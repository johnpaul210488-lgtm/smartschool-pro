using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace SmartSchool.Admin.Helpers;

/// <summary>
/// ObservableCollection jo bade data (hazaron rows) ko ek hi "Reset" notification se replace karti hai.
/// Clear() + Add() loop har item par UI ko alag refresh karwata hai (hazaron rows = UI jam).
/// Ye ek dafa replace karta hai, is liye grid/list fauran update hoti hai.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public BulkObservableCollection() { }

    public BulkObservableCollection(IEnumerable<T> items) => ReplaceAll(items);

    public void ReplaceAll(IEnumerable<T> items)
    {
        var list = items as IList<T> ?? items.ToList();
        Items.Clear();
        foreach (var item in list) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
