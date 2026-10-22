using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace ClaudeAutoPass
{
    /// <summary>
    /// Loads the libraries the build deflated into the exe ("lib/&lt;name&gt;.dll"), so the release stays a single file.
    /// Register before any code that touches those libraries is compiled by the JIT.
    /// </summary>
    static class EmbeddedAssemblies
    {
        static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        public static void Register() => AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            lock (Loaded)
            {
                if (Loaded.TryGetValue(name, out Assembly cached)) return cached;
                using (Stream stream = typeof(EmbeddedAssemblies).Assembly.GetManifestResourceStream("lib/" + name + ".dll"))
                {
                    if (stream == null) return null;
                    using (var deflate = new DeflateStream(stream, CompressionMode.Decompress))
                    using (var bytes = new MemoryStream())
                    {
                        deflate.CopyTo(bytes);
                        Assembly assembly = Assembly.Load(bytes.ToArray());
                        Loaded[name] = assembly;
                        return assembly;
                    }
                }
            }
        }
    }
}
