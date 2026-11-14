using System;
using System.IO;
using System.Text;
using System.Threading;

namespace ZapretControl
{
    // Shared atomic JSON write: unique temporary file in the target directory,
    // CreateNew against pre-planted links, replace/move with retries and no
    // non-atomic Copy fallback.
    public static class AtomicFile
    {
        public static bool WriteAllText(string path, string text)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }
            string temporary = null;
            try
            {
                AppPaths.EnsureDirectory(directory);
                if (AppPaths.IsReparsePoint(directory))
                {
                    return false;
                }
                temporary = Path.Combine(
                    directory,
                    Path.GetFileName(path) + "." +
                    Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream stream = new FileStream(
                    temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (StreamWriter writer = new StreamWriter(
                        stream, new UTF8Encoding(false)))
                    {
                        writer.Write(text);
                        writer.Flush();
                        stream.Flush(true);
                    }
                }
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            if (AppPaths.IsReparsePoint(path))
                            {
                                return false;
                            }
                            File.Replace(temporary, path, null);
                        }
                        else
                        {
                            File.Move(temporary, path);
                        }
                        temporary = null;
                        return true;
                    }
                    catch (IOException)
                    {
                        if (attempt == 2)
                        {
                            return false;
                        }
                        Thread.Sleep(50);
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporary))
                {
                    try
                    {
                        if (File.Exists(temporary))
                        {
                            File.Delete(temporary);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
    }
}
