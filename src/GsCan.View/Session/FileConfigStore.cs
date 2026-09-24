using System;
using System.IO;
using System.Text.Json;

namespace GsCan.View.Session
{
    /// <summary>
    /// JSON file store for the remembered form. Default location is
    /// <c>%LocalAppData%\GsCan.View\session.json</c> so a zip-distributed
    /// copy stays writable and the form survives replacing the unzipped folder.
    /// </summary>
    public sealed class FileConfigStore : IConfigStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _path;

        public FileConfigStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public static FileConfigStore InLocalAppData()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GsCan.View");
            return new FileConfigStore(Path.Combine(dir, "session.json"));
        }

        public ViewConfig? Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return null;
                }

                var json = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<ViewConfig>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        public void Save(ViewConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(_path, JsonSerializer.Serialize(config, JsonOptions));
            }
            catch
            {
                // Remembering the form is best-effort; do not crash the session.
            }
        }
    }
}
