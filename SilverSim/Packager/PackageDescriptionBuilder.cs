// SilverSim is distributed under the terms of the
// GNU Affero General Public License v3

using SilverSim.Updater;
using System.Collections.Generic;

namespace SilverSim.Packager
{
    public class PackageDescriptionBuilder : PackageDescription
    {
        public PackageDescriptionBuilder(string name)
        {
            Name = name;
        }

        public PackageDescriptionBuilder(PackageDescription desc)
        {
            Version = desc.Version;
            InterfaceVersion = desc.InterfaceVersion;
            Name = desc.Name;
            Hash = desc.Hash;
            foreach(KeyValuePair<string, string> kvp in desc.Dependencies)
            {
                m_Dependencies.Add(kvp.Key, kvp.Value);
            }
            foreach(KeyValuePair<string, PackageDescription.FileInfo> kvp in Files)
            {
                m_Files.Add(kvp.Key, kvp.Value);
            }
            foreach(Configuration cfg in DefaultConfigurations)
            {                
                m_DefaultConfigurations.Add(cfg);
            }
        }

        public new string InterfaceVersion
        {
            get
            {
                return base.InterfaceVersion;
            }
            set
            {
                base.InterfaceVersion = value;
            }
        }

        public new string Version
        {
            get
            {
                return base.Version;
            }
            set
            {
                base.Version = value;
            }
        }

        public new byte[] Hash
        {
            get
            {
                return base.Hash;
            }
            set
            {
                base.Hash = value;
            }
        }

        public new Dictionary<string, string> Dependencies { get { return m_Dependencies; } }
        public new Dictionary<string, PackageDescription.FileInfo> Files { get { return m_Files; } }
    }
}
