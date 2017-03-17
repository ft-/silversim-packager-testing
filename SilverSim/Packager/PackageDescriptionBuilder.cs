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
            foreach(KeyValuePair<string, PackageDescription.FileInfo> kvp in desc.Files)
            {
                m_Files.Add(kvp.Key, kvp.Value);
            }
            foreach(Configuration cfg in desc.DefaultConfigurations)
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
