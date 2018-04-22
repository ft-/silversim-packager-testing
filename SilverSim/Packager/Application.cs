// SilverSim is distributed under the terms of the
// GNU Affero General Public License v3 with
// the following clarification and special exception.

// Linking this library statically or dynamically with other modules is
// making a combined work based on this library. Thus, the terms and
// conditions of the GNU Affero General Public License cover the whole
// combination.

// As a special exception, the copyright holders of this library give you
// permission to link this library with independent modules to produce an
// executable, regardless of the license terms of these independent
// modules, and to copy and distribute the resulting executable under
// terms of your choice, provided that you also meet, for each linked
// independent module, the terms and conditions of the license of that
// module. An independent module is a module which is not derived from
// or based on this library. If you modify this library, you may extend
// this exception to your version of the library, but you are not
// obligated to do so. If you do not wish to do so, delete this
// exception statement from your version.

using SilverSim.Updater;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;

namespace SilverSim.Packager
{
    internal static class Application
    {
        private static bool IsFrameworkAssembly(string refAssembly) =>
            refAssembly.StartsWith("System.") ||
                refAssembly == "mscorlib" ||
                refAssembly == "System" ||
                refAssembly == "PresentationFramework" ||
                refAssembly == "PresentationCore" ||
                refAssembly == "WindowsBase" ||
                refAssembly == "Microsoft.CSharp" ||
                refAssembly.StartsWith("PresentationFramework.");

