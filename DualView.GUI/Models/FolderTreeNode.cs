using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DualView.Shared.Models;

namespace DualView.GUI.Models;

public class FolderTreeNode
{
    public delegate Task OnOperateOnNode(FolderTreeNode originatingNode, FolderTreeNode currentTreeNode);

    public FolderTreeNode(string name, long serverId, ObservableCollection<FolderTreeNode>? nodes = null)
    {
        Name = name;
        SubNodes = nodes ?? new ObservableCollection<FolderTreeNode>();
        ServerId = serverId;

        // Automatically set subnode parent references
        if (SubNodes.Count > 0)
        {
            foreach (var subNode in SubNodes)
            {
                subNode.Parent = this;
            }
        }
    }

    public ObservableCollection<FolderTreeNode> SubNodes { get; }

    public string Name { get; }

    public long ServerId { get; set; }

    public FolderTreeNode? Parent { get; internal set; }

    /// <summary>
    ///   Optional operation handler looked up from this node up through parents.
    /// </summary>
    public OnOperateOnNode? OnCopyPath { get; set; }

    public bool HasCopyPathAction
    {
        get
        {
            var current = this;
            while (current != null)
            {
                if (current.OnCopyPath != null)
                    return true;
                current = current.Parent;
            }

            return false;
        }
    }

    public void UpdateNameIfRequired(string newName)
    {
        if (newName != Name)
        {
            // TODO: allow updating item names in the tree
        }
    }

    /// <summary>
    ///   Gets a path to a node
    /// </summary>
    /// <param name="tree">All nodes (needed to find the structure)</param>
    /// <param name="targetNode">Node to get the path to</param>
    /// <returns>Null if the node is not found, otherwise the path with '/' separators</returns>
    public static string? GetPath(IEnumerable<FolderTreeNode> tree, FolderTreeNode targetNode)
    {
        foreach (var folderTreeNode in tree)
        {
            // Assume server IDs are strong identity
            if (folderTreeNode.ServerId == targetNode.ServerId)
            {
                return folderTreeNode.Name;
            }

            if (folderTreeNode.SubNodes.Count > 0)
            {
                // Se if it is a child in this tree
                var child = GetPath(folderTreeNode.SubNodes, targetNode);

                if (child != null)
                    return $"{folderTreeNode.Name}/{child}";
            }
        }

        // Not found
        return null;
    }

    public void TriggerCopyPath()
    {
        FolderTreeNode? current = this;

        while (current != null)
        {
            if (current.OnCopyPath != null)
            {
                Task.Run(async () => await current.OnCopyPath(this, current));
                return;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("No copy path handler found");
    }
}

public static class FolderTreeBuilder
{
    public static void HandleTree<TNode, TData>(ObservableCollection<FolderTreeNode> tree,
        List<IGrouping<long?, TData>> newData, Func<string, long, TNode> factory)
        where TNode : FolderTreeNode
        where TData : IFolderInfo
    {
        Dictionary<long, FolderTreeNode> folderMap = new();
        List<TData>? pendingItems = null;

        foreach (var group in newData)
        {
            var parentId = group.Key;

            ObservableCollection<FolderTreeNode> targetCollection;
            FolderTreeNode? newNodeParent;

            if (parentId == null || parentId == -1)
            {
                targetCollection = tree;
                newNodeParent = null;
            }
            else if (folderMap.TryGetValue(parentId.Value, out var parentNode))
            {
                targetCollection = parentNode.SubNodes;
                newNodeParent = parentNode;
            }
            else
            {
                // We are seeing items out of order, so we must delay some of them until later
                pendingItems ??= new List<TData>();

                foreach (var data in group.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    pendingItems.Add(data);
                }

                continue;
            }

            // Sort here to make nice order in the GUI
            foreach (var data in group.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var node = SyncNodeInCollection(targetCollection, data, factory, newNodeParent);
                folderMap[data.Id] = node;
            }
        }

        if (pendingItems != null)
        {
            int maxAttempts = 50;

            // Handle pending items until done
            while (true)
            {
                if (--maxAttempts <= 0)
                    throw new Exception("Tree item parents are too out of order in nesting, cannot resolve tree");

                int count = pendingItems.Count;
                for (int i = 0; i < count; ++i)
                {
                    var item = pendingItems[i];

                    var parentId = item.ParentId;

                    if (parentId == null)
                        throw new Exception("Tree item that has no parent shouldn't get here");

                    if (!folderMap.TryGetValue(parentId.Value, out var parentNode))
                        continue;

                    var node = SyncNodeInCollection(parentNode.SubNodes, item, factory, parentNode);
                    folderMap[item.Id] = node;

                    pendingItems.RemoveAt(i);
                    --i;
                    --count;
                }

                if (pendingItems.Count < 1)
                    break;
            }
        }

        PruneDeletedTreeItems(folderMap, tree);
    }

    public static void PruneDeletedTreeItems<TNode, T2>(Dictionary<long, T2> validFolderIds,
        ObservableCollection<TNode> tree)
        where TNode : FolderTreeNode
    {
        while (true)
        {
            bool didSomething = false;

            // A simple loop that requires a restart if it deletes something as deletes should be relatively rare
            foreach (var treeNode in tree)
            {
                if (!validFolderIds.ContainsKey(treeNode.ServerId))
                {
                    tree.Remove(treeNode);
                    didSomething = true;
                    break;
                }
            }

            if (!didSomething)
            {
                // Time to recurse
                foreach (var subNode in tree)
                {
                    PruneDeletedTreeItems(validFolderIds, subNode.SubNodes);
                }

                break;
            }
        }
    }

    private static TNode SyncNodeInCollection<TNode, TData>(ObservableCollection<TNode> collection, TData newData,
        Func<string, long, TNode> factory, FolderTreeNode? parentNode = null)
        where TNode : FolderTreeNode
        where TData : IFolderInfo
    {
        foreach (var existingNode in collection)
        {
            if (existingNode.ServerId == newData.Id)
            {
                existingNode.UpdateNameIfRequired(newData.Name);
                return existingNode;
            }
        }

        // New node creation
        var newNode = factory(newData.Name, newData.Id);

        // Find insertion index to maintain alphabetical order

        int insertionIndex = 0;
        while (insertionIndex < collection.Count &&
               string.Compare(collection[insertionIndex].Name, newData.Name,
                   StringComparison.CurrentCultureIgnoreCase) < 0)
        {
            ++insertionIndex;
        }

        collection.Insert(insertionIndex, newNode);

        // Keep parent link up to date
        newNode.Parent = parentNode;
        return newNode;
    }
}
