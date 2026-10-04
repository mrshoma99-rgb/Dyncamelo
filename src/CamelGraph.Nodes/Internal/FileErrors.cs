using System;
using System.IO;
using System.Security;

namespace CamelGraph.Nodes;

/// <summary>The file-type lists the browse buttons of the file nodes offer (Windows file-dialog filter syntax).</summary>
internal static class FileFilters
{
    internal const string All = "All files (*.*)|*.*";

    internal const string Text = "Text files (*.txt)|*.txt|All files (*.*)|*.*";

    internal const string Csv = "CSV files (*.csv)|*.csv|Tab-separated files (*.tsv;*.txt)|*.tsv;*.txt|All files (*.*)|*.*";

    internal const string Json = "JSON files (*.json)|*.json|All files (*.*)|*.*";

    internal const string Excel = "Excel workbooks (*.xlsx)|*.xlsx";

    internal const string Zip = "Zip archives (*.zip)|*.zip|All files (*.*)|*.*";

    internal const string Log = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*";

    internal const string Xml = "XML files (*.xml)|*.xml|All files (*.*)|*.*";

    internal const string Programs = "Programs (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*";
}

/// <summary>
/// Turns the failures a person meets when a file cannot be read or written (a locked file, no permission, a path Windows does not
/// accept) into sentences that name the node and the file and say what to do, instead of the framework's own text.
/// </summary>
internal static class FileErrors
{
    /// <summary>Runs a file action and rewords the failures of the file system.</summary>
    /// <param name="nodeName">The node as the user knows it ("Text.WriteToFile").</param>
    /// <param name="path">The file or folder the action works on, as it is shown in the message.</param>
    /// <param name="writing">True when the action writes, so "read" becomes "write" in the sentences.</param>
    /// <param name="action">The work.</param>
    internal static T Run<T>(string nodeName, string path, bool writing, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (IsFileProblem(ex))
        {
            throw Friendly(ex, nodeName, path, writing);
        }
    }

    /// <summary>Runs a file action that returns nothing and rewords the failures of the file system.</summary>
    /// <param name="nodeName">The node as the user knows it.</param>
    /// <param name="path">The file or folder the action works on.</param>
    /// <param name="writing">True when the action writes.</param>
    /// <param name="action">The work.</param>
    internal static void Run(string nodeName, string path, bool writing, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (IsFileProblem(ex))
        {
            throw Friendly(ex, nodeName, path, writing);
        }
    }

    /// <summary>True for the exceptions <see cref="Friendly"/> knows how to reword.</summary>
    internal static bool IsFileProblem(Exception ex)
    {
        // ArgumentException is left out on purpose: a node's own complaints about its inputs (an unknown sheet, a bad sheet name) are
        // already sentences, and the paths themselves are checked up front by FileNodes.ResolveChecked.
        return ex is UnauthorizedAccessException || ex is IOException || ex is SecurityException || ex is NotSupportedException;
    }

    /// <summary>The sentence for a path the operating system cannot use.</summary>
    internal static string NotAPath(string nodeName, string path)
    {
        return nodeName + ": '" + path + "' is not a path Windows accepts. Check for characters such as < > | \" ? * and for a colon anywhere but after the drive letter.";
    }

    /// <summary>The same problem as a sentence for a person; file-not-found and folder-not-found keep their type.</summary>
    internal static Exception Friendly(Exception ex, string nodeName, string path, bool writing)
    {
        var verb = writing ? "write" : "read";
        if (ex is FileNotFoundException)
        {
            return new FileNotFoundException(nodeName + ": the file '" + path + "' does not exist.", path, ex);
        }

        if (ex is DirectoryNotFoundException)
        {
            return new DirectoryNotFoundException(nodeName + ": the folder of '" + path + "' does not exist.", ex);
        }

        if (ex is PathTooLongException)
        {
            return new IOException(nodeName + ": the path '" + path + "' is too long for Windows. Use a shorter folder or file name.", ex);
        }

        if (ex is UnauthorizedAccessException || ex is SecurityException)
        {
            return new IOException(
                nodeName + ": access to '" + path + "' was denied. Check that it is not read-only or open in another program, that it is not a folder, and that you may " +
                verb + " there.",
                ex);
        }

        if (ex is NotSupportedException || ex is ArgumentException)
        {
            return new ArgumentException(NotAPath(nodeName, path), "path", ex);
        }

        // Windows sharing / lock violations and a full disk have a stable code in the low 16 bits of the HResult.
        var code = ex.HResult & 0xFFFF;
        if (code == 32 || code == 33)
        {
            return new IOException(nodeName + ": '" + path + "' is in use by another program. Close it there (or wait until it is done), then run again.", ex);
        }

        if (code == 39 || code == 112)
        {
            return new IOException(nodeName + ": there is no room left on the disk to " + verb + " '" + path + "'. Free some space or choose another drive.", ex);
        }

        return new IOException(nodeName + ": '" + path + "' could not be " + (writing ? "written" : "read") + ". " + ex.Message, ex);
    }
}