        private static List<string> GetFileList()
        {
            var files = new List<string>();
            files.AddRange(Directory.GetFiles("data", "*", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles("bin", "*", SearchOption.AllDirectories));
            var outfiles = new List<string>();
            foreach(string infile in files)
            {
                string file = infile.Replace("\\", "/");
                if(file.EndsWith(".ini") ||
                    file.EndsWith(".pdb") ||
                    file.EndsWith(".log") ||
                    file.EndsWith("SilverSim.Packager.exe") ||
                    file.EndsWith(".vshost.exe") ||
                    file.EndsWith(".vshost.exe.config") ||
                    file.EndsWith(".manifest") ||
                    file.EndsWith(".suo") ||
                    file.EndsWith(".txt") ||
                    file.EndsWith(".a") ||
                    file.EndsWith(".p12") ||
                    file.EndsWith(".spkg") ||
                    file.EndsWith(".db") ||
                    file.StartsWith(".") ||
                    file.Contains("/.") ||
                    file.EndsWith(".build"))
                {
                    continue;
                }
                outfiles.Add(file);
            }
            return outfiles;
        }

        private static void Main(string[] args)
        {
            Thread.CurrentThread.Name = "SilverSim:Packager";

            string[] pkgfiles = Directory.GetFiles(CoreUpdater.Instance.InstalledPackagesPath, "*.spkg");

            var packages = new Dictionary<string, PackageDescription>();

            Console.WriteLine("Loading package descriptions ...");
            foreach (string pkgfile in pkgfiles)
            {
                PackageDescription desc;
                using (Stream i = new FileStream(pkgfile, FileMode.Open))
                {
                    try
                    {
                        desc = new PackageDescription(i);
                    }
                    catch
                    {
                        Console.WriteLine("Failed to load package description {0}", pkgfile);
                        Environment.Exit(1);
                        return;
                    }

                    if(!pkgfile.EndsWith(desc.Name + ".spkg"))
                    {
                        Console.WriteLine("Package description {0} does not match the declared name {1}.", pkgfile, desc.Name);
                    }
                    try
                    {
                        packages.Add(desc.Name, desc);
                    }
                    catch
                    {
                        Console.WriteLine("Installed package {0} is duplicate in {1}.", desc.Name, pkgfile);
                        Environment.Exit(1);
                        return;
                    }
                }
            }

            Console.WriteLine("Verifying dependencies ...");
            bool depcheckfailed = false;
            bool partialpackagelist = args.Contains("--partial-package-list");
            foreach (PackageDescription desc in packages.Values)
            {
                foreach(KeyValuePair<string, string> dep in desc.Dependencies)
                {
                    if(!packages.ContainsKey(dep.Key) && !partialpackagelist)
                    {
                        Console.WriteLine("Package {0} has unknown dependency {1}", desc.Name, dep.Key);
                        depcheckfailed = true;
                    }
                    if(dep.Key == desc.Name)
                    {
                        Console.WriteLine("Package {0} contains unallowed self-reference", desc.Name);
                        depcheckfailed = true;
                    }
                }
            }

            if(depcheckfailed)
            {
                Environment.Exit(1);
                return;
            }

            var assemblytopackage = new Dictionary<string, string>();
            var assembliesreferenced = new Dictionary<string, List<string>>();

            Directory.SetCurrentDirectory(CoreUpdater.Instance.InstallRootPath);

            Console.WriteLine("Collecting file list ...");
            List<string> availablefiles = GetFileList();
            var existingfiles = new List<string>(availablefiles);

            Console.WriteLine("Validating files being used ...");
            bool filecheckfailed = false;
            foreach(PackageDescription desc in packages.Values)
            {
                foreach(KeyValuePair<string, PackageDescription.FileInfo> kvp in desc.Files)
                {
                    if(File.Exists(kvp.Key) && !existingfiles.Contains(kvp.Key))
                    {
                        existingfiles.Add(kvp.Key);
                        availablefiles.Add(kvp.Key);
                    }
                    if(!existingfiles.Contains(kvp.Key))
                    {
                        filecheckfailed = true;
                        Console.WriteLine("Package {0} references unknown file {1}", desc.Name, kvp.Key);
                    }
                    else if(!availablefiles.Contains(kvp.Key))
                    {
                        filecheckfailed = true;
                        Console.WriteLine("Package {0} references already referenced file {1}", desc.Name, kvp.Key);
                    }
                    else
                    {
                        availablefiles.Remove(kvp.Key);
                        if(kvp.Key.EndsWith(".dll") || kvp.Key.EndsWith(".exe"))
                        {
                            Assembly a;
                            try
                            {
                                a = Assembly.LoadFile(Path.GetFullPath(kvp.Key));
                            }
                            catch
                            {
                                a = null;
                            }
                            if(a != null)
                            {
                                assemblytopackage[a.GetName().Name] = desc.Name;
                                List<string> refs = new List<string>();
                                assembliesreferenced[a.GetName().Name] = refs;
                                foreach(AssemblyName aName in a.GetReferencedAssemblies())
                                {
                                    refs.Add(aName.Name);
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine("Validating preload-assemblies being used ...");
            foreach (PackageDescription desc in packages.Values)
            {
                foreach (PackageDescription.PreloadAssembly preloadData in desc.PreloadAssemblies)
                {
                    bool notfound = true;
                    string actualfname = "bin/" + preloadData.Filename;
                    foreach (KeyValuePair<string, PackageDescription.FileInfo> kvp in desc.Files)
                    {
                        if(kvp.Key == actualfname)
                        {
                            notfound = false;
                        }
                    }

                    if (notfound)
                    {
                        filecheckfailed = true;
                        Console.WriteLine("Package {0} references preload-assembly {1} not being in file-list", desc.Name, actualfname);
                    }
                }
            }

            if (!partialpackagelist)
            {
                foreach (string file in availablefiles)
                {
                    filecheckfailed = true;
                    Console.WriteLine("Found unreferenced file {0}", file);
                }
            }

            if(filecheckfailed)
            {
                Environment.Exit(1);
                return;
            }
            Console.WriteLine("Calculating hashes ... ");
            var finalPacks = new Dictionary<string, PackageDescriptionBuilder>();
            foreach(PackageDescription desc in packages.Values)
            {
                PackageDescriptionBuilder builder = new PackageDescriptionBuilder(desc);
                Console.WriteLine("Calculating file hashes of package {0}", desc.Name);
                foreach (string file in desc.Files.Keys)
                {
                    PackageDescription.FileInfo fi = desc.Files[file];
                    using (SHA256 sha = SHA256.Create())
                    {
                        using (Stream s = new FileStream(file, FileMode.Open, FileAccess.Read))
                        {
                            sha.ComputeHash(s);
                        }
                        fi.Hash = sha.Hash;
                    }
                    builder.Files[file] = fi;
                }

                finalPacks.Add(desc.Name, builder);
            }

            Console.WriteLine("Checking dependencies ...");
            foreach(KeyValuePair<string, string> kvp in assemblytopackage)
            {
                PackageDescriptionBuilder desc = finalPacks[kvp.Value];
                foreach(string refAssembly in assembliesreferenced[kvp.Key])
                {
                    if(IsFrameworkAssembly(refAssembly))
                    {
                        /* skip System. namespace */
                        continue;
                    }
                    string pkgname;
                    if(!assemblytopackage.TryGetValue(refAssembly, out pkgname) ||
                        !finalPacks.ContainsKey(pkgname))
                    {
                        Console.WriteLine("Package {0} requires reference to {1} which is not packaged.", kvp.Value, refAssembly);
                        Environment.Exit(1);
                        return;
                    }

                    if(!desc.Dependencies.ContainsKey(pkgname) && pkgname != desc.Name)
                    {
                        desc.Dependencies.Add(pkgname, string.Empty);
                    }
                }
            }

            Console.WriteLine("Collecting file version ...");
            var versions = new Dictionary<string, string>();
            var licenses = new Dictionary<string, string>();
            foreach (PackageDescriptionBuilder desc in finalPacks.Values)
            {
                foreach(string filename in new List<string>(desc.Files.Keys))
                {
                    if(filename.EndsWith(".dll") || filename.EndsWith(".exe"))
                    {
                        PackageDescription.FileInfo fi = desc.Files[filename];
                        try
                        {
                            Assembly a = Assembly.LoadFile(Path.GetFullPath(filename));
                            fi.Version = a.GetName().Version.ToString();
                            desc.Files[filename] = fi;
                            Console.WriteLine("Appended version {0} to {1}", fi.Version, filename);
                            if (fi.IsVersionSource)
                            {
                                versions[desc.Name] = fi.Version;
                                var copyrightAttr = a.GetCustomAttribute(typeof(AssemblyCopyrightAttribute)) as AssemblyCopyrightAttribute;
                                if (copyrightAttr != null)
                                {
                                    licenses[desc.Name] = copyrightAttr.Copyright;
                                }
                            }
                        }
                        catch
                        {
                            /* ignore */
                        }
                    }
                }
            }

            string interfaceVersion = "0.0.0.0";

            if (File.Exists("versioninject.xml"))
            {
                Console.WriteLine("Loading versioninject.xml ...");
                var matchversions = new List<string>();

                using (var x = new FileStream("versioninject.xml", FileMode.Open, FileAccess.Read))
                {
                    using (var reader = new XmlTextReader(x)
                    {
                        DtdProcessing = DtdProcessing.Ignore,
                        XmlResolver = null
                    })
                    {
                        while (reader.Read())
                        {
                            switch (reader.NodeType)
                            {
                                case XmlNodeType.Element:
                                    switch (reader.Name)
                                    {
                                        case "interface-version":
                                            if (reader.IsEmptyElement)
                                            {
                                                break;
                                            }
                                            interfaceVersion = reader.ReadElementContentAsString();
                                            break;

                                        case "default-version":
                                            if (reader.MoveToFirstAttribute())
                                            {
                                                string version = string.Empty;
                                                do
                                                {
                                                    switch (reader.Name)
                                                    {
                                                        case "version":
                                                            version = reader.Value;
                                                            break;
                                                    }
                                                } while (reader.MoveToNextAttribute());
                                                if (version?.Length != 0)
                                                {
                                                    foreach (string pkgname in finalPacks.Keys)
                                                    {
                                                        if (versions.ContainsKey(pkgname))
                                                        {
                                                            continue;
                                                        }
                                                        versions[pkgname] = version;
                                                    }
                                                }
                                            }
                                            break;

                                        case "package":
                                            if (reader.MoveToFirstAttribute())
                                            {
                                                string package = string.Empty;
                                                string version = string.Empty;
                                                string license = string.Empty;
                                                bool exactmatch = false;
                                                do
                                                {
                                                    switch (reader.Name)
                                                    {
                                                        case "name":
                                                            package = reader.Value;
                                                            break;

                                                        case "version":
                                                            version = reader.Value;
                                                            break;

                                                        case "license":
                                                            license = reader.Value;
                                                            break;

                                                        case "version-src":
                                                            try
                                                            {
                                                                Assembly a = Assembly.LoadFile(Path.GetFullPath(reader.Value));
                                                                version = a.GetName().Version.ToString();
                                                                var copyrightAttr = a.GetCustomAttribute(typeof(AssemblyCopyrightAttribute)) as AssemblyCopyrightAttribute;
                                                                if (copyrightAttr != null)
                                                                {
                                                                    license = copyrightAttr.Copyright;
                                                                }
                                                            }
                                                            catch
                                                            {
                                                                if (!partialpackagelist)
                                                                {
                                                                    Console.WriteLine("Failed to load assembly {0}", reader.Value);
                                                                    Environment.Exit(1);
                                                                    return;
                                                                }
                                                            }
                                                            break;

                                                        case "version-from-package-files":
                                                            PackageDescription actpack = packages[package];
                                                            foreach (PackageDescription.FileInfo fi in actpack.Files.Values)
                                                            {
                                                                if (fi.Version?.Length != 0)
                                                                {
                                                                    version = fi.Version;
                                                                    break;
                                                                }
                                                            }
                                                            break;

                                                        case "exactmatch":
                                                            exactmatch = bool.Parse(reader.Value);
                                                            break;
                                                    }
                                                } while (reader.MoveToNextAttribute());
                                                if (package?.Length != 0 && version?.Length != 0)
                                                {
                                                    versions[package] = version;
                                                    if (exactmatch)
                                                    {
                                                        matchversions.Add(package);
                                                    }
                                                }
                                                if (package?.Length != 0 && license?.Length != 0)
                                                {
                                                    licenses[package] = license;
                                                }
                                            }
                                            break;

                                        default:
                                            break;
                                    }
                                    break;

                                default:
                                    break;
                            }
                        }
                    }
                }

                Console.WriteLine("Checking completeness ...");
                bool versionmissing = false;
                foreach(PackageDescriptionBuilder desc in finalPacks.Values)
                {
                    if(!versions.ContainsKey(desc.Name) && string.IsNullOrEmpty(desc.Version) && !desc.SkipDelivery)
                    {
                        Console.WriteLine("Package {0} not in versioninject.xml", desc.Name);
                        versionmissing = true;
                    }
                }

                if(versionmissing)
                {
                    Environment.Exit(1);
                    return;
                }

                Console.WriteLine("Injecting versions ...");
                foreach(PackageDescriptionBuilder desc in finalPacks.Values)
                {
                    if(desc.SkipDelivery)
                    {
                        continue;
                    }
                    if (string.IsNullOrEmpty(desc.Version))
                    {
                        desc.Version = versions[desc.Name];
                    }
                    desc.InterfaceVersion = interfaceVersion;
                    if(licenses.ContainsKey(desc.Name))
                    {
                        desc.License = licenses[desc.Name];
                    }

                    foreach(KeyValuePair<string, string> kvp in new Dictionary<string, string>(desc.Dependencies))
                    {
                        if(matchversions.Contains(kvp.Key))
                        {
                            desc.Dependencies[kvp.Key] = versions[kvp.Key];
                        }
                    }
                }
            }

            Console.WriteLine("Checking versions ...");
            foreach (PackageDescriptionBuilder desc in finalPacks.Values)
            {
                if (desc.SkipDelivery)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(desc.Version))
                {
                    desc.Version = "0.0.0.0";
                }
                if (string.IsNullOrEmpty(desc.InterfaceVersion))
                {
                    desc.InterfaceVersion = interfaceVersion;
                }
            }

            Console.WriteLine("Final package versions ...");
            foreach(PackageDescriptionBuilder desc in finalPacks.Values)
            {
                if (desc.SkipDelivery)
                {
                    continue;
                }
                Console.WriteLine("Package {0} => {1}", desc.Name, desc.Version);
            }

            var skippackages = new List<string>();
            if (File.Exists("skippackagelist.xml"))
            {
                using (var fs = new FileStream("skippackagelist.xml", FileMode.Open, FileAccess.Read))
                {
                    using (var reader = new XmlTextReader(fs)
                    {
                        DtdProcessing = DtdProcessing.Ignore,
                        XmlResolver = null
                    })
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element && reader.Name == "package")
                            {
                                if (reader.MoveToFirstAttribute())
                                {
                                    do
                                    {
                                        switch (reader.Name)
                                        {
                                            case "name":
                                                skippackages.Add(reader.Value);
                                                break;
                                        }
                                    } while (reader.MoveToNextAttribute());
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine("Building packaged structure ...");
            foreach(PackageDescriptionBuilder desc in finalPacks.Values)
            {
                if(skippackages.Contains(desc.Name) || desc.SkipDelivery)
                {
                    continue;
                }

                Console.WriteLine("Packing {0} into zip", desc.Name);
                string zipPath = PackageZipPath(desc);
                string zipFolderPath = Path.GetFullPath(Path.Combine(zipPath, ".."));
                if(!Directory.Exists(zipFolderPath))
                {
                    Directory.CreateDirectory(zipFolderPath);
                }
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                using (var zipStream = new FileStream(zipPath + ".tmp", FileMode.Create))
                {
                    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                    {
                        foreach(string file in desc.Files.Keys)
                        {
                            ZipArchiveEntry e = archive.CreateEntry(file);
                            using (Stream o = e.Open())
                            {
                                using (FileStream i = new FileStream(file, FileMode.Open, FileAccess.Read))
                                {
                                    i.CopyTo(o);
                                }
                            }
                        }
                    }
                }

                File.Delete(zipPath);
                File.Move(zipPath + ".tmp", zipPath);

                Console.WriteLine("Hasing zip of {0}", desc.Name);
                using (var zipStream = new FileStream(zipPath, FileMode.Open))
                {
                    using (var sha = SHA256.Create())
                    {
                        sha.ComputeHash(zipStream);
                        desc.Hash = sha.Hash;
                    }
                }
            }

            Console.WriteLine("Write package feed ...");
            foreach(PackageDescription desc in finalPacks.Values)
            {
                if (skippackages.Contains(desc.Name) || desc.SkipDelivery)
                {
                    continue;
                }

                string targetname = PackageUpdateFeedPath(desc);
                desc.WriteFile(targetname + ".tmp");
                File.Delete(targetname);
                File.Move(targetname + ".tmp", targetname);
                targetname = PackageSpecificVersionFeedPath(desc);
                desc.WriteFile(targetname + ".tmp");
                File.Delete(targetname);
                File.Move(targetname + ".tmp", targetname);
            }

            var hidepackages = new List<string>();
            if(File.Exists("hidepackagelist.xml"))
            {
                using (var fs = new FileStream("hidepackagelist.xml", FileMode.Open, FileAccess.Read))
                {
                    using (var reader = new XmlTextReader(fs)
                    {
                        DtdProcessing = DtdProcessing.Ignore,
                        XmlResolver = null
                    })
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element && reader.Name == "package")
                            {
                                if (reader.MoveToFirstAttribute())
                                {
                                    do
                                    {
                                        switch (reader.Name)
                                        {
                                            case "name":
                                                hidepackages.Add(reader.Value);
                                                break;
                                        }
                                    } while (reader.MoveToNextAttribute());
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine("Updating package feed list ...");
            string feedList = "feed/" + interfaceVersion + "/packages.list";
            using (var writer = new XmlTextWriter(feedList + ".tmp", new UTF8Encoding(false)))
            {
                writer.WriteStartElement("packages");
                foreach (string file in Directory.GetFiles("feed/" + interfaceVersion, "*.spkg", SearchOption.TopDirectoryOnly))
                {
                    string pkgname = Path.GetFileNameWithoutExtension(file);
                    writer.WriteStartElement("package");
                    writer.WriteAttributeString("name", pkgname);
                    if(hidepackages.Contains(pkgname))
                    {
                        writer.WriteAttributeString("hidden", "true");
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            File.Delete(feedList);
            File.Move(feedList + ".tmp", feedList);
        }

        private static string PackageUpdateFeedPath(PackageDescription desc) =>
            string.Format("feed/{0}/{1}.spkg", desc.InterfaceVersion, desc.Name);

        private static string PackageSpecificVersionFeedPath(PackageDescription desc) =>
            string.Format("feed/{0}/{1}/{2}.spkg", desc.InterfaceVersion, desc.Version, desc.Name);

        private static string PackageZipPath(PackageDescription desc) =>
            string.Format("feed/{0}/{1}/{2}.zip", desc.InterfaceVersion, desc.Version, desc.Name);
    }
}
