using System;
using System.Text;
using System.Xml;

using Terminals.Data;
using Terminals.Converters;
using Terminals.Configuration;
using Terminals.Integration.Import;

namespace Terminals.Integration.Export
{
    /// ---------------------------------------------------
    /// <summary>
    ///     This is the Terminals native exporter, 
    ///     which exports favorites into its own file
    /// </summary>
    
    internal class ExportTerminals : IExport
    {
        private readonly IPersistence _persistence;

        private readonly ITerminalsOptionsExport[] _optionsExporters;

        // ------------------------------------------------

        string IIntegration.Name
        {
            get { return ImportTerminals.PROVIDER_NAME; }
        }

        // ------------------------------------------------

        string IIntegration.KnownExtension
        {
            get { return ImportTerminals.TERMINALS_FILEEXTENSION; }
        }

        // ------------------------------------------------

        public ExportTerminals(IPersistence persistence, ITerminalsOptionsExport[] optionsExporters)
        {
            _persistence = persistence;
            _optionsExporters = optionsExporters;
        }

        // ------------------------------------------------

        public void Export(ExportOptions options)
        {
            try
            {
                using (var xmlWriter = new XmlTextWriter(options.FileName, Encoding.UTF8))
                {
                    xmlWriter.Formatting = Formatting.Indented;
                    xmlWriter.WriteStartDocument();
                    xmlWriter.WriteStartElement("favorites");

                    foreach (FavoriteConfigurationElement favorite in options.Favorites)
                    {
                        var favoriteSecurity = new FavoriteConfigurationSecurity(_persistence, favorite);
                        var context = new ExportOptionsContext(xmlWriter, favoriteSecurity, options.IncludePasswords, favorite);

                        WriteFavorite(context);
                    }
                    
                    xmlWriter.WriteEndElement();
                    xmlWriter.WriteEndDocument();

                    xmlWriter.Flush();
                    xmlWriter.Close();
                }
            }
            catch (Exception ex)
            {
                Logging.Error("Export XML Failed", ex);
            }
        }

        // ------------------------------------------------

        private void WriteFavorite(ExportOptionsContext context)
        {
            context.Writer.WriteStartElement("favorite");
            
            ExportGeneralOptions(context.Writer, context.Favorite);
            ExportCredentials(context);
            ExportExecuteBeforeConnect(context.Writer, context.Favorite);

            foreach (ITerminalsOptionsExport optionsExporter in _optionsExporters)
            {
                optionsExporter.ExportOptions(context);
            }

            context.Writer.WriteEndElement();
        }

        // ------------------------------------------------

        private static void ExportGeneralOptions(XmlTextWriter w, FavoriteConfigurationElement favorite)
        {
            w.WriteElementString("protocol", favorite.Protocol);
            w.WriteElementString("port", favorite.Port.ToString());
            w.WriteElementString("serverName", favorite.ServerName);
            w.WriteElementString("url", favorite.Url);
            w.WriteElementString("name", favorite.Name);
            w.WriteElementString("notes", favorite.Notes);
            var tagsConverter = new TagsConverter();
            w.WriteElementString("tags", tagsConverter.ResolveTags(favorite));
            w.WriteElementString("newWindow", favorite.NewWindow.ToString());
            w.WriteElementString("toolBarIcon", favorite.ToolBarIcon);
            w.WriteElementString("bitmapPeristence", favorite.Protocol);
        }

        // ------------------------------------------------

        private void ExportCredentials(ExportOptionsContext context)
        {
            FavoriteConfigurationElement favorite = context.Favorite;

            var favoriteSecurity = new FavoriteConfigurationSecurity(_persistence, favorite);
            context.WriteElementString("credential", favorite.Credential);
            context.WriteElementString("domainName", favoriteSecurity.ResolveDomainName());

            if (context.IncludePasswords)
            {
                context.WriteElementString("userName", favoriteSecurity.ResolveUserName());
                context.WriteElementString("password", favoriteSecurity.Password);
            }
        }

        // ------------------------------------------------

        private static void ExportExecuteBeforeConnect(XmlTextWriter w, FavoriteConfigurationElement favorite)
        {
            if (favorite.ExecuteBeforeConnect)
            {
                w.WriteElementString("executeBeforeConnect", favorite.ExecuteBeforeConnect.ToString());
                w.WriteElementString("executeBeforeConnectCommand", favorite.ExecuteBeforeConnectCommand);
                w.WriteElementString("executeBeforeConnectArgs", favorite.ExecuteBeforeConnectArgs);
                w.WriteElementString("executeBeforeConnectInitialDirectory", favorite.ExecuteBeforeConnectInitialDirectory);
                w.WriteElementString("executeBeforeConnectWaitForExit", favorite.ExecuteBeforeConnectWaitForExit.ToString());
            }
        }
    }
}
