using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ObservableCollections;

namespace TouchChanX.UWP;

// 因为 ToNotifyCollectionChangedSlim 拿到的 INotifyCollectionChangedSynchronizedViewList<T>
// ItemsSource 似乎不接受这个 winrt abi 接口，即使打开了 AllowUnsafeBlocks 也不行
// 所以自己封装一个 ObservableCollection<T> 支持集合通知，并且也让调用方用起来更反应式
public sealed class ObservableListBindableView<T> : ObservableCollection<T>
{
    private readonly ObservableList<T> _source;

    public ObservableListBindableView(ObservableList<T> source)
    {
        _source = source;
        foreach (var item in source)
        {
            Items.Add(item);
        }

        source.CollectionChanged += OnSourceCollectionChanged;
    }

    private void OnSourceCollectionChanged(in ObservableCollections.NotifyCollectionChangedEventArgs<T> e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.IsSingleItem)
                {
                    InsertItem(e.NewStartingIndex, e.NewItem);
                }
                else
                {
                    InsertRange(e.NewStartingIndex, e.NewItems);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.IsSingleItem)
                {
                    RemoveItem(e.OldStartingIndex);
                }
                else
                {
                    RemoveRange(e.OldStartingIndex, e.OldItems.Length);
                }
                break;

            case NotifyCollectionChangedAction.Replace:
                if (e.IsSingleItem)
                {
                    SetItem(e.NewStartingIndex, e.NewItem);
                }
                else
                {
                    ReplaceRange(e.NewStartingIndex, e.NewItems);
                }
                break;

            case NotifyCollectionChangedAction.Move:
                MoveItem(e.OldStartingIndex, e.NewStartingIndex);
                break;

            default:
                ResetFromSource();
                break;
        }
    }

    private void InsertRange(int index, ReadOnlySpan<T> items)
    {
        for (var i = 0; i < items.Length; i++)
        {
            InsertItem(index + i, items[i]);
        }
    }

    private void RemoveRange(int index, int count)
    {
        for (var i = 0; i < count; i++)
        {
            RemoveItem(index);
        }
    }

    private void ReplaceRange(int index, ReadOnlySpan<T> items)
    {
        for (var i = 0; i < items.Length; i++)
        {
            SetItem(index + i, items[i]);
        }
    }

    private void ResetFromSource()
    {
        Items.Clear();
        foreach (var item in _source)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public static class ObservableListBindableViewExtensions
{
    public static ObservableListBindableView<T> ToBindableView<T>
        (this ObservableList<T> source)
    {
        return new ObservableListBindableView<T>(source);
    }
}