using System;
using System.Net;
using System.Diagnostics;

using Terminals.Connections;

namespace Terminals.Data
{
    internal static class FavoritesFactory
    {
        private const string DISCOVERED_CONNECTIONS = "Discovered Connections";
        private static readonly String terminalsReleasesFavoriteName = Program.Resources.GetString("TerminalsNews");

        // ------------------------------------------------

        internal static string TerminalsReleasesFavoriteName
        {
            get { return terminalsReleasesFavoriteName; }
        }

        // ------------------------------------------------

        internal static FavoriteConfigurationElement CreateNewFavorite(ConnectionManager connectionManager, string favoriteName, string server, int port,
                                                                       string domain, string userName)
        {
            var newFavorite = new FavoriteConfigurationElement()
            { 
                Port = port,
                Name = favoriteName,
                ServerName = server,
                UserName = userName,
                DomainName = domain,
                Tags = DISCOVERED_CONNECTIONS,
                Protocol = connectionManager.GetPortName(port)
            };

            return newFavorite;
        }

        // ------------------------------------------------

        internal static FavoriteConfigurationElement CreateNewFavorite(ConnectionManager connectionManager, string favoriteName, string server, int port)
        {
            var name = GetHostName(connectionManager, server, favoriteName, port);
            var domainName = GetCurrentDomainName(server);
            return CreateNewFavorite(connectionManager, name, server, port, domainName, Environment.UserName);
        }

        // ------------------------------------------------

        private static string GetCurrentDomainName(string server)
        {
            if(Environment.UserDomainName != Environment.MachineName)
            {
                return Environment.UserDomainName;
            }

            return server;
        }

        // ------------------------------------------------

        private static string GetHostName(ConnectionManager connectionManager, string server, string name, int port)
        {
            try
            {
                IPAddress address;

                if(IPAddress.TryParse(server, out address))
                {
                    name = Dns.GetHostEntry(address).HostName;
                }

                var portName = connectionManager.GetPortName(port);
                return string.Format("{0}_{1}", name, portName);
            }
            catch 
            {
                // ----------------------
                // don't log dns lookups!

                Debug.WriteLine("Unable to resolve '{0}' host name.", server);
                return name;
            }
        }

        /// -----------------------------------------------
        /// <summary>
        ///     Gets persisted favorite, if there is a favorite named by server parameter.
        ///     If no favorite is found creates new favorite, which is configured by parameter properties
        ///     and point to RDP server.
        /// </summary>
        /// <param name="server">the RDP server name</param>
        /// <param name="connectToConsole">Flag used for ConnectToConsole RDP option</param>
        /// <param name="port">Number of port, which RDP service is listening on server "server"</param>

        internal static IFavorite GetOrCreateQuickConnectFavorite(IPersistence persistence, string server, bool connectToConsole, int port)
        {
            var favorite = persistence.Favorites[server];

            // ---------------------------------------------
            // create a temporary favorite and connect to it

            if(favorite == null) 
            {
                favorite = persistence.Factory.CreateFavorite();
                favorite.ServerName = server;
                favorite.Name = server;

                if(port != 0)
                {
                    favorite.Port = port;
                }
            }

            var rdpProperties = favorite.ProtocolProperties as IForceConsoleOptions;

            if(rdpProperties != null)
            {
                rdpProperties.ConnectToConsole = connectToConsole;
            }

            return favorite;
        }

        /// -----------------------------------------------
        /// <summary>
        ///     Gets group with required groupName or creates 
        ///     new group which is immediately added to the persistence.
        /// </summary>
        /// <param name="persistence">Not null persistence, where to search for groups</param>
        /// <param name="groupName">Name of the group to search in persistence.</param>
        /// <returns>Not null value of Group obtained from persistence or newly created group</returns>

        internal static IGroup GetOrAddNewGroup(IPersistence persistence, string groupName)
        {
            var groups = persistence.Groups;
            var group = groups[groupName];
            
            if(group == null)
            {
                group = persistence.Factory.CreateGroup(groupName);
                groups.Add(group);
            }

            return group;
        }
    }
}
