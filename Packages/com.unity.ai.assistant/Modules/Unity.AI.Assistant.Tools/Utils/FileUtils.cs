using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Tools.Editor
{
    internal static class FileUtils
    {
        /// <summary>
        /// Recursively builds or traverses folder tree structure to find the target folder
        /// for a given path. Creates folders as needed.
        /// </summary>
        public static AssetTools.AssetFolder GetOrCreateFolder(
            AssetTools.AssetFolder root,
            string[] pathParts,
            int depth,
            string fullPath)
        {
            bool isFolder = AssetDatabase.IsValidFolder(fullPath);

            // Determine the final index of folder segments
            int maxFolderIndex = isFolder ? pathParts.Length - 1 : pathParts.Length - 2;

            // Stop when all folder segments has been processed
            if (depth > maxFolderIndex)
                return root;

            var folderName = pathParts[depth];

            var child = root.Children.FirstOrDefault(f => f.Name == folderName);
            if (child == null)
            {
                child = new AssetTools.AssetFolder { Name = folderName };
                root.Children.Add(child);
            }

            return GetOrCreateFolder(child, pathParts, depth + 1, fullPath);
        }
    }
}
