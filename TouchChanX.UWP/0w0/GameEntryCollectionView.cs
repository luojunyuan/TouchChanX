using System.Collections;
using System.Collections.Specialized;
using ObservableCollections;
using Windows.Foundation.Collections;
using Windows.UI.Xaml.Interop;

namespace TouchChanX.UWP;

// 自定义 INotifyCollectionChanged 集合跨 UWP ABI 发送 Add/Remove 时，
// NotifyCollectionChangedEventArgs 会把项目包装成内部 ReadOnlyList，CsWinRT 无法将其投影为 IBindableVector。
// ObservableCollection 有框架内建投影不会遇到该问题，因此这里直接实现 IBindableObservableVector。
// 视图按 ObservableList 的索引同步；同 Path 的 record 替换只更新原 ViewModel，只有实体真的改变才替换 ViewModel。
public sealed partial class GameEntryCollectionView : IBindableObservableVector, IDisposable
{
    private readonly ObservableList<GameEntry> _source;
    private readonly List<GameEntryViewModel> _items = [];

    internal GameEntryCollectionView(ObservableList<GameEntry> source)
    {
        _source = source;
        foreach (var game in source)
        {
            _items.Add(new GameEntryViewModel(game));
        }

        source.CollectionChanged += OnSourceCollectionChanged;
    }

    public event BindableVectorChangedEventHandler? VectorChanged;

    public int Count => _items.Count;

    public bool IsFixedSize => true;

    public bool IsReadOnly => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    object? IList.this[int index]
    {
        get => _items[index];
        set => throw new NotSupportedException();
    }

    private void OnSourceCollectionChanged(in NotifyCollectionChangedEventArgs<GameEntry> args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Add:
                AddFromSource(args);
                break;
            case NotifyCollectionChangedAction.Remove:
                RemoveFromSource(args);
                break;
            case NotifyCollectionChangedAction.Replace:
                ReplaceFromSource(args);
                break;
            case NotifyCollectionChangedAction.Move:
                MoveFromSource(args.OldStartingIndex, args.NewStartingIndex);
                break;
            default:
                ResetFromSource();
                break;
        }
    }

    private void AddFromSource(in NotifyCollectionChangedEventArgs<GameEntry> args)
    {
        if (args.IsSingleItem)
        {
            _items.Insert(args.NewStartingIndex, new GameEntryViewModel(args.NewItem));
            RaiseVectorChanged(CollectionChange.ItemInserted, args.NewStartingIndex);
            return;
        }

        for (var offset = 0; offset < args.NewItems.Length; offset++)
        {
            var index = args.NewStartingIndex + offset;
            _items.Insert(index, new GameEntryViewModel(args.NewItems[offset]));
            RaiseVectorChanged(CollectionChange.ItemInserted, index);
        }
    }

    private void RemoveFromSource(in NotifyCollectionChangedEventArgs<GameEntry> args)
    {
        var count = args.IsSingleItem ? 1 : args.OldItems.Length;
        for (var offset = 0; offset < count; offset++)
        {
            _items.RemoveAt(args.OldStartingIndex);
            RaiseVectorChanged(CollectionChange.ItemRemoved, args.OldStartingIndex);
        }
    }

    private void ReplaceFromSource(in NotifyCollectionChangedEventArgs<GameEntry> args)
    {
        if (!args.IsSingleItem)
        {
            ResetFromSource();
            return;
        }

        var index = args.NewStartingIndex;
        if (StringComparer.OrdinalIgnoreCase.Equals(args.OldItem.Path, args.NewItem.Path))
        {
            _items[index].Update(args.NewItem);
            return;
        }

        _items[index] = new GameEntryViewModel(args.NewItem);
        RaiseVectorChanged(CollectionChange.ItemChanged, index);
    }

    private void MoveFromSource(int oldIndex, int newIndex)
    {
        var item = _items[oldIndex];
        _items.RemoveAt(oldIndex);
        _items.Insert(newIndex, item);
        RaiseVectorChanged(CollectionChange.Reset, 0);
    }

    private void ResetFromSource()
    {
        var existingViews = _items.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        _items.Clear();
        foreach (var game in _source)
        {
            if (existingViews.TryGetValue(game.Path, out var view))
            {
                view.Update(game);
                _items.Add(view);
            }
            else
            {
                _items.Add(new GameEntryViewModel(game));
            }
        }

        RaiseVectorChanged(CollectionChange.Reset, 0);
    }

    private void RaiseVectorChanged(CollectionChange change, int index) =>
        VectorChanged?.Invoke(this, new GameEntryVectorChangedEventArgs(change, (uint)index));

    public void Dispose() => _source.CollectionChanged -= OnSourceCollectionChanged;

    public bool Contains(object? value) => value is GameEntryViewModel item && _items.Contains(item);

    public int IndexOf(object? value) => value is GameEntryViewModel item ? _items.IndexOf(item) : -1;

    public IEnumerator GetEnumerator() => _items.GetEnumerator();

    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    public int Add(object? value) => throw new NotSupportedException();

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();
}

internal sealed partial class GameEntryVectorChangedEventArgs(CollectionChange collectionChange, uint index)
    : IVectorChangedEventArgs
{
    public CollectionChange CollectionChange { get; } = collectionChange;

    public uint Index { get; } = index;
}

public static class GameEntryCollectionViewExtensions
{
    public static GameEntryCollectionView ToNotifyCollectionChangedSlimCompact(
        this ObservableList<GameEntry> source) => new(source);
}
