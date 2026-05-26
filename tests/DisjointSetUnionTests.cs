using Aiursoft.HowToCookViewer.Util;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class DisjointSetUnionTests
{
    [TestMethod]
    public void Union_TwoElements_TheyBelongToSameGroup()
    {
        var dsu = new DisjointSetUnion(5);
        dsu.Union(0, 1);
        var groups = dsu.AsGroups().ToList();
        Assert.IsTrue(groups.Any(g => g.Contains(0) && g.Contains(1)),
            "Elements 0 and 1 should be in the same group after union.");
    }

    [TestMethod]
    public void Union_TransitiveClosure_AllThreeInSameGroup()
    {
        var dsu = new DisjointSetUnion(5);
        dsu.Union(0, 1);
        dsu.Union(1, 2); // 0-1-2 should all be in the same group
        var groups = dsu.AsGroups().ToList();
        Assert.IsTrue(groups.Any(g => g.Contains(0) && g.Contains(1) && g.Contains(2)),
            "0, 1, 2 should be in the same group via transitive closure.");
    }

    [TestMethod]
    public void AsGroups_ReturnsAllElements()
    {
        var dsu = new DisjointSetUnion(4);
        dsu.Union(0, 1);
        var groups = dsu.AsGroups().ToList();
        var allElements = groups.SelectMany(g => g).ToHashSet();
        Assert.AreEqual(4, allElements.Count, "All 4 elements should appear in groups.");
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, allElements.ToList());
    }

    [TestMethod]
    public void AsGroups_IgnoreSingletons_ExcludesSingletons()
    {
        var dsu = new DisjointSetUnion(5);
        dsu.Union(0, 1); // Only 0 and 1 are connected
        var groups = dsu.AsGroups(ignoreSingletons: true).ToList();
        Assert.AreEqual(1, groups.Count, "Only one group should exist when ignoring singletons.");
        var group = groups[0];
        CollectionAssert.AreEquivalent(new[] { 0, 1 }, group,
            "The only group should contain 0 and 1.");
    }

    [TestMethod]
    public void AsGroups_IgnoreSingletons_AllSingletons_ReturnsEmpty()
    {
        var dsu = new DisjointSetUnion(3);
        // No unions — every element is its own singleton
        var groups = dsu.AsGroups(ignoreSingletons: true).ToList();
        Assert.AreEqual(0, groups.Count, "Should return empty when all elements are singletons.");
    }

    [TestMethod]
    public void AsGroups_IgnoreSingletons_IncludesRootInGroup()
    {
        var dsu = new DisjointSetUnion(3);
        dsu.Union(0, 1); // root=1, element 0 points to 1
        var groups = dsu.AsGroups(ignoreSingletons: true).ToList();
        Assert.AreEqual(1, groups.Count);
        Assert.IsTrue(groups[0].Contains(1), "Root element should be included in the group.");
        Assert.IsTrue(groups[0].Contains(0), "Non-root element should be included.");
    }

    [TestMethod]
    public void Union_SameElement_DoesNothing()
    {
        var dsu = new DisjointSetUnion(3);
        dsu.Union(1, 1); // self-union, should not throw
        var groups = dsu.AsGroups().ToList();
        Assert.AreEqual(3, groups.Count, "Self-union should not merge anything.");
    }

    [TestMethod]
    public void Union_AlreadyUnited_DoesNothing()
    {
        var dsu = new DisjointSetUnion(3);
        dsu.Union(0, 1);
        dsu.Union(0, 1); // duplicate union, should not throw or change state
        var groups = dsu.AsGroups().ToList();
        // Groups: {0,1} and {2} = 2 groups
        Assert.AreEqual(2, groups.Count);
    }

    [TestMethod]
    public void AsGroups_MultipleDisjointGroups()
    {
        var dsu = new DisjointSetUnion(6);
        dsu.Union(0, 1);
        dsu.Union(2, 3);
        dsu.Union(4, 5);
        var groups = dsu.AsGroups().ToList();
        Assert.AreEqual(3, groups.Count, "Should have 3 disjoint groups.");
    }

    [TestMethod]
    public void AsGroups_MixedSingletonsAndGroups()
    {
        var dsu = new DisjointSetUnion(5);
        dsu.Union(0, 1);  // group {0,1}
        // 2, 3, 4 are singletons
        var groups = dsu.AsGroups().ToList();
        Assert.AreEqual(4, groups.Count, "Should have 1 group + 3 singletons = 4 groups.");
    }

    [TestMethod]
    public void AsGroups_LargeChain()
    {
        const int n = 100;
        var dsu = new DisjointSetUnion(n);
        for (var i = 0; i < n - 1; i++)
            dsu.Union(i, i + 1); // chain all together
        var groups = dsu.AsGroups().ToList();
        Assert.AreEqual(1, groups.Count, "All 100 elements should be in one group.");
        Assert.AreEqual(n, groups[0].Count, "The single group should contain all elements.");
    }

    [TestMethod]
    public void Union_ReverseOrder_SameResult()
    {
        var dsuForward = new DisjointSetUnion(3);
        dsuForward.Union(0, 1);
        dsuForward.Union(1, 2);
        var forward = dsuForward.AsGroups().First(g => g.Contains(0));

        var dsuReverse = new DisjointSetUnion(3);
        dsuReverse.Union(2, 1);
        dsuReverse.Union(1, 0);
        var reverse = dsuReverse.AsGroups().First(g => g.Contains(0));

        CollectionAssert.AreEquivalent(forward, reverse,
            "Union direction should not affect final group membership.");
    }

    [TestMethod]
    public void AsGroups_SizeZero_ReturnsEmpty()
    {
        var dsu = new DisjointSetUnion(0);
        var groups = dsu.AsGroups().ToList();
        Assert.AreEqual(0, groups.Count);
    }

    [TestMethod]
    public void AsGroups_SizeOne_IgnoreSingletonsFalse_ReturnsOneGroup()
    {
        var dsu = new DisjointSetUnion(1);
        var groups = dsu.AsGroups(ignoreSingletons: false).ToList();
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(0, groups[0][0]);
    }

    [TestMethod]
    public void AsGroups_SizeOne_IgnoreSingletonsTrue_ReturnsEmpty()
    {
        var dsu = new DisjointSetUnion(1);
        var groups = dsu.AsGroups(ignoreSingletons: true).ToList();
        Assert.AreEqual(0, groups.Count);
    }

    [TestMethod]
    public void Union_StarTopology_AllInOneGroup()
    {
        var dsu = new DisjointSetUnion(10);
        for (var i = 1; i < 10; i++)
            dsu.Union(0, i); // star: all connected to 0
        var groups = dsu.AsGroups().ToList();
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(10, groups[0].Count);
    }

    [TestMethod]
    public void AsGroups_PathCompression_DoesNotAffectResult()
    {
        var dsu = new DisjointSetUnion(5);
        dsu.Union(0, 1);
        dsu.Union(1, 2);
        dsu.Union(2, 3);
        dsu.Union(3, 4);
        // First call compresses paths
        _ = dsu.AsGroups().ToList();
        // Second call should produce the same result
        var groups = dsu.AsGroups().ToList();
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(5, groups[0].Count,
            "Path compression should not change group composition.");
    }
}
