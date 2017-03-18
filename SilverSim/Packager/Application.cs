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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;

namespace SilverSim.Packager
{
    static class Application
    {
        static bool IsFrameworkAssembly(string refAssembly)
        {
            return refAssembly.StartsWith("System.") ||
                refAssembly == "mscorlib" ||
                refAssembly == "System" ||
                refAssembly == "PresentationFramework" ||
                refAssembly == "PresentationCore" ||
                refAssembly == "WindowsBase" ||
                refAssembly == "Microsoft.CSharp" ||
                refAssembly.StartsWith("PresentationFramework.");
        }

        static List<string> GetFileList()
        {
            List<string> files = new List<string>();
            files.AddRange(Directory.GetFiles("data", "*", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles("bin", "*", SearchOption.AllDirectories));
            List<string> outfiles = new List<string>();
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
                    file.StartsWith(".") ||
                    file.Contains("/."))
                {
                    continue;
                }
                outfiles.Add(file);
            }
            return outfiles;
        }

        [SuppressMessage("Gendarme.Rules.Exceptions", "DoNotSwallowErrorsCatchingNonSpecificExceptionsRule")]
        static void Main(string[] args)
        {
            Thread.CurrentThread.Name = "SilverSim:Packager";

            string[] pkgfiles = Directory.GetFiles(CoreUpdater.Instance.InstalledPackagesPath, "*.spkg");

            Dictionary<string, PackageDescription> packages = new Dictionary<string, PackageDescription>();

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
            foreach (PackageDescription desc in packages.Values)
            {
                foreach(KeyValuePair<string, string> dep in desc.Dependencies)
                {
                    if(!packages.ContainsKey(dep.Key))
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

            Dictionary<string, string> assemblytopackage = new Dictionary<string, string>();
            Dictionary<string, List<string>> assembliesreferenced = new Dictionary<string, List<string>>();

            Directory.SetCurrentDirectory(CoreUpdater.Instance.InstallRootPath);

            Console.WriteLine("Collecting file list ...");
            List<string> availablefiles = GetFileList();
            List<string> existingfiles = new List<string>(availablefiles);

            Console.WriteLine("Validating files being used ...");
            bool filecheckfailed = false;
            foreach(PackageDescription desc in packages.Values)
            {
                foreach(KeyValuePair<string, PackageDescription.FileInfo> kvp in desc.Files)
                {
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

            foreach(string file in availablefiles)
            {
                filecheckfailed = true;
                Console.WriteLine("Found unreferenced file {0}", file);
            }

            if(filecheckfailed)
            {
                Environment.Exit(1);
                return;
            }
            Console.WriteLine("Calculating hashes ... ");
            Dictionary<string, PackageDescriptionBuilder> finalPacks = new Dictionary<string, PackageDescriptionBuilder>();
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
            foreach(PackageDescriptionBuilder desc in finalPacks.Values)
            {
                foreach(string filename in new List<string>(desc.Files.Keys))
                {
                    if(filename.EndsWith(".dll") || filename.EndsWith(".exe"))
                    {
                        PackageDescription.FileInfo fi = desc.Files[filename];
                        try
                        {
                            fi.Version = Assembly.LoadFile(Path.GetFullPath(filename)).GetName().Version.ToString();
                            desc.Files[filename] = fi;
                            Console.WriteLine("Appended version {0} to {1}", fi.Version, filename);
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
                Dictionary<string, string> versions = new Dictionary<string, string>();
                List<string> matchversions = new List<string>();
                Dictionary<string, string> licenses = new Dictionary<string, string>();
                
                using (FileStream x = new FileStream("versioninject.xml", FileMode.Open, FileAccess.Read))
                {
                    using (XmlTextReader reader = new XmlTextReader(x))
                    {
                        while (reader.Read())
                        {
                            switch (reader.NodeType)
                            {
                                case XmlNodeType.Element:
                                    switch (reader.Name)
                                    {
                                        case "interface-version":
                                            if(reader.IsEmptyElement)
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
                                                if (!string.IsNullOrEmpty(version))
                                                {
                                                    foreach(string pkgname in finalPacks.Keys)
                                                    {
                                                        if(versions.ContainsKey(pkgname))
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
                                                                AssemblyCopyrightAttribute copyrightAttr = a.GetCustomAttribute(typeof(AssemblyCopyrightAttribute)) as AssemblyCopyrightAttribute;
                                                                if(null != copyrightAttr)
                                                                {
                                                                    license = copyrightAttr.Copyright;
                                                                }
                                                            }
                                                            catch
                                                            {
                                                                Console.WriteLine("Failed to load assembly {0}", reader.Value);
                                                                Environment.Exit(1);
                                                                return;
                                                            }
                                                            break;

                                                        case "version-from-package-files":
                                                            PackageDescription actpack = packages[package];
                                                            foreach(PackageDescription.FileInfo fi in actpack.Files.Values)
                                                            {
                                                                if (!string.IsNullOrEmpty(fi.Version))
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
                                                if (!string.IsNullOrEmpty(package) && !string.IsNullOrEmpty(version))
                                                {
                                                    versions[package] = version;
                                                    if (exactmatch)
                                                    {
                                                        matchversions.Add(package);
                                                    }
                                                }
                                                if(!string.IsNullOrEmpty(package) && !string.IsNullOrEmpty(license))
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
                    if(!versions.ContainsKey(desc.Name))
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
                    desc.Version = versions[desc.Name];
                    desc.InterfaceVersion = interfaceVersion;

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
                Console.WriteLine("Package {0} => {1}", desc.Name, desc.Version);
            }

            Console.WriteLine("Building packaged structure ...");
            foreach(PackageDescriptionBuilder desc in finalPacks.Values)
            {
                Console.WriteLine("Packing {0} into zip", desc.Name);
                string zipPath = PackageZipPath(desc);
                Directory.CreateDirectory(Path.Combine(zipPath, ".."));
                if(File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                using (FileStream zipStream = new FileStream(zipPath, FileMode.Create))
                {
                    using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
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

                Console.WriteLine("Hasing zip of {0}", desc.Name);
                using (FileStream zipStream = new FileStream(zipPath, FileMode.Open))
                {
                    using (SHA256 sha = SHA256.Create())
                    {
                        sha.ComputeHash(zipStream);
                        desc.Hash = sha.Hash;
                    }
                }
            }

            Console.WriteLine("Write package feed ...");
            foreach(PackageDescription desc in finalPacks.Values)
            {
                desc.WriteFile(PackageUpdateFeedPath(desc));
                desc.WriteFile(PackageSpecificVersionFeedPath(desc));
            }

            List<string> hidepackages = new List<string>();
            if(File.Exists("hidepackagelist.xml"))
            {
                using (FileStream fs = new FileStream("hidepackagelist.xml", FileMode.Open, FileAccess.Read))
                {
                    using (XmlTextReader reader = new XmlTextReader(fs))
                    {
                        while(reader.Read())
                        {
                            if(reader.NodeType == XmlNodeType.Element && reader.Name == "package")
                            {
                                if (reader.MoveToFirstAttribute())
                                {
                                    string version = string.Empty;
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
            using (XmlTextWriter writer = new XmlTextWriter("feed/" + interfaceVersion + "/packages.list", new UTF8Encoding(false)))
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
        }

        static string PackageUpdateFeedPath(PackageDescription desc)
        {
            return string.Format("feed/{0}/{1}.spkg", desc.InterfaceVersion, desc.Name);
        }

        static string PackageSpecificVersionFeedPath(PackageDescription desc)
        {
            return string.Format("feed/{0}/{1}/{2}.spkg", desc.InterfaceVersion, desc.Version, desc.Name);
        }

        static string PackageZipPath(PackageDescription desc)
        {
            return string.Format("feed/{0}/{1}/{2}.zip", desc.InterfaceVersion, desc.Version, desc.Name);
        }

    }
}
