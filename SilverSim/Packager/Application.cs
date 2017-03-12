// SilverSim is distributed under the terms of the
// GNU Affero General Public License v3

using SilverSim.Updater;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;

namespace SilverSim.Packager
{
    static class Application
    {
        [SuppressMessage("Gendarme.Rules.Exceptions", "DoNotSwallowErrorsCatchingNonSpecificExceptionsRule")]
        static void Main(string[] args)
        {
            Thread.CurrentThread.Name = "SilverSim:Packager";
            CoreUpdater.Instance.CheckForUpdates();
            CoreUpdater.Instance.VerifyInstallation();
        }
    }
}
