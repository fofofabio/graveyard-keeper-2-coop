using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;

namespace GK2Coop
{
    /// <summary>
    /// Lets a Steam Workshop subscription keep the installed mod current.
    ///
    /// The game's own mod system loads only language and voice-over packs, so Steam downloads a
    /// Workshop item to <c>steamapps\workshop\content\4358690\&lt;id&gt;</c> and nothing runs it.
    /// Every BepInEx mod on that Workshop asks the player to copy its folder into the game once.
    /// That first copy is unavoidable; after it, this closes the gap. At startup it compares the
    /// Workshop copy with the running plugin and, when the Workshop copy is newer, copies it into
    /// this plugin's folder for the next launch. Without it Steam silently updates a folder
    /// nobody reads, and two players who both "have the latest version" refuse each other's
    /// handshake.
    ///
    /// Only one Workshop item is trusted: the pinned <see cref="PublishedItemId"/>, which only its
    /// owner can update. Anyone can publish an item containing a file named GK2Coop.dll, so
    /// scanning every subscribed item for one would let any subscription replace this code.
    /// With no id pinned the updater does nothing.
    ///
    /// Only files inside this plugin's own folder are written, never BepInEx itself or the
    /// player's config. The running DLL is locked by Mono but may be renamed, so each replaced
    /// file is moved aside to <c>.old</c> and removed on the next start.
    /// </summary>
    internal static class CoopWorkshopUpdater
    {
        /// <summary>Steam app id of Graveyard Keeper 2.</summary>
        internal const string AppId = "4358690";

        /// <summary>The published Workshop item id, set once the mod has been uploaded. Zero disables updating.</summary>
        internal const ulong PublishedItemId = 0;

        private const string OldSuffix = ".old";

        private static ManualLogSource log;

        /// <summary>A line for the main menu when something was installed or went wrong; empty otherwise.</summary>
        private static string noticeText;
        private static string noticeValue;

        /// <summary>What the main menu tells the player about an update, in the game's language.</summary>
        internal static string Notice => noticeText == null ? string.Empty : L.F(noticeText, noticeValue);

        internal static void Run(ManualLogSource source, bool enabled, ulong itemIdOverride, string folderOverride)
        {
            log = source;
            string pluginFolder = Path.GetDirectoryName(typeof(CoopWorkshopUpdater).Assembly.Location);
            if (!string.Equals(Path.GetFileName(pluginFolder), "GK2Coop", StringComparison.OrdinalIgnoreCase))
            {
                // Installed loose in plugins\: that folder belongs to every mod, so neither the
                // leftover sweep nor a copy into it is safe.
                log.LogInfo("Workshop update: the plugin is not in its own GK2Coop folder; updating is off.");
                return;
            }
            RemoveLeftovers(pluginFolder);
            if (!enabled)
            {
                return;
            }

            string sourceFolder = ResolveSourceFolder(itemIdOverride, folderOverride);
            if (sourceFolder == null)
            {
                return;
            }
            string sourceDll = Path.Combine(sourceFolder, "GK2Coop.dll");
            if (!File.Exists(sourceDll))
            {
                log.LogInfo("Workshop update: no GK2Coop.dll at " + sourceFolder + "; nothing to compare.");
                return;
            }

            Version running = typeof(CoopWorkshopUpdater).Assembly.GetName().Version;
            Version available;
            try
            {
                // Reads the manifest only; the assembly is not loaded into this domain.
                available = AssemblyName.GetAssemblyName(sourceDll).Version;
            }
            catch (Exception ex)
            {
                log.LogWarning("Workshop update: could not read the Workshop copy's version: " + ex.Message);
                return;
            }
            if (available <= running)
            {
                log.LogInfo($"Workshop update: running {Short(running)}, Workshop has {Short(available)}; up to date.");
                return;
            }

            try
            {
                int copied = CopyTree(sourceFolder, pluginFolder);
                noticeText = L.Key("Co-op mod {0} was installed from the Workshop. Restart the game to use it.");
                noticeValue = Short(available);
                log.LogInfo($"Workshop update: installed {Short(available)} over {Short(running)} ({copied} file(s)); active from the next launch.");
            }
            catch (Exception ex)
            {
                noticeText = L.Key("Could not install the co-op mod update from the Workshop: {0}");
                noticeValue = ex.Message;
                log.LogWarning("Workshop update failed; restored the previous files where possible: " + ex);
            }
        }

        private static string ResolveSourceFolder(ulong itemIdOverride, string folderOverride)
        {
            if (!string.IsNullOrWhiteSpace(folderOverride))
            {
                return Path.GetFullPath(folderOverride.Trim());
            }
            ulong id = itemIdOverride != 0 ? itemIdOverride : PublishedItemId;
            if (id == 0)
            {
                log.LogInfo("Workshop update: no Workshop item is pinned yet; skipping.");
                return null;
            }
            // Steam keeps Workshop content in the same library as the game:
            // <library>\steamapps\common\<game> and <library>\steamapps\workshop\content\<app>\<id>.
            string steamapps = Directory.GetParent(Paths.GameRootPath)?.Parent?.FullName;
            if (steamapps == null)
            {
                return null;
            }
            return Path.Combine(steamapps, "workshop", "content", AppId, id.ToString(), "BepInEx", "plugins", "GK2Coop");
        }

        /// <summary>
        /// Copies every file, moving each existing target aside first. If any step fails, the files
        /// already replaced are put back so the install is never left half old, half new.
        /// </summary>
        private static int CopyTree(string sourceFolder, string targetFolder)
        {
            string[] files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
            var replaced = new System.Collections.Generic.List<string>();
            var created = new System.Collections.Generic.List<string>();
            try
            {
                foreach (string file in files)
                {
                    string relative = file.Substring(sourceFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (relative.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string target = Path.Combine(targetFolder, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target))
                    {
                        string aside = target + OldSuffix;
                        if (File.Exists(aside))
                        {
                            File.Delete(aside);
                        }
                        File.Move(target, aside);
                        replaced.Add(target);
                    }
                    else
                    {
                        created.Add(target);
                    }
                    File.Copy(file, target, false);
                    if (!SameContent(file, target))
                    {
                        throw new IOException("The copy of " + relative + " does not match the Workshop file.");
                    }
                }
                return files.Length;
            }
            catch
            {
                foreach (string target in created)
                {
                    TryDelete(target);
                }
                foreach (string target in replaced)
                {
                    TryDelete(target);
                    try { File.Move(target + OldSuffix, target); }
                    catch (Exception ex) { log.LogWarning("Workshop update: could not restore " + target + ": " + ex.Message); }
                }
                throw;
            }
        }

        private static void RemoveLeftovers(string pluginFolder)
        {
            try
            {
                foreach (string file in Directory.GetFiles(pluginFolder, "*" + OldSuffix, SearchOption.AllDirectories))
                {
                    TryDelete(file);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Workshop update: could not tidy previous update leftovers: " + ex.Message);
            }
        }

        private static bool SameContent(string a, string b)
        {
            using (var sha = SHA256.Create())
            {
                byte[] first;
                byte[] second;
                using (FileStream stream = File.OpenRead(a)) { first = sha.ComputeHash(stream); }
                using (FileStream stream = File.OpenRead(b)) { second = sha.ComputeHash(stream); }
                return Convert.ToBase64String(first) == Convert.ToBase64String(second);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Still locked by this process when it is the DLL that was just moved aside; the
                // next start removes it.
            }
        }

        private static string Short(Version version)
        {
            return version == null ? "?" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }
    }
}
