using System.Collections;
using System.Collections.Specialized;
using ObservableCollections;
using Windows.Foundation.Collections;
using Windows.UI.Xaml.Interop;

namespace TouchChanX.UWP;

// 自定义 INotifyCollectionChanged 集合跨 UWP ABI 发送 Add/Remove 时，
// NotifyCollectionChangedEventArgs 会把项目包装成内部 ReadOnlyList，CsWinRT 无法将其投影为 IBindableVector。
// ObservableCollection 有框架内建投影不会遇到该问题，但这里需要以 Path 维持字典项与 ViewModel 的稳定身份映射，
// 因此直接实现 IBindableObservableVector：增删发送原生 VectorChanged，同 Path 的 record 替换只更新原 ViewModel。
public sealed partial class GameEntryCollectionView : IBindableObservableVector, IDisposable
{
    private readonly ObservableDictionary<string, GameEntry> _source;
    private readonly List<GameEntryViewModel> _items = [];
    private readonly Dictionary<string, GameEntryViewModel> _views = new(StringComparer.OrdinalIgnoreCase);

    internal GameEntryCollectionView(ObservableDictionary<string, GameEntry> source)
    {
        _source = source;
        foreach (var game in source)
        {
            AddView(game.Key, game.Value);
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

    private void OnSourceCollectionChanged(in NotifyCollectionChangedEventArgs<KeyValuePair<string, GameEntry>> args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Add:
                AddFromSource(args.NewItem.Key, args.NewItem.Value);
                break;
            case NotifyCollectionChangedAction.Remove:
                RemoveFromSource(args.OldItem.Key);
                break;
            case NotifyCollectionChangedAction.Replace:
                _views[args.OldItem.Key].Update(args.NewItem.Value);
                break;
            default:
                ResetFromSource();
                break;
        }
    }

    private void AddFromSource(string path, GameEntry game)
    {
        var index = _items.Count;
        AddView(path, game);
        RaiseVectorChanged(CollectionChange.ItemInserted, index);
    }

    private void AddView(string path, GameEntry game)
    {
        var view = new GameEntryViewModel(game);
        _items.Add(view);
        _views.Add(path, view);
    }

    private void RemoveFromSource(string path)
    {
        var item = _views[path];
        var index = _items.IndexOf(item);
        _views.Remove(path);
        _items.RemoveAt(index);
        RaiseVectorChanged(CollectionChange.ItemRemoved, index);
    }

    private void ResetFromSource()
    {
        _items.Clear();
        _views.Clear();
        foreach (var game in _source)
        {
            AddView(game.Key, game.Value);
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
        this ObservableDictionary<string, GameEntry> source) => new(source);
}
