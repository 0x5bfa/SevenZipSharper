using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public sealed class GenerateSevenZipInputs : Task
{
    private const string GeneratedComment = "// Generated from SevenZipSharper.Interop/external/7zip/CPP/7zip/Guid.txt. Do not edit by hand.";
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    [Required] public string CppRoot { get; set; }
    [Required] public string NamespaceMapPath { get; set; }
    [Required] public string PartitionsRoot { get; set; }
    [Required] public string ScraperHeaderPath { get; set; }
    [Required] public string NamespaceResponsePath { get; set; }
    [Required] public string InterfaceGuidsResponsePath { get; set; }
    [Output] public ITaskItem[] Partitions { get; set; }

    public override bool Execute()
    {
        try
        {
            Generate();
            return true;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception);
            return false;
        }
    }

    private void Generate()
    {
        string cppRoot = Path.GetFullPath(CppRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string headersRoot = Path.Combine(cppRoot, "7zip");
        string guidPath = Path.Combine(headersRoot, "Guid.txt");
        if (!File.Exists(guidPath))
        {
            throw new FileNotFoundException("7-Zip submodule is missing. Run 'git submodule update --init --recursive' first.", guidPath);
        }

        Dictionary<string, Dictionary<string, string>> namespaceMap;
        var serializer = new DataContractJsonSerializer(
            typeof(Dictionary<string, Dictionary<string, string>>),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using (var stream = File.OpenRead(NamespaceMapPath))
        {
            namespaceMap = (Dictionary<string, Dictionary<string, string>>)serializer.ReadObject(stream);
        }

        string catalog = File.ReadAllText(guidPath);
        int handlersStart = catalog.IndexOf("Handler GUIDs:", StringComparison.Ordinal);
        if (handlersStart >= 0)
        {
            catalog = catalog.Substring(0, handlersStart);
        }

        var publicHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var internalHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(catalog, @"(?im)^\s*(?://\s*)?[0-9A-F]{2}\s+(?<header>[A-Za-z0-9_./\\-]+\.h)::(?<interface>[A-Za-z0-9_]+)"))
        {
            string header = HeaderName(match.Groups["header"].Value);
            if (match.Groups["interface"].Value.EndsWith("Internal", StringComparison.Ordinal))
            {
                internalHeaders.Add(header);
            }
            else
            {
                publicHeaders.Add(header);
            }
        }

        var catalogHeaders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(catalog, @"(?i)(?<header>[A-Za-z0-9_./\\-]+\.h)"))
        {
            string header = HeaderName(match.Groups["header"].Value);
            if (!internalHeaders.Contains(header) || publicHeaders.Contains(header))
            {
                catalogHeaders.Add(header);
            }
        }
        // Mapped headers can contribute constants even if Guid.txt defines no interface in them.
        foreach (string header in namespaceMap["headers"].Keys)
        {
            catalogHeaders.Add(HeaderName(header));
        }

        if (catalogHeaders.Count == 0)
        {
            throw new InvalidDataException("No interface headers were found in '" + guidPath + "'.");
        }

        var excludedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "UI/Agent/IFolderArchive.h",
            "UI/Common/IFileExtractCallback.h",
            "UI/FileManager/PluginInterface.h"
        };
        var headerFiles = Directory.GetFiles(headersRoot, "*.h", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToLookup(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        var partitions = new List<ITaskItem>();
        var imports = new SortedSet<string>(StringComparer.Ordinal);
        var partitionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string headerName in catalogHeaders)
        {
            if (!headerFiles.Contains(headerName))
            {
                throw new FileNotFoundException("Guid.txt references a header that was not found under '" + headersRoot + "': " + headerName);
            }
            foreach (string headerPath in headerFiles[headerName])
            {
                string relativeHeader = headerPath.Substring(headersRoot.Length + 1).Replace('\\', '/');
                if (excludedHeaders.Contains(relativeHeader))
                {
                    continue;
                }
                string headerNamespace;
                if (!namespaceMap["headers"].TryGetValue(relativeHeader, out headerNamespace))
                {
                    throw new InvalidDataException("Assign a functional namespace to '" + relativeHeader + "' in '" + NamespaceMapPath + "'.");
                }

                string partitionName = Regex.Replace(relativeHeader, "[^A-Za-z0-9_]", "_");
                if (!partitionNames.Add(partitionName))
                {
                    throw new InvalidDataException("Duplicate generated partition name: " + partitionName);
                }
                string partitionDirectory = Path.Combine(PartitionsRoot, partitionName);
                string relativePath = "7zip/" + relativeHeader;
                string mainPath = Path.Combine(partitionDirectory, "main.cpp");
                WriteIfChanged(Path.Combine(partitionDirectory, "settings.rsp"),
                    "--traverse\r\n<IncludeRoot>/" + relativePath + "\r\n--namespace\r\n" + headerNamespace + "\r\n");
                WriteIfChanged(mainPath, GeneratedComment + "\r\n#include \"" + relativePath.Replace('/', '\\') + "\"\r\n");

                var partition = new TaskItem(mainPath);
                partition.SetMetadata("Name", partitionName);
                partition.SetMetadata("TraverseFiles", headerPath);
                partitions.Add(partition);
                imports.Add("using " + headerNamespace + ";");
            }
        }

        WriteIfChanged(ScraperHeaderPath, GeneratedComment + "\r\n" + string.Join("\r\n", imports) + "\r\n");
        WriteIfChanged(NamespaceResponsePath, "--requiredNamespaceForName\r\n" + string.Join("\r\n",
            namespaceMap["types"].OrderBy(mapping => mapping.Key, StringComparer.Ordinal)
                .Select(mapping => mapping.Key + "=" + mapping.Value)) + "\r\n");
        WriteIfChanged(InterfaceGuidsResponsePath, CreateInterfaceGuidsResponse(catalog));
        Partitions = partitions.ToArray();
        Log.LogMessage(MessageImportance.High, "Prepared {0} header partitions from Guid.txt and the namespace map.", Partitions.Length);
    }

    private static string CreateInterfaceGuidsResponse(string catalog)
    {
        var interfaceGuids = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguousInterfaces = new HashSet<string>(StringComparer.Ordinal);
        string guidFamily = null;

        foreach (string line in catalog.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Regex.IsMatch(line, @"^\s*//"))
            {
                continue;
            }

            Match headerMatch = Regex.Match(line, @"^\s*(?<family>[0-9A-F]{2})\s+[A-Za-z0-9_./\\-]+\.h\s*$", RegexOptions.IgnoreCase);
            if (headerMatch.Success)
            {
                guidFamily = headerMatch.Groups["family"].Value.ToUpperInvariant();

                continue;
            }

            Match interfaceMatch = Regex.Match(line, @"^\s*(?<id>[0-9A-F]{2})\s+(?:[A-Za-z0-9_./\\-]+\.h::)?(?<name>[A-Za-z0-9_]+)(?:\s|$)", RegexOptions.IgnoreCase);
            if (!interfaceMatch.Success || guidFamily is null)
            {
                continue;
            }

            string name = interfaceMatch.Groups["name"].Value;
            string id = interfaceMatch.Groups["id"].Value.ToUpperInvariant();
            string guid = "23170F69-40C1-278A-0000-00" + guidFamily + "00" + id + "0000";
            if (ambiguousInterfaces.Contains(name))
            {
                continue;
            }

            if (interfaceGuids.TryGetValue(name, out string existingGuid) && !string.Equals(existingGuid, guid, StringComparison.OrdinalIgnoreCase))
            {
                interfaceGuids.Remove(name);
                ambiguousInterfaces.Add(name);

                continue;
            }

            interfaceGuids[name] = guid;
        }

        if (interfaceGuids.Count == 0)
        {
            throw new InvalidDataException("No unambiguous interface GUIDs were found in Guid.txt.");
        }

        return string.Join("\r\n", interfaceGuids.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => "--with-attribute\r\n" + pair.Key + "=" + FormatGuidAttribute(pair.Value))) + "\r\n";
    }

    private static string FormatGuidAttribute(string value)
    {
        string[] parts = Guid.Parse(value).ToString("D").ToUpperInvariant().Split('-');
        string remainingBytes = parts[3] + parts[4];
        var arguments = new List<string>
        {
            "0x" + parts[0] + "u",
            "0x" + parts[1],
            "0x" + parts[2]
        };

        for (int index = 0; index < remainingBytes.Length; index += 2)
        {
            arguments.Add("0x" + remainingBytes.Substring(index, 2));
        }

        return "global::Windows.Win32.Foundation.Metadata.GuidAttribute(" + string.Join(", ", arguments) + ")";
    }

    private static string HeaderName(string path)
    {
        return Path.GetFileName(path.Replace('\\', '/'));
    }

    private static void WriteIfChanged(string path, string content)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, content, Utf8);
        }
    }
}
