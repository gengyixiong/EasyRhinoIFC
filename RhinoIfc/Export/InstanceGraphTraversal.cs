using System;
using System.Collections.Generic;

namespace RhinoIfc.Export
{
    /// <summary>
    /// Traverses an instance-definition graph while accumulating transforms.
    /// The generic core is independent of Rhino so nesting, repeated definitions,
    /// and cycle protection can be covered by the console test suite.
    /// </summary>
    internal static class InstanceGraphTraversal
    {
        public static void Traverse<TNode, TTransform, TKey>(
            TNode root,
            TTransform identity,
            Func<TNode, bool> isInstance,
            Func<TNode, TKey> getDefinitionKey,
            Func<TNode, IEnumerable<TNode>> getChildren,
            Func<TNode, TTransform> getInstanceTransform,
            Func<TTransform, TTransform, TTransform> combineTransforms,
            Action<TNode, TTransform> visitLeaf)
        {
            if (isInstance == null) throw new ArgumentNullException(nameof(isInstance));
            if (getDefinitionKey == null) throw new ArgumentNullException(nameof(getDefinitionKey));
            if (getChildren == null) throw new ArgumentNullException(nameof(getChildren));
            if (getInstanceTransform == null) throw new ArgumentNullException(nameof(getInstanceTransform));
            if (combineTransforms == null) throw new ArgumentNullException(nameof(combineTransforms));
            if (visitLeaf == null) throw new ArgumentNullException(nameof(visitLeaf));

            TraverseRecursive(
                root,
                identity,
                new HashSet<TKey>(),
                isInstance,
                getDefinitionKey,
                getChildren,
                getInstanceTransform,
                combineTransforms,
                visitLeaf);
        }

        private static void TraverseRecursive<TNode, TTransform, TKey>(
            TNode node,
            TTransform accumulatedTransform,
            HashSet<TKey> activeDefinitions,
            Func<TNode, bool> isInstance,
            Func<TNode, TKey> getDefinitionKey,
            Func<TNode, IEnumerable<TNode>> getChildren,
            Func<TNode, TTransform> getInstanceTransform,
            Func<TTransform, TTransform, TTransform> combineTransforms,
            Action<TNode, TTransform> visitLeaf)
        {
            if (ReferenceEquals(node, null)) return;

            if (!isInstance(node))
            {
                visitLeaf(node, accumulatedTransform);
                return;
            }

            var children = getChildren(node);
            if (children == null) return;

            var definitionKey = getDefinitionKey(node);
            if (!activeDefinitions.Add(definitionKey)) return;

            try
            {
                var combinedTransform = combineTransforms(
                    accumulatedTransform,
                    getInstanceTransform(node));

                foreach (var child in children)
                {
                    TraverseRecursive(
                        child,
                        combinedTransform,
                        activeDefinitions,
                        isInstance,
                        getDefinitionKey,
                        getChildren,
                        getInstanceTransform,
                        combineTransforms,
                        visitLeaf);
                }
            }
            finally
            {
                activeDefinitions.Remove(definitionKey);
            }
        }
    }
}
