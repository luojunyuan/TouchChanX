using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ObservableCollections;

namespace TouchChanX.UWP;

// 因为 ToNotifyCollectionChangedSlim 拿到的 INotifyCollectionChangedSynchronizedViewList<T>
// ItemsSource 似乎不接受这个 winrt abi 接口，即使打开了 AllowUnsafeBlocks 也不行
// 所以自己封装一个 ObservableCollection<T> 支持集合通知，并且也让调用方用起来更反应式
public sealed class ObservableListBindableView<TView> : ObservableCollection<TView>
{
    private ObservableListBindableView()
    {
    }

    internal static ObservableListBindableView<TView> Create<TSource>(
        ObservableList<TSource> source,
        Func<TSource, TView> map)
    {
        var view = new ObservableListBindableView<TView>();
        foreach (var item in source)
        {
            view.Items.Add(map(item));
        }

        source.CollectionChanged += OnSourceCollectionChanged;
        return view;

        void OnSourceCollectionChanged(in NotifyCollectionChangedEventArgs<TSource> e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.IsSingleItem)
                    {
                        view.InsertItem(e.NewStartingIndex, map(e.NewItem));
                    }
                    else
                    {
                        InsertRange(e.NewStartingIndex, e.NewItems);
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.IsSingleItem)
                    {
                        DisposeItem(view[e.OldStartingIndex]);
                        view.RemoveItem(e.OldStartingIndex);
                    }
                    else
                    {
                        view.RemoveRange(e.OldStartingIndex, e.OldItems.Length);
                    }
                    break;

                case NotifyCollectionChangedAction.Replace:
                    if (e.IsSingleItem)
                    {
                        DisposeItem(view[e.NewStartingIndex]);
                        view.SetItem(e.NewStartingIndex, map(e.NewItem));
                    }
                    else
                    {
                        ReplaceRange(e.NewStartingIndex, e.NewItems);
                    }
                    break;

                case NotifyCollectionChangedAction.Move:
                    view.MoveItem(e.OldStartingIndex, e.NewStartingIndex);
                    break;

                default:
                    view.ResetFromSource(source, map);
                    break;
            }
        }

        void InsertRange(int index, ReadOnlySpan<TSource> items)
        {
            for (var i = 0; i < items.Length; i++)
            {
                view.InsertItem(index + i, map(items[i]));
            }
        }

        void ReplaceRange(int index, ReadOnlySpan<TSource> items)
        {
            for (var i = 0; i < items.Length; i++)
            {
                DisposeItem(view[index + i]);
                view.SetItem(index + i, map(items[i]));
            }
        }
    }

    private void RemoveRange(int index, int count)
    {
        for (var i = 0; i < count; i++)
        {
            DisposeItem(this[index]);
            RemoveItem(index);
        }
    }

    private void ResetFromSource<TSource>(ObservableList<TSource> source, Func<TSource, TView> map)
    {
        foreach (var item in Items)
        {
            DisposeItem(item);
        }

        Items.Clear();
        foreach (var item in source)
        {
            Items.Add(map(item));
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private static void DisposeItem(TView item)
    {
        if (item is IDisposable disposable)
            disposable.Dispose();
    }
}

public static class ObservableListBindableViewExtensions
{
    public static ObservableListBindableView<TView> ToBindableView<TSource, TView>
        (this ObservableList<TSource> source, Func<TSource, TView> map)
    {
        return ObservableListBindableView<TView>.Create(source, map);
    }
}
