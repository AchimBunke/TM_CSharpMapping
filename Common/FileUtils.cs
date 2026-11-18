using System;
using System.Collections.Generic;
using System.Text;

namespace TM_GenericMapping.Common;

public static class FileUtils
{
    public static string? ResolveRealPath(string? path)
    {
        if(string.IsNullOrWhiteSpace(path))
            return path;
        var full = Path.GetFullPath(path);
        var current = new DirectoryInfo(Path.GetDirectoryName(full)!);
        var tail = Path.GetFileName(full);

        while (current is not null)
        {
            var target = current.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
                return Path.Combine(target.FullName, tail);

            tail = Path.Combine(current.Name, tail);
            current = current.Parent;
        }
        return full;
    }
}
