using System;
using System.Collections.Generic;
using System.Linq;

public static class SimpleYAML
{
    static string delimiter = "=";
    static string indent = "";

    public static string GetValue(string yaml, string key)
    {
        var lines = yaml.Split('\n');
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith(key + delimiter))
            {
                return trimmedLine.Substring(key.Length + 1).Trim();
            }
        }
        return null;
    }

    public static string SetOrUpdateTag(string yaml, string section, string tagName, string newTag)
    {
        var lines = yaml.Split(new[] { '\r', '\n' }).ToList();
        var newLines = new List<string>();

        string sectionHeader = $"[{section}]";
        string tagLinePrefix = $"{tagName}{delimiter}";

        // Find the section header index
        int sectionIndex = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim() == sectionHeader)
            {
                sectionIndex = i;
                break;
            }
        }

        // If section doesn't exist, add it
        if (sectionIndex == -1)
        {
            lines.Add(sectionHeader);
            lines.Add($"{indent}{tagLinePrefix}{newTag}");
            return string.Join("\n", lines);
        }

        // Section exists, find the tags line
        int tagsIndex = -1;
        for (int i = sectionIndex + 1; i < lines.Count; i++)
        {
            // Stop if we hit another section
            if (lines[i].Trim().StartsWith("["))
            {
                break;
            }
            if (lines[i].Trim().StartsWith(tagLinePrefix))
            {
                tagsIndex = i;
                break;
            }
        }

        // If tags line doesn't exist, add it under the section
        if (tagsIndex == -1)
        {
            lines.Insert(sectionIndex + 1, $"{indent}{tagLinePrefix}{newTag}");
            return string.Join("\n", lines);
        }

        // Tags line exists, append the new tag if it's not already there
        var existingTags = lines[tagsIndex].Substring(lines[tagsIndex].IndexOf(delimiter) + 1).Trim();
        var tagList = existingTags.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        if (!tagList.Contains(newTag))
        {
            tagList.Add(newTag);
            lines[tagsIndex] = $"{indent}{tagLinePrefix}{string.Join(", ", tagList)}";
        }

        return string.Join("\n", lines);
    }
}