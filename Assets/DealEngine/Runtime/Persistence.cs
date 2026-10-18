using System.IO;
using System.Text;

namespace DealEngine
{
    /// <summary>The engine does not know how to write JSON. The host (Unity, a server, a test) supplies this.</summary>
    public interface IJsonSerializer
    {
        string ToJson<T>(T obj);
        T FromJson<T>(string json);
    }

    public static class SafeFile
    {
        /// <summary>
        /// Writes to a temp file, flushes it to disk, then replaces the real file.
        /// A crash at any point leaves either the old file or the new file, never a half-written one.
        /// </summary>
        public static void WriteAtomic(string path, string text)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temp = path + ".tmp";
            byte[] data = new UTF8Encoding(false).GetBytes(text);

            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data, 0, data.Length);
                stream.Flush(true); // force it onto the disk, not just the OS cache
            }

            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}