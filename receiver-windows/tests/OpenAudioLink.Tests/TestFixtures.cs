using System;
using System.IO;

namespace OpenAudioLink.Tests
{
    internal static class TestFixtures
    {
        public static byte[] Read(string relativePath)
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                string path = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Fixture not found.", relativePath);
        }
    }
}
