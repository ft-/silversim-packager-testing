﻿// SilverSim is distributed under the terms of the
// GNU Affero General Public License v3

using SilverSim.Updater;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Xml;

namespace SilverSim.Packager
{
    static class Application
    {
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

                if(string.IsNullOrEmpty(builder.Version))
                {
                    builder.Version = "0.0.0.0";
                }
                if(string.IsNullOrEmpty(builder.InterfaceVersion))
                {
                    builder.InterfaceVersion = "0.0.0.0";
                }
                finalPacks.Add(desc.Name, builder);
            }

            if (File.Exists("versioninject.xml"))
            {
                Console.WriteLine("Loading versioninject.xml ...");
                Dictionary<string, string> versions = new Dictionary<string, string>();
                List<string> matchversions = new List<string>();
                string interfaceVersion = string.Empty;
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
                            desc.Dependencies[kvp.Key] = versions[desc.Name];
                        }
                    }
                }
            }


            Console.WriteLine("Building packaged structure ...");
            foreach(PackageDescriptionBuilder desc in finalPacks.Values)
            {
                Console.WriteLine("Packing {0} into zip", desc.Name);
                string zipPath = PackageZipPath(desc);
                Directory.CreateDirectory(Path.Combine(zipPath, ".."));
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
