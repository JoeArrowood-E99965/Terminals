using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace Terminals.Data
{
    /// ---------------------------------------------------
    /// <summary>
    ///     In previous versions Groups and Tags.
    ///     Now both features are solved here.
    /// </summary>

    internal class Groups : IGroups, IFavoriteGroups
    {
        private readonly DataDispatcher _dispatcher;
        private readonly FilePersistence _persistence;
        private readonly Dictionary<Guid, IGroup> _cache;

        // ------------------------------------------------

        internal Groups(FilePersistence persistence)
        {
            _persistence = persistence;
            _dispatcher = persistence.Dispatcher;
            _cache = new Dictionary<Guid, IGroup>();
        }

        // ------------------------------------------------

        private bool AddToCache(Group group)
        {
            if(group == null || _cache.ContainsKey(group.Id))
            {
                return false;
            }

            _cache.Add(group.Id, group);
            return true;
        }

        // ------------------------------------------------

        internal List<IGroup> AddAllToCache(IEnumerable<IGroup> groups)
        {
            var added = new List<IGroup>();
            
            if(groups == null)
            {
                return added;
            }

            foreach(Group group in groups)
            {
                if(AddToCache(group))
                {
                    added.Add(group);
                }
            }

            return added;
        }

        // ------------------------------------------------

        private List<IGroup> DeleteFromCache(List<IGroup> groups)
        {
            var deleted = new List<IGroup>();
            if(groups == null)
                return deleted;

            foreach(Group group in groups)
            {
                if(DeleteFromCache(group))
                {
                    deleted.Add(group);
                }
            }

            return deleted;
        }

        // ------------------------------------------------

        private bool DeleteFromCache(Group group)
        {
            if(IsNotCached(group))
            {
                return false;
            }

            _cache.Remove(group.Id);
            return true;
        }

        // ------------------------------------------------

        private List<IGroup> GetEmptyGroups()
        {
            return _cache.Values
                .Where(group => group.Favorites.Count == 0)
                .ToList();
        }

        // ------------------------------------------------

        internal List<IGroup> Merge(List<IGroup> newGroups)
        {
            List<IGroup> oldGroups = this.ToList();
            List<IGroup> addedGroups = ListsHelper.GetMissingSourcesInTarget(newGroups, oldGroups);
            List<IGroup> deletedGroups = ListsHelper.GetMissingSourcesInTarget(oldGroups, newGroups);

            addedGroups = AddAllToCache(addedGroups);
            _dispatcher.ReportGroupsAdded(addedGroups);

            deletedGroups = DeleteFromCache(deletedGroups);
            _dispatcher.ReportGroupsDeleted(deletedGroups);

            return addedGroups;
        }

        /// -----------------------------------------------
        /// <summary>
        ///     Deletes all favorites from all groups and 
        ///     removes empty groups after that.
        ///     Also fires removed groups event.
        /// </summary>

        internal void DeleteFavoritesFromAllGroups(List<IFavorite> favoritesToRemove)
        {
            if(favoritesToRemove == null)
                return;

            RemoveFavoritesFromGroups(favoritesToRemove, this);
        }

        // ------------------------------------------------

        internal static void RemoveFavoritesFromGroups(List<IFavorite> favoritesToRemove, IEnumerable<IGroup> groups)
        {
            foreach(IGroup group in groups)
            {
                group.RemoveFavorites(favoritesToRemove);
            }
        }

        /// -----------------------------------------------
        /// <summary>
        ///     Gets group by its name. If there are more than 
        ///     one with this name returns the first found.
        ///     If there is no group with such name, returns null. 
        ///     Search isn't case sensitive.
        ///     Use this only to identify, if group with required 
        ///     name isn't already present, to prevent name duplicities.
        /// </summary>

        public IGroup this[string groupName]
        {
            get
            {
                return _cache.Values
                    .FirstOrDefault(group => group.Name
                        .Equals(groupName, StringComparison.CurrentCultureIgnoreCase));
            }
        }

        /// -----------------------------------------------
        /// <summary>
        ///     Gets a group by its unique identifier. 
        ///     Returns null, if the identifier is unknown.
        /// </summary>

        internal IGroup this[Guid groupId]
        {
            get
            {
                if(_cache.ContainsKey(groupId))
                {
                    return _cache[groupId];
                }

                return null;
            }
        }

        // ------------------------------------------------

        public void Add(IGroup group)
        {
            if(AddToCache(group as Group))
            {
                _dispatcher.ReportGroupsAdded(new List<IGroup> { group });
                _persistence.SaveImmediatelyIfRequested();
            }
        }

        // ------------------------------------------------

        public void Update(IGroup group)
        {
            if(UpdateInCache(group as Group))
            {
                _dispatcher.ReportGroupsUpdated(new List<IGroup> { group });
                _persistence.SaveImmediatelyIfRequested();
            }
        }

        // ------------------------------------------------

        private bool UpdateInCache(Group group)
        {
            if(IsNotCached(group))
            {
                return false;
            }

            _cache[group.Id] = group;
            return true;
        }

        // ------------------------------------------------

        private bool IsNotCached(Group group)
        {
            return group == null || !_cache.ContainsKey(group.Id);
        }

        // ------------------------------------------------

        public void Delete(IGroup group)
        {
            var toRemove = group as Group;

            if(DeleteFromCache(toRemove))
            {
                var changedFavorites = group.Favorites;

                RemoveChildGroupsParent(toRemove);
                _dispatcher.ReportGroupsDeleted(new List<IGroup> { group });
                _dispatcher.ReportFavoritesUpdated(changedFavorites);
                _persistence.SaveImmediatelyIfRequested();
            }
        }

        // ------------------------------------------------

        private void RemoveChildGroupsParent(Group group)
        {
            var childs = GetChildGroups(group);
            SetParentToRoot(childs);
            _dispatcher.ReportGroupsUpdated(childs);
        }

        // ------------------------------------------------

        private static void SetParentToRoot(List<IGroup> childs)
        {
            foreach(IGroup child in childs)
            {
                child.Parent = null;
            }
        }

        // ------------------------------------------------

        private List<IGroup> GetChildGroups(Group group)
        {
            // Search by id, because the parent already cant be obtained from cache

            return _cache.Values.Where(candidate => group.Id == ((Group)candidate).Parent).ToList();
        }

        // ------------------------------------------------

        public List<IGroup> GetGroupsContainingFavorite(Guid favoriteId)
        {
            return _cache.Values.Where(group => group.Favorites.Select(favorite => favorite.Id).Contains(favoriteId)).ToList();
        }

        // ------------------------------------------------

        public void Rebuild()
        {
            List<IGroup> emptyGroups = GetEmptyGroups();
            DeleteFromCache(emptyGroups);

            _dispatcher.ReportGroupsDeleted(emptyGroups);
            _persistence.SaveImmediatelyIfRequested();
        }

        // ------------------------------------------------

        public IEnumerator<IGroup> GetEnumerator()
        {
            return _cache.Values.GetEnumerator();
        }

        // ------------------------------------------------

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
