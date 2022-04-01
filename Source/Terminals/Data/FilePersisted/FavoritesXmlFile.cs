using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Terminals.Data.FilePersisted
{
    internal class FavoritesXmlFile
    {
        private const string FAVORITESINGROUP = "//t:FavoritesInGroup";

        private readonly XDocument _document;
        private readonly XmlNamespaceManager _namespaceManager;

        // ------------------------------------------------

        private FavoritesXmlFile(XDocument document)
        {
            _document = document;
            _namespaceManager = CreateNameSpaceManager();
        }

        // ------------------------------------------------

        private static XmlNamespaceManager CreateNameSpaceManager()
        {
            var nameSpaceManager = new XmlNamespaceManager(new NameTable());
            nameSpaceManager.AddNamespace("t", "http://Terminals.codeplex.com");
            nameSpaceManager.AddNamespace("xsi", "http://www.w3.org/2001/XMLSchema-instance");
            nameSpaceManager.AddNamespace("xsd", "http://www.w3.org/2001/XMLSchema");

            return nameSpaceManager;
        }

        // ------------------------------------------------

        internal static FavoritesXmlFile LoadXmlDocument(string fileLocation)
        {
            var doc = XDocument.Load(fileLocation);
            return CreateDocument(doc);
        }

        // ------------------------------------------------

        internal static FavoritesXmlFile CreateDocument(XDocument source)
        {
            return new FavoritesXmlFile(source);
        }

        // ------------------------------------------------

        internal UnknonwPluginElements RemoveUnknownFavorites(string[] availableProtocols)
        {
            var favorites = SelectElements("//t:Favorite");
            var unknownFavorites = favorites.Where(f => IsUnknownProtocol(f, availableProtocols)).ToList();
            unknownFavorites.ForEach(f => f.Remove());
            var groupMembership = SelectElements(FAVORITESINGROUP);
            var unknownMemberships = FilterGroupMembeship(groupMembership, unknownFavorites);

            return new UnknonwPluginElements(unknownFavorites, unknownMemberships);
        }

        // ------------------------------------------------

        private Dictionary<string, List<XElement>> FilterGroupMembeship(IEnumerable<XElement> favoritesInGroups, List<XElement> unknownFavorites)
        {
            var unknownFavoriteIds = unknownFavorites.Select(f => f.Attribute("id").Value).ToArray();
            return favoritesInGroups.ToDictionary(FindGroupId, fg => FilterUnknownFavoritesForGroup(fg, unknownFavoriteIds));
        }

        // ------------------------------------------------

        private List<XElement> FilterUnknownFavoritesForGroup(XElement favoritesInGroup, string[] unknownFavoriteIds)
        {
            return favoritesInGroup.XPathSelectElements("//t:guid", _namespaceManager)
                .Where(guid => unknownFavoriteIds.Contains(guid.Value))
                .Select(ExtractFavoriteGuid)
                .ToList();
        }

        // ------------------------------------------------

        private XElement ExtractFavoriteGuid(XElement unknownFavorite)
        {
            unknownFavorite.Remove();
            return unknownFavorite;
        }

        // ------------------------------------------------

        private bool IsUnknownProtocol(XElement favoriteElement, string[] availableProtocols)
        {
            var protocol = favoriteElement.XPathSelectElements("t:Protocol", _namespaceManager).First();
            return !availableProtocols.Contains(protocol.Value);
        }

        // ------------------------------------------------

        internal XmlReader CreateReader()
        {
            return _document.CreateReader();
        }

        // ------------------------------------------------

        internal void AppenUnknownContent(UnknonwPluginElements unknownElements)
        {
            AppendUnknownFavorites(unknownElements.Favorites);
            AppenUnknownGroupMembership(unknownElements.GroupMembership);
        }

        // ------------------------------------------------

        private void AppenUnknownGroupMembership(Dictionary<string, List<XElement>> unknownFavoritesInGroup)
        {
            foreach(var favoritesInGroup in SelectElements(FAVORITESINGROUP))
            {
                AddUnknownFavoritesToGroup(unknownFavoritesInGroup, favoritesInGroup);
            }
        }

        // ------------------------------------------------

        private void AddUnknownFavoritesToGroup(Dictionary<string, List<XElement>> unknownFavoritesInGroup, XElement favoritesInGroup)
        {
            var groupId = FindGroupId(favoritesInGroup);
            List<XElement> toAdd = null;

            if(unknownFavoritesInGroup.TryGetValue(groupId, out toAdd))
            {
                // --------------------------------------------------------------
                // missing backslash is not a mistake: search inside the element.

                var favorites = SelectElements(favoritesInGroup, "t:Favorites").First();
                favorites.Add(toAdd);
            }
        }

        // ------------------------------------------------

        private static string FindGroupId(XElement favoritesInGroup)
        {
            return favoritesInGroup.Attribute("groupId").Value;
        }

        // ------------------------------------------------

        private void AppendUnknownFavorites(List<XElement> unknownFavorites)
        {
            var favorites = SelectElements("//t:Favorites").First();
            favorites.Add(unknownFavorites);
        }

        // ------------------------------------------------

        private IEnumerable<XElement> SelectElements(string filter)
        {
            return SelectElements(_document, filter);
        }

        // ------------------------------------------------

        private IEnumerable<XElement> SelectElements(XNode target, string filter)
        {
            return target.XPathSelectElements(filter, _namespaceManager);
        }
    }
}