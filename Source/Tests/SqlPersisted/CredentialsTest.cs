using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Terminals.Data;
using Terminals.Data.DB;
using Terminals.Configuration;
using Terminals.Data.Credentials;

namespace Tests.SqlPersisted
{
    /// -----------------------------------------------
    /// <summary>
    ///     This is a test class for database 
    ///     implementation of StoredCredentials
    /// </summary>

    [TestClass]
    public class CredentialsTest : TestsLab
    {
        private ICredentials PrimaryCredentials
        {
            get
            {
                return PrimaryPersistence.Credentials;
            }
        }

        // ------------------------------------------------

        private IQueryable<DbCredentialSet> CheckDatabaseCredentials
        {
            get
            {
                return CheckDatabase.CredentialBase
                    .OfType<DbCredentialSet>();
            }
        }

        // ------------------------------------------------

        [TestInitialize]
        public void TestInitialize()
        {
            InitializeTestLab();
        }

        // ------------------------------------------------

        [TestCleanup]
        public void TestClose()
        {
            ClearTestLab();
        }

        // ------------------------------------------------

        private DbCredentialSet CreateTestCredentialSet()
        {
            var credentials = PrimaryFactory.CreateCredentialSet() as DbCredentialSet;
            credentials.Name = "TestCredentialName";

            return credentials;
        }

        // ------------------------------------------------

        [TestMethod]
        public void AddCredentialsTest()
        {
            AddTestCredentialsToDatabase();

            var checkCredentialSet = SecondaryPersistence.Credentials.FirstOrDefault() as DbCredentialSet;
            string resolvedPassword = ResolveVerifiedPassword(checkCredentialSet);

            Assert.IsNotNull(checkCredentialSet, "Credential didn't reach the database");
            Assert.AreEqual(VALIDATION_VALUE, resolvedPassword, "Password doesn't match");
        }

        // ------------------------------------------------

        [TestMethod]
        public void RemoveCredentialsTest()
        {
            var testCredentials = AddTestCredentialsToDatabase();

            int credentialsCountBefore = CheckDatabaseCredentials.Count();
            PrimaryCredentials.Remove(testCredentials);
            int credentialsCountAfter = CheckDatabaseCredentials.Count();

            int baseAfter = CheckDatabase.Database
                .SqlQuery<int>("select Count(Id) from CredentialBase")
                .FirstOrDefault();

            Assert.AreEqual(1, credentialsCountBefore, "credential wasn't added to the database");
            Assert.AreEqual(0, credentialsCountAfter, "credential wasn't removed from the database");
            Assert.AreEqual(0, baseAfter, "credential base wasn't removed from the database");
        }

        // ------------------------------------------------

        private DbCredentialSet AddTestCredentialsToDatabase()
        {
            var testCredentials = CreateTestCredentialSet();
            PrimaryCredentials.Add(testCredentials);
            return testCredentials;
        }

        // ------------------------------------------------
        /// <summary>
        ///     A test for UpdatePasswordsByNewKeyMaterial
        /// </summary>

        [TestMethod]
        public void UpdateCredentialsPasswordsByNewKeyMaterialTest()
        {
            // this is the only one test, which plays with different master passwords

            Settings.Instance.PersistenceSecurity = PrimaryPersistence.Security;
            AddTestCredentialsToDatabase();
            PrimaryPersistence.Security.UpdateMasterPassword(VALIDATION_VALUE_B);

            ICredentialSet checkCredentials = SecondaryPersistence.Credentials.FirstOrDefault();
            string resolvedPassword = ResolveVerifiedPassword(checkCredentials);

            Assert.AreEqual(VALIDATION_VALUE, resolvedPassword, "Password lost after update of key material");
        }

        // ------------------------------------------------

        private string ResolveVerifiedPassword(ICredentialSet checkCredentialSet)
        {
            var guarded = new GuardedCredential(checkCredentialSet, SecondaryPersistence.Security);
            return guarded.Password;
        }
    }
}
