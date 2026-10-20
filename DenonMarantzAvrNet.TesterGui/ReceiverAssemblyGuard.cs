using DenonMarantzAvrNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DenonMarantzAvrNet.TesterGui
{
    /// <summary>
    /// Checks the library actually loaded by the CLR BEFORE any GUI code calls recently added members.
    /// Avoids diagnosing a mismatched DLL as a receiver or network failure.
    /// </summary>
    internal static class ReceiverAssemblyGuard
    {
        internal static string? Validate()
        {
            Assembly library = typeof(DenonAvrClient).Assembly;
            Type clientType = typeof(DenonAvrClient);
            var missing = new List<string>();

            RequireNullableIntGetter(clientType, "AppCommandPort", missing);
            RequireNullableIntGetter(clientType, "AppCommand0300Port", missing);

            if (missing.Count == 0)
                return null;

            string path;
            try
            {
                path = string.IsNullOrEmpty(library.Location)
                    ? "(In-memory/embedded assembly; no file path)"
                    : library.Location;
            }
            catch (NotSupportedException)
            {
                path = "(No file path available)";
            }

            string versions = string.Join(Environment.NewLine,
                AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => string.Equals(a.GetName().Name,
                        "DenonMarantzAvrNet", StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.FullName));

            return "Die WPF-Test-GUI hat eine unpassende DenonMarantzAvrNet-Bibliothek geladen."
                + Environment.NewLine + Environment.NewLine
                + "Fehlende/falsche API: " + string.Join(", ", missing)
                + Environment.NewLine + "Geladene Assembly: " + library.FullName
                + Environment.NewLine + "Geladener Pfad: " + path
                + Environment.NewLine + "Assemblies im AppDomain: " + versions
                + Environment.NewLine + Environment.NewLine
                + "Die EXE und DenonMarantzAvrNet.dll muessen aus DEMSELBEN Build stammen. "
                + "In diesem Projekt wird die Bibliothek bewusst nicht mehr in die EXE eingebettet. "
                + "Pruefe den hier ausgegebenen DLL-Pfad und starte die EXE aus dem frisch gebauten "
                + "bin\\Debug- bzw. bin\\Release-Verzeichnis.";
        }

        private static void RequireNullableIntGetter(Type type, string name, ICollection<string> missing)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(int?) ||
                property.GetGetMethod() == null)
            {
                missing.Add("DenonAvrClient.get_" + name + "(): Nullable<Int32>");
            }
        }
    }
}
