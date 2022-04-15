using System.Xml;
using Terminals.Configuration;

namespace Terminals.Integration.Export
{
    public class ExportOptionsContext : IExportOptionsContext
    {
        private readonly FavoriteConfigurationSecurity _favoriteSecurity;

        public XmlTextWriter Writer { get; private set; }

        public bool IncludePasswords { get; private set; }

        public FavoriteConfigurationElement Favorite { get; private set; }

        public string TsgwPassword { get { return _favoriteSecurity.TsgwPassword; } }

        // ------------------------------------------------

        internal ExportOptionsContext(XmlTextWriter writer, FavoriteConfigurationSecurity favoriteSecurity,
                                      bool includePasswords, FavoriteConfigurationElement favorite)
        {
            Writer = writer;
            Favorite = favorite;
            IncludePasswords = includePasswords;
            _favoriteSecurity = favoriteSecurity;
        }

        // ------------------------------------------------

        public void WriteElementString(string elementName, string elementValue)
        {
            Writer.WriteElementString(elementName, elementValue);
        }
    }
}