namespace Aiursoft.HowToCookViewer.Util;

public class DisjointSetUnion(int size)
{
    private readonly int[] _pa = Enumerable.Range(0, size).ToArray();

    private int Find(int elem)
    {
        return _pa[elem] == elem ? elem : _pa[elem] = Find(_pa[elem]);
    }

    public void Union(int a, int b)
    {
        if (a == b) return;
        _pa[Find(a)] = Find(b);
    }

    public IEnumerable<List<int>> AsGroups(bool ignoreSingletons = false)
    {
        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < _pa.Length; i++)
        {
            var root = Find(i);
            if (ignoreSingletons && root == i) continue;
            if (groups.TryGetValue(root, out var value))
            {
                value.Add(i);
            }
            else
            {
                groups.Add(root, [i]);
            }
        }

        if (ignoreSingletons)
        {
            foreach (var (id, value) in groups)
            {
                value.Add(id);
            }
        }
        return groups.Values;
    }
}
