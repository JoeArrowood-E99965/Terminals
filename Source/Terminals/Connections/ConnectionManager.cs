using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

using Terminals.Data;
using Terminals.Common.Connections;
using Terminals.Integration.Export;

namespace Terminals.Connections
{
    internal class ConnectionManager : IConnectionManager
    {
        private readonly IConnectionPlugin _dummyPlugin = new DummyPlugin();

        private readonly Dictionary<string, IConnectionPlugin> _plugins;

        // ------------------------------------------------

        internal ConnectionManager(IPluginsLoader loader)
        {            
            IEnumerable<IConnectionPlugin> loaded = loader.Load();
            _plugins = SortExternalPlugins(loaded);
        }

        // ------------------------------------------------

        private static Dictionary<string, IConnectionPlugin> SortExternalPlugins(IEnumerable<IConnectionPlugin> plugins)
        {
           var sortedPlugins = new Dictionary<string, IConnectionPlugin>();

            foreach (IConnectionPlugin loaded in plugins)
            {
                SortPlugin(sortedPlugins, loaded);
            }

            return sortedPlugins;
        }

        // ------------------------------------------------

        private static void SortPlugin(Dictionary<string, IConnectionPlugin> sortedPlugins, IConnectionPlugin loaded)
        {
            if(sortedPlugins.ContainsKey(loaded.PortName))
            {
                LogDuplicitPlugin(loaded);
            }
            else
            {
                sortedPlugins.Add(loaded.PortName, loaded);
            }
        }

        // ------------------------------------------------

        private static void LogDuplicitPlugin(IConnectionPlugin loaded)
        {
            var pluginType = loaded.GetType();
            var assemblyPath = pluginType.Assembly.CodeBase;
            var message = $"Plugin for protocol {loaded.PortName} ({pluginType}:{assemblyPath}) already present.";

            Logging.Warn(message);
        }

        // ------------------------------------------------

        internal Dictionary<string, Image> GetPluginIcons()
        {
            return _plugins.Values.ToDictionary(p => p.PortName, p => p.GetIcon());
        }

        // ------------------------------------------------
        /// <summary>
        ///     Explicit call of update properties container depending on selected protocol.
        ///     Don't call this in property setter, because of serializer.
        ///     Returns never null instance of the options, in case the protocol is identical, returns currentOptions.
        /// </summary>

        internal ProtocolOptions UpdateProtocolPropertiesByProtocol(string newProtocol, ProtocolOptions currentOptions)
        {
            IConnectionPlugin plugin = FindPlugin(newProtocol);
            return SwitchPropertiesIfNotTheSameType(currentOptions, plugin);
        }

        // ------------------------------------------------

        private static ProtocolOptions SwitchPropertiesIfNotTheSameType(ProtocolOptions currentOptions, IConnectionPlugin plugin)
        {
            // ---------------------------
            // prevent to reset properties

            if(currentOptions == null || currentOptions.GetType() != plugin.GetOptionsType())
            {
                return plugin.CreateOptions();
            }

            return currentOptions;
        }

        // ------------------------------------------------

        internal ushort[] SupportedPorts()
        {
            return _plugins.Values.Where(p => !IsProtocolWebBased(p.PortName))
                .Select(p => Convert.ToUInt16(p.Port))
                .Distinct()
                .ToArray();
        }

        // ------------------------------------------------

        internal Connection CreateConnection(IFavorite favorite)
        {
            IConnectionPlugin plugin = FindPlugin(favorite.Protocol);
            return plugin.CreateConnection();
        }

        // ------------------------------------------------

        internal int GetPort(string name)
        {
            IConnectionPlugin plugin = FindPlugin(name);
            return plugin.Port;
        }

        // ------------------------------------------------
        /// <summary>
        ///     Returns at least one pluging representing port.
        /// </summary>

        public IEnumerable<IConnectionPlugin> GetPluginsByPort(int port)
        {
            var resolvedPlugins = _plugins.Values.Where(p => PluginIsOnPort(port, p))
                .ToList();

            if(resolvedPlugins.Count > 0)
            {
                return resolvedPlugins;
            }

            return new List<IConnectionPlugin>() { _dummyPlugin};
        }

