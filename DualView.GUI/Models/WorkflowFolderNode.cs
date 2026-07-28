using System;
using System.Collections.ObjectModel;

namespace DualView.GUI.Models;

/// <summary>
///   Client side representation of the workflow folders for viewing in a tree
/// </summary>
public class WorkflowFolderNode
{
    public delegate void OnOperateOnNode(WorkflowFolderNode originatingNode, WorkflowFolderNode currentTreeNode);

    public WorkflowFolderNode(string name, bool isFolder, long serverId,
        ObservableCollection<WorkflowFolderNode>? subNodes = null)
    {
        Name = name;
        SubNodes = subNodes;
        IsFolder = isFolder;
        ServerId = serverId;

        // Automatically set subnode parent references
        if (SubNodes != null)
        {
            foreach (var subNode in SubNodes)
            {
                subNode.Parent = this;
            }
        }

        if (!isFolder && subNodes != null)
            throw new ArgumentException("Non-folder items cannot have subnodes");
    }

    public ObservableCollection<WorkflowFolderNode>? SubNodes { get; }

    public string Name { get; }

    public long ServerId { get; set; }

    /// <summary>
    ///   If false, this is a workflow
    /// </summary>
    public bool IsFolder { get; set; }

    public WorkflowFolderNode? Parent { get; private set; }

    public OnOperateOnNode? OnMove { get; set; }
    public OnOperateOnNode? OnDelete { get; set; }
    public OnOperateOnNode? OnRename { get; set; }

    public void UpdateNameIfRequired(string newName)
    {
        if (newName != Name)
        {
            // TODO: allow updating item names in the tree
        }
    }

    /// <summary>
    ///   Safely adds a child while keeping the parent reference updated
    /// </summary>
    /// <param name="node">Node to add</param>
    /// <exception cref="InvalidOperationException">If the node already has a parent or this is not a folder</exception>
    public void AddChild(WorkflowFolderNode node)
    {
        if (SubNodes == null)
            throw new InvalidOperationException("This is not a folder");

        if (node.Parent != null)
            throw new InvalidOperationException("Node already has a parent");

        SubNodes.Add(node);
        node.Parent = this;
    }

    public void TriggerMove()
    {
        WorkflowFolderNode? current = this;

        while (current != null)
        {
            if (current.OnMove != null)
            {
                current.OnMove(this, current);
                return;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("No move handler found");
    }

    public void TriggerDelete()
    {
        WorkflowFolderNode? current = this;

        while (current != null)
        {
            if (current.OnDelete != null)
            {
                current.OnDelete(this, current);
                return;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("No delete handler found");
    }

    public void TriggerRename()
    {
        WorkflowFolderNode? current = this;

        while (current != null)
        {
            if (current.OnRename != null)
            {
                current.OnRename(this, current);
                return;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("No rename handler found");
    }
}
