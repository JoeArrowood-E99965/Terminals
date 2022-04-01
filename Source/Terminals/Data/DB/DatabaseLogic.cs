using System;
using System.Data;
using System.Linq;
using System.Data.Common;
using System.Data.Entity;
using System.Data.Objects;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;

using Terminals.Common.Connections;

namespace Terminals.Data.DB
{
    internal partial class Database : DbContext
    {
        /// <summary>
        ///     Gets this instance connector for cached entities
        /// </summary>
        
        public CacheConnector Cache { get; private set; }

        // ------------------------------------------------

        internal Database(DbConnection connection)
            : base(connection, true)
        {
            // todo disable change tracking, we use the context disconnected, but implementation has to be fixed

            //Configuration.ProxyCreationEnabled = false
            //Configuration.AutoDetectChangesEnabled = false

            Groups = Set<DbGroup>();
            Favorites = Set<DbFavorite>();
            Security = Set<DbSecurityOptions>();
            CredentialBase = Set<DbCredentialBase>();
            DisplayOptions = Set<DbDisplayOptions>();
            BeforeConnectExecute = Set<DbBeforeConnectExecute>();

            Cache = new CacheConnector(this);
        }

        // ------------------------------------------------

        internal void SaveImmediatelyIfRequested()
        {
            // don't ask, save immediately. Here is no benefit to save in batch like in FilePersistence
            SaveChanges();
        }

        // ------------------------------------------------

        public override int SaveChanges()
        {
            IEnumerable<DbFavorite> changedFavorites = GetChangedOrAddedFavorites();

            // add to database first, otherwise the favorite properties cant be committed.

            int returnValue = base.SaveChanges();
            SaveFavoriteDetails(changedFavorites);
            return returnValue;
        }

        // ------------------------------------------------

        private void SaveFavoriteDetails(IEnumerable<DbFavorite> changedFavorites)
        {
            foreach(DbFavorite favorite in changedFavorites)
            {
                favorite.SaveDetails(this);
            }
        }

        // ------------------------------------------------

        private IEnumerable<DbFavorite> GetChangedOrAddedFavorites()
        {
            return ChangeTracker.Entries<DbFavorite>()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                .Select(change => change.Entity)
                .ToList();
        }

        // ------------------------------------------------

        internal byte[] GetFavoriteIcon(int favoriteId)
        {
            byte[] obtained = GetFavoriteIcon((int?)favoriteId).FirstOrDefault();

            if(obtained != null)
            {
                return obtained;
            }

            return FavoriteIcons.EmptyImageData;
        }

        // ------------------------------------------------

        internal string GetProtocolPropertiesByFavorite(int favoriteId)
        {
            return GetFavoriteProtocolProperties(favoriteId).FirstOrDefault();
        }

        // ------------------------------------------------

        internal string GetMasterPasswordHash()
        {
            string obtained = GetMasterPasswordKey().FirstOrDefault();

            if(obtained != null)
            {
                return obtained;
            }

            return String.Empty;
        }

        // ------------------------------------------------

        internal void UpdateMasterPassword(string newMasterPasswordKey)
        {
            UpdateMasterPasswordKey(newMasterPasswordKey);
        }

        // ------------------------------------------------

        internal List<int> GetRdpFavoriteIds()
        {
            return Favorites.Where(candidate => candidate.Protocol == KnownConnectionConstants.RDP)
                       .Select(rdpFavorite => rdpFavorite.Id).ToList();
        }

        // ------------------------------------------------

        internal void AddAll(IEnumerable<DbFavorite> favorites)
        {
            foreach(DbFavorite favorite in favorites)
            {
                Favorites.Add(favorite);
            }
        }

        // ------------------------------------------------
        /// <summary>
        ///     we have to delete the credentials base manually, this property uses lazy creation 
        ///     and therefore there is no database constraint
        /// </summary>

        internal void RemoveRedundantCredentialBase(List<DbCredentialBase> redundantCredentialBase)
        {
            foreach(DbCredentialBase credentialBase in redundantCredentialBase)
            {
                CredentialBase.Remove(credentialBase);
            }
        }

        // ------------------------------------------------

        internal void DeleteAll(IEnumerable<DbFavorite> favorites)
        {
            foreach(DbFavorite favorite in favorites)
            {
                Favorites.Remove(favorite);
            }
        }

        // ------------------------------------------------

        internal void AddToGroups(DbGroup toAdd)
        {
            toAdd.FieldsToReferences();

            if(toAdd.ParentGroup != null)
            {
                Cache.Attach(toAdd.ParentGroup);
            }

            Groups.Add(toAdd);
        }

        // ------------------------------------------------

        internal List<IGroup> AddToDatabase(List<IGroup> groups)
        {
            // not added groups don't have an identifier obtained from database

            List<IGroup> added = groups.Where(candidate => ((DbGroup)candidate).Id == 0).ToList();
            AddAll(added);
            List<DbGroup> toAttach = groups.Where(candidate => ((DbGroup)candidate).Id != 0).Cast<DbGroup>().ToList();
            Cache.AttachAll(toAttach);
            return added;
        }

        // ------------------------------------------------

        private void AddAll(List<IGroup> added)
        {
            foreach(DbGroup group in added)
            {
                Groups.Add(group);
            }
        }

        // ------------------------------------------------

        internal void DeleteAll(IEnumerable<DbGroup> groups)
        {
            foreach(DbGroup group in groups)
            {
                Groups.Remove(group);
            }
        }

        // ------------------------------------------------

        internal void RefreshEntity<TEntity>(TEntity toUpdate) where TEntity : class
        {
            Entry(toUpdate).Reload();
            ((IObjectContextAdapter)this).ObjectContext.Refresh(RefreshMode.ClientWins, toUpdate);
        }
    }
}