        // ------------------------------------------------
        /// <summary>
        ///     Resolves first service from known plugins 
        ///     assigned to requested port.
        ///     Returns RDP as default service.
        /// </summary>

        internal string GetPortName(int port)
        {
            // -------------------------------------------------
            // hack to let the VNC take precedence over the VMRC
           
            var plugin = _plugins.Values.OrderBy(p => p.PortName.Length)
                .FirstOrDefault(p => PluginIsOnPort(port, p));

            if(plugin != null)
            {
                return plugin.PortName;
            }

            return _dummyPlugin.PortName;
        }

        // ------------------------------------------------

        private static bool PluginIsOnPort(int port, IConnectionPlugin plugin)
        {
            return plugin.Port == port;
        }

        // ------------------------------------------------
        /// <summary>
        ///     Ensures web based protocol shortcut. 
        ///     Returns true in case of HTTP or HTTPS.
        /// </summary>
        /// <param name="protocol">One of connection short cuts.</param>

        internal bool IsProtocolWebBased(string protocol)
        {
            return protocol == KnownConnectionConstants.HTTP || protocol == KnownConnectionConstants.HTTPS;
        }

        // ------------------------------------------------

        internal bool IsKnownProtocol(string protocol)
        {
            return _plugins.Any(p => p.Key == protocol);
        }

        // ------------------------------------------------

        internal Control[] CreateControls(string newProtocol)
        {
            IConnectionPlugin plugin = FindPlugin(newProtocol);
            return plugin.CreateOptionsControls();
        }

        // ------------------------------------------------

        private IConnectionPlugin FindPlugin(string protocolName)
        {
            IConnectionPlugin plugin;

            if(_plugins.TryGetValue(protocolName, out plugin))
            {
                return plugin;
            }

            return _dummyPlugin;
        }

        // ------------------------------------------------

        public string[] GetAvailableProtocols()
        {
            return _plugins.Values.Select(p => p.PortName)
                .ToArray();
        }

        // ------------------------------------------------

        internal ITerminalsOptionsExport[] GetTerminalsOptionsExporters()
        {
            return _plugins.Values.OfType<IOptionsExporterFactory>()
                .Select(p => p.CreateOptionsExporter())
                .ToArray();
        }

        // ------------------------------------------------

        public IToolbarExtender[] CreateToolbarExtensions(ICurrenctConnectionProvider provider)
        {
            return _plugins.Values.OfType<IToolbarExtenderFactory>()
                .Select(p => p.CreateToolbarExtender(provider))
                .ToArray();
        }

        // ------------------------------------------------

        public Type[] GetAllKnownProtocolOptionTypes()
        {
            List<Type> knownTypes = _plugins.Values
                .Select(p => p.GetOptionsType())
                .ToList();

            knownTypes.Add(typeof(EmptyOptions));
            return knownTypes.Distinct()
                .ToArray();
        }

        // ------------------------------------------------

        public IOptionsConverterFactory GetOptionsConverterFactory(string protocolName)
        {
            var protocolPlugin = FindPlugin(protocolName) as IOptionsConverterFactory;

            if(protocolPlugin == null)
            {
                protocolPlugin = _dummyPlugin as IOptionsConverterFactory;
            }

            return protocolPlugin;
        }

        // ------------------------------------------------

        internal void SetDefaultProtocol(IFavorite favorite)
        {
            string defaultProtocol = KnownConnectionConstants.RDP;
            var available = GetAvailableProtocols();

            if(!available.Contains(defaultProtocol))
            {
                defaultProtocol = available.First();
            }

            ChangeProtocol(favorite, defaultProtocol);
        }

        // ------------------------------------------------

        public void ChangeProtocol(IFavorite favorite, string protocol)
        {
            ProtocolOptions options = UpdateProtocolPropertiesByProtocol(protocol, favorite.ProtocolProperties);
            favorite.ChangeProtocol(protocol, options);
        }

        // ------------------------------------------------

        public override string ToString()
        {
            string[] loadedProtocols = GetAvailableProtocols();
            string pluginsLabel = string.Join(",", loadedProtocols);
            return string.Format("ConnectionManager:{0}", pluginsLabel);
        }
    }
}
